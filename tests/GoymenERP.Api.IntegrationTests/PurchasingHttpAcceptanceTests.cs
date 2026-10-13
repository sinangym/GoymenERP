using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>The purchasing routes' wire contract over a stub service (the Sales / Logistics HTTP acceptance pattern): metadata, statuses, headers, code boundary.</summary>
public sealed class PurchasingHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid PurchaseOrderId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid AcceptanceId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid PurchaseOrderLineId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid ProductId = Guid.Parse("40000000-0000-0000-0000-000000000004");
    private static readonly Guid UnitId = Guid.Parse("50000000-0000-0000-0000-000000000005");

    [Theory]
    [InlineData("permission_unregistered")]
    [InlineData("counterparty_provider_unavailable")]
    [InlineData("product_eligibility_unavailable")]
    [InlineData("reference_provider_invalid")]
    [InlineData("service_unavailable")]
    public async Task A_503_of_the_service_reaches_the_wire_only_as_the_platforms_public_code(string sourceCode)
    {
        var service = new StubService { CreateOrder = () => throw new PurchasingServiceException(503, sourceCode) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(PurchasingApiV1.OrdersPath, "SENTETIK-503", OrderBody()));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("service_unavailable", JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
        if (sourceCode != "service_unavailable")
        {
            Assert.DoesNotContain(sourceCode, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_created_order_answers_201_with_its_location_and_a_replay_carries_the_first_attempts_headers()
    {
        var id = Guid.NewGuid();
        var service = new StubService { CreateOrder = () => Task.FromResult(new PurchaseOrderResponse(OrderDetail(id), false, "SENTETIK-corr-1")) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var response = await client.SendAsync(Post(PurchasingApiV1.OrdersPath, "SENTETIK-created", OrderBody())))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(new Uri(client.BaseAddress!, $"{PurchasingApiV1.OrdersPath.TrimStart('/')}/{id:D}"), response.Headers.Location);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
            Assert.Equal(id, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("order").GetProperty("id").GetGuid());
        }

        service.CreateOrder = () => Task.FromResult(new PurchaseOrderResponse(OrderDetail(id), true, "SENTETIK-first"));
        using (var response = await client.SendAsync(Post(PurchasingApiV1.OrdersPath, "SENTETIK-created", OrderBody())))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        // A recorded terminal refusal of the approval (the Σ-shares rule, §7) replays with the same headers and its field errors.
        service.Approve = () => throw new PurchasingServiceException(409, PurchasingProblemCodesV1.RequestLineOverSourced,
            [new("lines[0].sourceRequestLineId", "purchasing_request_line_over_sourced")], isReplay: true, originalCorrelationId: "SENTETIK-first");
        using (var response = await client.SendAsync(Post($"{PurchasingApiV1.OrdersPath}/{id:D}/approval", "SENTETIK-approve", "{\"expectedVersion\":\"2\"}")))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(PurchasingProblemCodesV1.RequestLineOverSourced, problem.GetProperty("code").GetString());
            Assert.Equal("lines[0].sourceRequestLineId", problem.GetProperty("errors")[0].GetProperty("field").GetString());
        }

        // The approval after the transition answers 200 with the order.
        service.Approve = () => Task.FromResult(new PurchaseOrderResponse(OrderDetail(id, "Approved", "3"), false, "SENTETIK-corr-2"));
        using (var response = await client.SendAsync(Post($"{PurchasingApiV1.OrdersPath}/{id:D}/approval", "SENTETIK-approve-2", "{\"expectedVersion\":\"2\"}")))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Approved", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("order").GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task Read_routes_require_supplier_display_while_command_wire_shape_stays_unchanged()
    {
        var detail = OrderDetail(PurchaseOrderId);
        var service = new StubService
        {
            CreateOrder = () => Task.FromResult(new PurchaseOrderResponse(detail, false, "SENTETIK-create")),
            Approve = () => Task.FromResult(new PurchaseOrderResponse(detail, false, "SENTETIK-approve")),
        };
        var owner = new StubOrderRecordSource { Detail = detail };
        var displays = new StubSupplierDisplaySource();
        using var host = CreateHost(service, owner, displays);
        using var client = CreateClient(host);

        using (var listResponse = await client.GetAsync(PurchasingApiV1.OrdersPath))
        {
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
            var supplier = list.GetProperty("items")[0].GetProperty("supplierDisplay");
            Assert.Equal("TED-HTTP", supplier.GetProperty("code").GetString());
            Assert.Equal(detail.Order.SupplierPartyId, supplier.GetProperty("partyId").GetGuid());
        }

        using (var detailResponse = await client.GetAsync($"{PurchasingApiV1.OrdersPath}/{PurchaseOrderId:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
            var body = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Tedarikçi HTTP A.Ş.", body.GetProperty("order").GetProperty("supplierDisplay").GetProperty("legalName").GetString());
        }

        using (var createResponse = await client.SendAsync(Post(PurchasingApiV1.OrdersPath, "SENTETIK-read-shape-create", OrderBody())))
        {
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            var order = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("order");
            Assert.False(order.TryGetProperty("supplierDisplay", out _));
        }

        using (var approvalResponse = await client.SendAsync(Post(
                   $"{PurchasingApiV1.OrdersPath}/{PurchaseOrderId:D}/approval",
                   "SENTETIK-read-shape-approve",
                   "{\"expectedVersion\":\"2\"}")))
        {
            Assert.Equal(HttpStatusCode.OK, approvalResponse.StatusCode);
            var order = (await approvalResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("order");
            Assert.False(order.TryGetProperty("supplierDisplay", out _));
        }

        Assert.Equal(2, displays.Requests.Count);
    }

    [Fact]
    public async Task Order_list_accepts_one_exact_supplier_filter_while_request_list_rejects_it()
    {
        var supplierPartyId = Guid.Parse("60000000-0000-0000-0000-000000000006");
        var owner = new StubOrderRecordSource();
        using var host = CreateHost(new StubService(), owner, new StubSupplierDisplaySource());
        using var client = CreateClient(host);

        using (var response = await client.GetAsync($"{PurchasingApiV1.OrdersPath}?supplierPartyId={supplierPartyId:D}&status=Draft"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal((supplierPartyId, "Draft"), (owner.LastQuery?.SupplierPartyId, owner.LastQuery?.Status));
        }

        using (var response = await client.GetAsync($"{PurchasingApiV1.RequestsPath}?supplierPartyId={supplierPartyId:D}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var response = await client.GetAsync($"{PurchasingApiV1.OrdersPath}?supplierPartyId={Guid.Empty:D}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Historical_inactive_supplier_remains_visible_while_missing_relation_closes_list_and_detail()
    {
        var detail = OrderDetail(PurchaseOrderId);
        var service = new StubService();
        var owner = new StubOrderRecordSource { Detail = detail };
        var historical = new StubSupplierDisplaySource
        {
            Answer = request => new PurchasingSupplierDisplayBatchAnswerV1(
                PurchasingSupplierDisplayBatchOutcomeV1.Success,
                [.. request.PartyIds.Select(id => new PurchasingSupplierDisplayV1(
                    id,
                    "PASIF-HTTP",
                    "Pasif Tarihsel Tedarikçi",
                    null))]),
        };
        using (var historicalHost = CreateHost(service, owner, historical))
        using (var historicalClient = CreateClient(historicalHost))
        {
            using var listResponse = await historicalClient.GetAsync(PurchasingApiV1.OrdersPath);
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("PASIF-HTTP", list.GetProperty("items")[0].GetProperty("supplierDisplay").GetProperty("code").GetString());

            using var detailResponse = await historicalClient.GetAsync($"{PurchasingApiV1.OrdersPath}/{PurchaseOrderId:D}");
            Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
            var body = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Pasif Tarihsel Tedarikçi", body.GetProperty("order").GetProperty("supplierDisplay").GetProperty("legalName").GetString());
        }

        var missing = new StubSupplierDisplaySource
        {
            Answer = _ => new PurchasingSupplierDisplayBatchAnswerV1(
                PurchasingSupplierDisplayBatchOutcomeV1.Success,
                []),
        };
        using var missingHost = CreateHost(service, owner, missing);
        using var missingClient = CreateClient(missingHost);
        using var missingList = await missingClient.GetAsync(PurchasingApiV1.OrdersPath);
        using var missingDetail = await missingClient.GetAsync($"{PurchasingApiV1.OrdersPath}/{PurchaseOrderId:D}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, missingList.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, missingDetail.StatusCode);
        Assert.DoesNotContain(detail.Order.SupplierPartyId.ToString(), await missingList.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(detail.Order.SupplierPartyId.ToString(), await missingDetail.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_created_request_answers_201_under_the_requests_path_and_its_submission_200()
    {
        var id = Guid.NewGuid();
        var service = new StubService
        {
            CreateRequest = () => Task.FromResult(new PurchaseRequestResponse(RequestDetail(id, "Draft", "2"), false, "SENTETIK-corr-1")),
            Submit = () => Task.FromResult(new PurchaseRequestResponse(RequestDetail(id, "Submitted", "3"), false, "SENTETIK-corr-2")),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var response = await client.SendAsync(Post(PurchasingApiV1.RequestsPath, "SENTETIK-req", RequestBody())))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(new Uri(client.BaseAddress!, $"{PurchasingApiV1.RequestsPath.TrimStart('/')}/{id:D}"), response.Headers.Location);
        }

        using (var response = await client.SendAsync(Post($"{PurchasingApiV1.RequestsPath}/{id:D}/submission", "SENTETIK-submit", "{\"expectedVersion\":\"2\"}")))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Submitted", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("request").GetProperty("status").GetString());
        }

        Assert.Equal(2, service.Calls);
    }

    [Fact]
    public async Task Order_detail_exposes_only_the_ordered_immutable_acceptance_history()
    {
        var first = Guid.Parse("61000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("61000000-0000-0000-0000-000000000002");
        var firstAt = new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
        var baseDetail = OrderDetail(PurchaseOrderId, "Approved", "5");
        var line = Assert.Single(baseDetail.Lines).LineId;
        var detail = baseDetail with
        {
            Acceptances =
            [
                new PurchaseOrderAcceptanceHistoryView(first, line, "40", firstAt),
                new PurchaseOrderAcceptanceHistoryView(second, line, "25", firstAt.AddMinutes(1)),
            ],
        };
        var service = new StubService();
        var owner = new StubOrderRecordSource { Detail = detail };
        using var host = CreateHost(service, owner, new StubSupplierDisplaySource());
        using var client = CreateClient(host);

        using var response = await client.GetAsync($"{PurchasingApiV1.OrdersPath}/{PurchaseOrderId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "order", "lines", "acceptances" }, body.RootElement.EnumerateObject().Select(property => property.Name));
        var acceptances = body.RootElement.GetProperty("acceptances");
        Assert.Equal(2, acceptances.GetArrayLength());
        Assert.Equal(new[] { "acceptanceId", "lineId", "quantity", "recordedAtUtc" },
            acceptances[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal((first, line, "40", firstAt),
            (acceptances[0].GetProperty("acceptanceId").GetGuid(), acceptances[0].GetProperty("lineId").GetGuid(),
                acceptances[0].GetProperty("quantity").GetString(), acceptances[0].GetProperty("recordedAtUtc").GetDateTimeOffset()));
        Assert.Equal(second, acceptances[1].GetProperty("acceptanceId").GetGuid());
        string payload = body.RootElement.GetRawText();
        foreach (string forbidden in new[] { "price", "currency", "tax", "payment", "schedule", "invoice", "openItem" })
        {
            Assert.DoesNotContain(forbidden, payload, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(404, "not_found")]
    [InlineData(403, "forbidden")]
    [InlineData(503, "service_unavailable")]
    public async Task Order_detail_failures_release_no_acceptance_history(int status, string code)
    {
        var protectedAcceptance = Guid.Parse("63000000-0000-0000-0000-000000000001");
        var service = new StubService();
        var owner = new StubOrderRecordSource
        {
            GetOrder = _ => throw new PurchasingServiceException(status, code),
        };
        using var host = CreateHost(service, owner, new StubSupplierDisplaySource());
        using var client = CreateClient(host);

        using var response = await client.GetAsync($"{PurchasingApiV1.OrdersPath}/{PurchaseOrderId:D}");

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        AssertPrivateNoStore(response);
        string payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(PurchaseOrderId.ToString(), payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(protectedAcceptance.ToString(), payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acceptances", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_missing_key_a_wrong_media_type_and_a_loose_body_are_refused_before_the_service()
    {
        var service = new StubService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var none = new HttpRequestMessage(HttpMethod.Post, PurchasingApiV1.OrdersPath) { Content = new StringContent(OrderBody(), Encoding.UTF8, "application/json") })
        using (var response = await client.SendAsync(none))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(PurchasingProblemCodesV1.CommandMetadataInvalid, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        using (var text = Post(PurchasingApiV1.OrdersPath, "SENTETIK-text", OrderBody(), "text/plain"))
        using (var response = await client.SendAsync(text))
        {
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }

        using (var loose = Post($"{PurchasingApiV1.OrdersPath}/{Guid.NewGuid():D}/approval", "SENTETIK-loose", "{\"expectedVersion\":\"1\",\"reason\":\"x\"}"))
        using (var response = await client.SendAsync(loose))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_failed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        // A bound line without a source request is a shape error, refused before the service.
        using (var unbound = Post(PurchasingApiV1.OrdersPath, "SENTETIK-unbound", OrderBody(sourceRequest: false, boundLine: true)))
        using (var response = await client.SendAsync(unbound))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Goods_receipt_quantity_comparison_returns_exact_contract_and_private_cache_headers()
    {
        var service = new StubService
        {
            Comparison = (purchaseOrderId, acceptanceId) => Task.FromResult(new PurchaseGoodsReceiptQuantityComparisonV1(
                CompanyA, acceptanceId, purchaseOrderId, PurchaseOrderLineId, "7", ProductId, "3", UnitId,
                "100.25", "101.75", "SameKilogramUnit", "100.25", "1.5"))
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(ComparisonPath(PurchaseOrderId, AcceptanceId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            new[]
            {
                "companyId", "acceptanceId", "purchaseOrderId", "purchaseOrderLineId",
                "purchaseOrderVersion", "productId", "productVersion", "unitId",
                "acceptedQuantity", "measuredQuantityKg", "comparisonStatus",
                "acceptedQuantityKg", "measuredMinusAcceptedKg"
            },
            body.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(CompanyA, body.RootElement.GetProperty("companyId").GetGuid());
        Assert.Equal(AcceptanceId, body.RootElement.GetProperty("acceptanceId").GetGuid());
        Assert.Equal("1.5", body.RootElement.GetProperty("measuredMinusAcceptedKg").GetString());
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task Goods_receipt_quantity_comparison_unknown_or_foreign_record_is_code_less_not_found()
    {
        var service = new StubService
        {
            Comparison = (_, _) => throw new PurchasingServiceException(404, "not_found")
        };
        using var host = CreateHost(service);
        using var unknownClient = CreateClient(host);
        using var foreignClient = CreateClient(host, TestUsers.CompanyB, TestCompanyIds.CompanyB);

        foreach (var client in new[] { unknownClient, foreignClient })
        {
            using var response = await client.GetAsync(ComparisonPath(PurchaseOrderId, AcceptanceId));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            AssertPrivateNoStore(response);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.False(body.RootElement.TryGetProperty("code", out _));
        }

        Assert.Equal(2, service.Calls);
    }

    [Fact]
    public async Task Goods_receipt_quantity_comparison_maps_invalid_source_to_public_service_unavailable()
    {
        var service = new StubService
        {
            Comparison = (_, _) => throw new PurchasingServiceException(503, PurchaseGoodsReceiptCodesV1.SourceInvalid)
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(ComparisonPath(PurchaseOrderId, AcceptanceId));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertPrivateNoStore(response);
        var payload = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(payload);
        Assert.Equal("service_unavailable", body.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(PurchaseGoodsReceiptCodesV1.SourceInvalid, payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Goods_receipt_quantity_comparison_late_authority_failure_exposes_no_protected_data()
    {
        var service = new StubService
        {
            Comparison = (_, _) => throw new PurchasingServiceException(403, "forbidden")
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(ComparisonPath(PurchaseOrderId, AcceptanceId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertPrivateNoStore(response);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(PurchaseOrderId.ToString(), payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AcceptanceId.ToString(), payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ProductId.ToString(), payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Goods_receipt_detail_reopens_the_exact_stored_receipt_with_private_cache_headers()
    {
        var receipt = ReceiptDetail();
        var service = new StubService
        {
            Receipt = (orderId, acceptanceId) =>
            {
                Assert.Equal(PurchaseOrderId, orderId);
                Assert.Equal(AcceptanceId, acceptanceId);
                return Task.FromResult(new PurchaseGoodsReceiptDetailV1(receipt));
            }
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        using var response = await client.GetAsync(ReceiptPath(PurchaseOrderId, AcceptanceId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        var actual = await response.Content.ReadFromJsonAsync<PurchaseGoodsReceiptDetailV1>();
        Assert.Equal(receipt, actual?.Receipt);
        Assert.Equal(1, service.Calls);
        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
    }

    [Theory]
    [InlineData(403, "forbidden")]
    [InlineData(404, "not_found")]
    [InlineData(503, "purchasing_goods_receipt_source_invalid")]
    public async Task Goods_receipt_detail_refusal_exposes_no_protected_record(int status, string code)
    {
        var service = new StubService { Receipt = (_, _) => throw new PurchasingServiceException(status, code) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        using var response = await client.GetAsync(ReceiptPath(PurchaseOrderId, AcceptanceId));

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        AssertPrivateNoStore(response);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(PurchaseOrderId.ToString(), payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AcceptanceId.ToString(), payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SENTETIK-RECEIPT-DETAIL", payload, StringComparison.Ordinal);
        if (status == 503) Assert.DoesNotContain(code, payload, StringComparison.Ordinal);
        else if (status == 403)
            Assert.Equal("forbidden", JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString());
        else Assert.False(JsonDocument.Parse(payload).RootElement.TryGetProperty("code", out _));
    }

    [Fact]
    public async Task Goods_receipt_detail_rejects_unknown_queries_before_owner_read_and_requires_authentication()
    {
        var service = new StubService { Receipt = (_, _) => Task.FromResult(new PurchaseGoodsReceiptDetailV1(ReceiptDetail())) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        using var invalid = await client.GetAsync(ReceiptPath(PurchaseOrderId, AcceptanceId) + "?unknown=1");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        AssertPrivateNoStore(invalid);
        using var anonymous = host.CreateClient();
        using var denied = await anonymous.GetAsync(ReceiptPath(PurchaseOrderId, AcceptanceId));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(0, service.Calls);
    }

    private static string ReceiptPath(Guid orderId, Guid acceptanceId) =>
        $"{PurchasingApiV1.OrdersPath}/{orderId:D}/goods-receipts/{acceptanceId:D}";

    private static PurchaseGoodsReceiptViewV1 ReceiptDetail() => new(
        CompanyA, AcceptanceId, PurchaseOrderId, PurchaseOrderLineId, "7", ProductId, "3", UnitId,
        "100.25", "400.75", Guid.NewGuid(), Guid.NewGuid(), "SENTETIK-RECEIPT-DETAIL", "1",
        Guid.NewGuid(), Guid.NewGuid(), "101.75", "Blocked", DateTimeOffset.Parse("2026-10-13T00:00:00Z"));

    private static string OrderBody(bool sourceRequest = true, bool boundLine = true)
    {
        var source = sourceRequest ? $"\"{Guid.NewGuid():D}\"" : "null";
        var bound = boundLine ? $"\"{Guid.NewGuid():D}\"" : "null";
        return $$"""{"supplierPartyId":"{{Guid.NewGuid():D}}","companyAccountId":"{{Guid.NewGuid():D}}","sourceRequestId":{{source}},"effectiveOn":"2026-09-19","lines":[{"lineId":"{{Guid.NewGuid():D}}","productId":"{{Guid.NewGuid():D}}","expectedProductVersion":"7","unitId":"{{Guid.NewGuid():D}}","quantity":"6","sourceRequestLineId":{{bound}}}]}""";
    }

    private static string RequestBody() =>
        $$"""{"effectiveOn":"2026-09-19","lines":[{"lineId":"{{Guid.NewGuid():D}}","productId":"{{Guid.NewGuid():D}}","expectedProductVersion":"7","unitId":"{{Guid.NewGuid():D}}","quantity":"6"}]}""";

    private static HttpRequestMessage Post(string path, string key, string json, string mediaType = "application/json")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, mediaType) };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static PurchaseOrderDetailView OrderDetail(Guid id, string status = "Draft", string version = "2") => new(
        new PurchaseOrderSummaryView(id, CompanyA, status, version, Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2026, 9, 19), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1),
        [new PurchaseOrderLineView(Guid.NewGuid(), 1, "General", null, null, Guid.NewGuid(), "7", Guid.NewGuid(), "6", "0", "6", "0", "6")]);

    private static PurchaseRequestDetailView RequestDetail(Guid id, string status, string version) => new(
        new PurchaseRequestSummaryView(id, CompanyA, status, version, new DateOnly(2026, 9, 19), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1),
        [new PurchaseRequestLineView(Guid.NewGuid(), 1, Guid.NewGuid(), "7", Guid.NewGuid(), "6")]);

    private WebApplicationFactory<Program> CreateHost(
        StubService service,
        StubOrderRecordSource? orderSource = null,
        StubSupplierDisplaySource? displaySource = null) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Scoped<IPurchasingService>(_ => service));
            services.Replace(ServiceDescriptor.Scoped<IPurchaseGoodsReceiptQuantityComparisonServiceV1>(_ => service));
            services.Replace(ServiceDescriptor.Scoped<IPurchaseGoodsReceiptServiceV1>(_ => service));
            if (orderSource is not null)
            {
                services.RemoveAll<IPurchasingOrderRecordReadSourceV1>();
                services.AddScoped<IPurchasingOrderRecordReadSourceV1>(_ => orderSource);
                services.RemoveAll<IErpAuthorityRevalidationService>();
                services.AddScoped<IErpAuthorityRevalidationService, StubAuthority>();
            }

            if (displaySource is not null)
            {
                services.RemoveAll<IPurchasingSupplierDisplaySourceV1>();
                services.AddScoped<IPurchasingSupplierDisplaySourceV1>(_ => displaySource);
            }
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
        => CreateClient(host, TestUsers.CompanyA, CompanyA);

    private static HttpClient CreateClient(WebApplicationFactory<Program> host, string userId, Guid companyId)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", userId);
        client.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString("D"));
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        return client;
    }

    private static string ComparisonPath(Guid purchaseOrderId, Guid acceptanceId) =>
        $"/api/v1/purchasing/orders/{purchaseOrderId:D}/goods-receipts/{acceptanceId:D}/quantity-comparison";

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    private sealed class StubService : IPurchasingService, IPurchaseGoodsReceiptQuantityComparisonServiceV1,
        IPurchaseGoodsReceiptServiceV1
    {
        public int Calls { get; private set; }
        public Func<Task<PurchaseRequestResponse>> CreateRequest { get; set; } = () => throw new InvalidOperationException("not expected");
        public Func<Task<PurchaseRequestResponse>> Submit { get; set; } = () => throw new InvalidOperationException("not expected");
        public Func<Task<PurchaseOrderResponse>> CreateOrder { get; set; } = () => throw new InvalidOperationException("not expected");
        public Func<Task<PurchaseOrderResponse>> Approve { get; set; } = () => throw new InvalidOperationException("not expected");
        public Func<Guid, Task<PurchaseOrderDetailView>> GetOrder { get; set; } = _ => throw new InvalidOperationException("not expected");
        public Func<Guid, Guid, Task<PurchaseGoodsReceiptQuantityComparisonV1>> Comparison { get; set; }
            = (_, _) => throw new InvalidOperationException("not expected");
        public Func<Guid, Guid, Task<PurchaseGoodsReceiptDetailV1>> Receipt { get; set; }
            = (_, _) => throw new InvalidOperationException("not expected");

        public Task<PurchaseGoodsReceiptDetailV1> GetGoodsReceiptAsync(Guid orderId, Guid acceptanceId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Receipt(orderId, acceptanceId);
        }

        public Task<PurchaseGoodsReceiptResponseV1> RecordGoodsReceiptAsync(Guid orderId, Guid lineId,
            RecordPurchaseGoodsReceiptV1 request, PurchasingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("not expected");

        public Task<PurchaseGoodsReceiptOptionsV1> GetGoodsReceiptOptionsAsync(Guid orderId, Guid lineId,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("not expected");

        public Task<PurchaseGoodsReceiptPageV1> GetGoodsReceiptsAsync(Guid orderId, int limit = 100,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("not expected");

        public Task<PurchaseGoodsReceiptQuantityComparisonV1> GetGoodsReceiptQuantityComparisonAsync(
            Guid purchaseOrderId, Guid acceptanceId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Comparison(purchaseOrderId, acceptanceId);
        }

        public Task<PurchaseRequestResponse> CreateRequestAsync(CreatePurchaseRequestRequest request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Calls++;
            return CreateRequest();
        }

        public Task<PurchaseRequestResponse> SubmitRequestAsync(Guid requestId, SubmitPurchaseRequestRequest request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Submit();
        }

        public Task<PurchaseRequestDetailView> GetRequestDetailAsync(Guid requestId, CancellationToken cancellationToken = default) =>
            throw new PurchasingServiceException(404, "not_found");

        public Task<PurchaseRequestListResponse> GetRequestListAsync(PurchaseRequestListQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PurchaseRequestListResponse([], null));

        public Task<PurchaseOrderResponse> CreateOrderAsync(CreatePurchaseOrderRequest request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Calls++;
            return CreateOrder();
        }

        public Task<PurchaseOrderResponse> ApproveOrderAsync(Guid orderId, ApprovePurchaseOrderRequest request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Approve();
        }

        public Task<PurchaseOrderDetailView> GetOrderDetailAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return GetOrder(orderId);
        }

        public Task<PurchaseOrderListResponse> GetOrderListAsync(PurchaseOrderListQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PurchaseOrderListResponse([], null));
    }

    private sealed class StubOrderRecordSource : IPurchasingOrderRecordReadSourceV1
    {
        public PurchaseOrderDetailView Detail { get; init; } = OrderDetail(PurchaseOrderId);
        public Func<Guid, Task<PurchaseOrderDetailView>>? GetOrder { get; init; }
        public PurchaseOrderListQuery? LastQuery { get; private set; }

        public Task<PurchaseOrderDetailView> GetOrderDetailAsync(
            Guid orderId,
            CancellationToken cancellationToken = default) =>
            GetOrder?.Invoke(orderId) ?? Task.FromResult(Detail);

        public Task<PurchaseOrderListResponse> GetOrderListAsync(
            PurchaseOrderListQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(new PurchaseOrderListResponse([Detail.Order], null));
        }
    }

    private sealed class StubSupplierDisplaySource : IPurchasingSupplierDisplaySourceV1
    {
        public List<PurchasingSupplierDisplayBatchRequestV1> Requests { get; } = [];
        public Func<PurchasingSupplierDisplayBatchRequestV1, PurchasingSupplierDisplayBatchAnswerV1>? Answer { get; init; }

        public Task<PurchasingSupplierDisplayBatchAnswerV1> GetAuthorizedBatchAsync(
            PurchasingSupplierDisplayBatchRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Answer is not null)
            {
                return Task.FromResult(Answer(request));
            }

            return Task.FromResult(new PurchasingSupplierDisplayBatchAnswerV1(
                PurchasingSupplierDisplayBatchOutcomeV1.Success,
                [.. request.PartyIds.Select(id => new PurchasingSupplierDisplayV1(
                    id,
                    "TED-HTTP",
                    "Tedarikçi HTTP A.Ş.",
                    "HTTP Metal"))]));
        }
    }

    private sealed class StubAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(
                true,
                true,
                new HashSet<string>(StringComparer.Ordinal) { "PURCHASING-HTTP" },
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                new HashSet<Guid>()));
    }
}
