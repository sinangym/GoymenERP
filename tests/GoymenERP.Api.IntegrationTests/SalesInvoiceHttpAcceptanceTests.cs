using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// S5 API-32 (Fatura cekirdegi 1): the invoice routes' HTTP boundary over a SENTETIK <see cref="ISalesInvoiceService"/> (no
/// database, no owner): the metadata and strict body checks that answer before the service, 201 + the absolute Location, the replay
/// headers of the same key again, the 415, the strict list query, the 404 of another company's invoice and the problem shapes
/// (409 with its code and field reasons; 403 / 404 / 503 code-less). The service's own rules are unit-tested; the store is the SQL
/// Server class. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesInvoiceHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-8444-444444444444");

    [Fact]
    public async Task A_created_invoice_answers_201_with_its_absolute_location_and_a_replay_carries_the_replay_headers()
    {
        var invoiceId = Guid.NewGuid();
        var service = new StubInvoiceService
        {
            Create = _ => Task.FromResult(new SalesInvoiceCommandResult(Detail(invoiceId, false), false, "SENTETIK-corr-1")),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var response = await client.SendAsync(Post("SENTETIK-invoice")))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(new Uri(client.BaseAddress!, $"{SalesEndpoints.InvoicesPath.TrimStart('/')}/{invoiceId:D}"), response.Headers.Location);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
            Assert.True(response.Headers.CacheControl is { NoStore: true, Private: true });
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(invoiceId, body.RootElement.GetProperty("id").GetGuid());
            Assert.Equal("Draft", body.RootElement.GetProperty("state").GetString());
        }

        service.Create = _ => Task.FromResult(new SalesInvoiceCommandResult(Detail(invoiceId, true), true, "SENTETIK-corr-1"));
        using (var replay = await client.SendAsync(Post("SENTETIK-invoice")))
        {
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-corr-1", Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        Assert.Equal(["SENTETIK-invoice", "SENTETIK-invoice"], service.Keys);
    }

    [Theory]
    [InlineData("missing-key")]
    [InlineData("blank-key")]
    [InlineData("comma-key")]
    [InlineData("not-json")]
    [InlineData("unknown-member")]
    [InlineData("null-share")]
    public async Task The_metadata_and_the_strict_body_answer_before_the_service(string fault)
    {
        var service = new StubInvoiceService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var json = fault switch
        {
            "unknown-member" => "{\"companyAccountId\":\"" + Account + "\",\"price\":\"1\",\"shares\":[{\"shipmentId\":\"" + Guid.NewGuid() + "\"}]}",
            "null-share" => "{\"companyAccountId\":\"" + Account + "\",\"shares\":[null]}",
            _ => JsonSerializer.Serialize(Body(), SalesDraftJson.Options),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.InvoicesPath)
        {
            Content = new StringContent(json, Encoding.UTF8, fault == "not-json" ? "text/plain" : "application/json"),
        };
        if (fault != "missing-key")
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", fault switch { "blank-key" => " ", "comma-key" => "a,b", _ => "SENTETIK-k" });
        }

        using var response = await client.SendAsync(request);

        if (fault == "not-json")
        {
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(fault.EndsWith("-key", StringComparison.Ordinal) ? "command_metadata_invalid" : "validation_failed",
                body.RootElement.GetProperty("code").GetString());
        }

        Assert.Empty(service.Keys);
    }

    [Fact]
    public async Task A_share_set_that_is_not_ready_is_one_409_with_its_code_and_every_reason_as_a_field_error()
    {
        var service = new StubInvoiceService
        {
            Create = _ => throw new SalesDraftServiceException(409, SalesInvoiceProblemCodesV1.InvoiceNotReady,
                [new("shares[0]", SalesInvoiceReasonCodesV1.ShareAlreadyInvoiced), new("shares[1]", SalesInvoiceReasonCodesV1.RateUnresolved)]),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post("SENTETIK-not-ready"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invoice_not_ready", body.RootElement.GetProperty("code").GetString());
        var errors = body.RootElement.GetProperty("errors").GetRawText();
        Assert.Contains("share_already_invoiced", errors, StringComparison.Ordinal);
        Assert.Contains("rate_unresolved", errors, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(403, "forbidden")]
    [InlineData(503, "permission_unregistered")]
    [InlineData(503, "dispatch_provider_unavailable")]
    public async Task Every_403_and_503_of_the_service_is_code_less_on_the_wire(int status, string code)
    {
        var service = new StubInvoiceService { Create = _ => throw new SalesDraftServiceException(status, code) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post("SENTETIK-refused"));

        Assert.Equal(status, (int)response.StatusCode);
        // The platform writer answers 403 with its own public "forbidden"; the service's setup and provider codes never reach the wire.
        if (code != "forbidden")
        {
            Assert.DoesNotContain(code, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Detail_and_list_answer_private_no_store_another_companys_invoice_is_404_and_the_list_query_is_strict()
    {
        var invoiceId = Guid.NewGuid();
        var service = new StubInvoiceService
        {
            Detail = id => id == invoiceId ? Task.FromResult(Detail(invoiceId, false)) : throw new SalesDraftServiceException(404, "not_found"),
            List = _ => Task.FromResult(new SalesInvoiceListResponse([], null)),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var found = await client.GetAsync($"{SalesEndpoints.InvoicesPath}/{invoiceId:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, found.StatusCode);
            Assert.True(found.Headers.CacheControl is { NoStore: true, Private: true });
        }

        using (var other = await client.GetAsync($"{SalesEndpoints.InvoicesPath}/{Guid.NewGuid():D}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        }

        using (var list = await client.GetAsync($"{SalesEndpoints.InvoicesPath}?pageSize=10"))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        }

        foreach (var invalid in new[] { "?pageSize=0", "?pageSize=101", "?unknown=1", "?cursor=" })
        {
            using var bad = await client.GetAsync(SalesEndpoints.InvoicesPath + invalid);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }
    }

    [Theory]
    [InlineData("submission", "submit")]
    [InlineData("approval", "approve")]
    [InlineData("rejection", "reject")]
    [InlineData("cancellation", "cancel")]
    [InlineData("finalization", "finalize")]
    [InlineData("finance-admission", "finance-resend")]
    public async Task Each_invoice_command_route_answers_200_and_a_replay_carries_the_replay_headers(string route, string command)
    {
        var invoiceId = Guid.NewGuid();
        var replay = false;
        var service = new StubInvoiceService
        {
            State = (_, id) => Task.FromResult(new SalesInvoiceCommandResult(Detail(id, replay), replay, "SENTETIK-corr-2")),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var response = await client.SendAsync(StatePost(invoiceId, route, "{\"expectedVersion\":\"1\"}", "SENTETIK-cmd")))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
            Assert.True(response.Headers.CacheControl is { NoStore: true, Private: true });
        }

        replay = true;
        using (var again = await client.SendAsync(StatePost(invoiceId, route, "{\"expectedVersion\":\"1\"}", "SENTETIK-cmd")))
        {
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            Assert.Equal("true", Assert.Single(again.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-corr-2", Assert.Single(again.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        Assert.Equal(2, service.StateBodies.Count);
        Assert.All(service.StateBodies, b => Assert.StartsWith($"{command}:1:", b, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("submission", "{\"expectedVersion\":\"1\",\"reason\":\"x\"}")]
    [InlineData("approval", "{\"expectedVersion\":\"1\",\"reason\":\"x\"}")]
    [InlineData("rejection", "{\"expectedVersion\":\"1\",\"extra\":true}")]
    [InlineData("cancellation", "{\"expectedVersion\":\"1\",\"extra\":true}")]
    [InlineData("cancellation", "{\"expectedVersion\":\"01\"}")]
    [InlineData("approval", "{}")]
    [InlineData("finalization", "{\"expectedVersion\":\"4\",\"reason\":\"x\"}")]
    [InlineData("finalization", "{}")]
    [InlineData("finance-admission", "{\"expectedVersion\":\"04\"}")]
    [InlineData("finance-admission", "{\"expectedVersion\":\"4\",\"extra\":true}")]
    public async Task An_invoice_command_body_that_is_not_strict_answers_400_before_the_service(string route, string json)
    {
        var service = new StubInvoiceService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(StatePost(Guid.NewGuid(), route, json, "SENTETIK-bad"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(service.Keys);
    }

    [Fact]
    public async Task An_invoice_command_without_an_idempotency_key_answers_400_and_a_self_approval_is_409_with_its_code()
    {
        var service = new StubInvoiceService
        {
            State = (_, _) => throw new SalesDraftServiceException(409, SalesInvoiceProblemCodesV1.SelfApproval),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var request = StatePost(Guid.NewGuid(), "approval", "{\"expectedVersion\":\"1\"}", null))
        using (var missing = await client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        }

        using var response = await client.SendAsync(StatePost(Guid.NewGuid(), "approval", "{\"expectedVersion\":\"1\"}", "SENTETIK-self"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("sales_invoice_self_approval", body.RootElement.GetProperty("code").GetString());
    }

    private const string MatchBody =
        "{\"expectedVersion\":\"3\",\"officialInvoiceNo\":\"GIB2026000000001\",\"ettn\":\"0f3b9a52-7c1e-4d8a-9b64-2a5c8e1d7f90\"}";

    [Fact]
    public async Task The_official_match_route_answers_200_passes_the_strict_body_and_a_replay_carries_the_replay_headers()
    {
        var replay = false;
        var service = new StubInvoiceService
        {
            State = (_, id) => Task.FromResult(new SalesInvoiceCommandResult(Detail(id, replay), replay, "SENTETIK-corr-2")),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var invoiceId = Guid.NewGuid();

        using (var response = await client.SendAsync(StatePost(invoiceId, "external-match", MatchBody, "SENTETIK-match")))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        }

        replay = true;
        using (var again = await client.SendAsync(StatePost(invoiceId, "external-match", MatchBody, "SENTETIK-match")))
        {
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            Assert.Equal("true", Assert.Single(again.Headers.GetValues("Idempotency-Replayed")));
        }

        Assert.All(service.StateBodies, b => Assert.StartsWith("match:3:GIB2026000000001|0f3b9a52-7c1e-4d8a-9b64-2a5c8e1d7f90|False|", b, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{\"expectedVersion\":\"3\",\"officialInvoiceNo\":\"A\"}")]
    [InlineData("{\"expectedVersion\":\"3\",\"officialInvoiceNo\":\"A\",\"ettn\":\"not-a-guid\"}")]
    [InlineData("{\"expectedVersion\":\"3\",\"officialInvoiceNo\":\" \",\"ettn\":\"0f3b9a52-7c1e-4d8a-9b64-2a5c8e1d7f90\"}")]
    [InlineData("{\"expectedVersion\":\"3\",\"officialInvoiceNo\":\"A\",\"ettn\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"expectedVersion\":\"3\",\"officialInvoiceNo\":\"A\",\"ettn\":\"0f3b9a52-7c1e-4d8a-9b64-2a5c8e1d7f90\",\"extra\":1}")]
    [InlineData("{\"expectedVersion\":\"03\",\"officialInvoiceNo\":\"A\",\"ettn\":\"0f3b9a52-7c1e-4d8a-9b64-2a5c8e1d7f90\"}")]
    public async Task An_official_match_body_that_is_not_strict_answers_400_before_the_service(string json)
    {
        var service = new StubInvoiceService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(StatePost(Guid.NewGuid(), "external-match", json, "SENTETIK-bad-match"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(service.Keys);
    }

    [Theory]
    [InlineData(409, "official_match_mismatch")]
    [InlineData(409, "difference_approval_required")]
    [InlineData(409, "official_document_already_matched")]
    [InlineData(422, "sales_invoice_match_difference_reason_required")]
    public async Task An_official_match_refusal_carries_its_stable_code(int status, string code)
    {
        var service = new StubInvoiceService { State = (_, _) => throw new SalesDraftServiceException(status, code) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(StatePost(Guid.NewGuid(), "external-match", MatchBody, "SENTETIK-refused-match"));

        Assert.Equal(status, (int)response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("finalization", 409, "sales_invoice_match_required")]
    [InlineData("finalization", 409, "version_conflict")]
    [InlineData("finalization", 422, "sales_invoice_instalment_functional_invalid")]
    [InlineData("finance-admission", 409, "state_conflict")]
    public async Task A_finalization_refusal_carries_its_stable_code(string route, int status, string code)
    {
        var service = new StubInvoiceService { State = (_, _) => throw new SalesDraftServiceException(status, code) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(StatePost(Guid.NewGuid(), route, "{\"expectedVersion\":\"4\"}", "SENTETIK-refused-final"));

        Assert.Equal(status, (int)response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_unavailable_official_invoice_provider_answers_the_platform_503_without_the_provider_code()
    {
        var service = new StubInvoiceService
        {
            State = (_, _) => throw new SalesDraftServiceException(503, SalesInvoiceProblemCodesV1.OfficialInvoiceProviderUnavailable),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(StatePost(Guid.NewGuid(), "external-match", MatchBody, "SENTETIK-503-match"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        // The service's own provider code never reaches the wire; the platform writer answers its generic code.
        Assert.Equal("service_unavailable", body.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("official_invoice_provider_unavailable", body.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_export_answers_one_private_xlsx_attachment_with_every_order_of_a_multi_order_invoice()
    {
        SalesInvoiceExportQuery? asked = null;
        var service = new StubInvoiceService
        {
            Export = query =>
            {
                asked = query;
                return Task.FromResult(ExportAnswer());
            },
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.GetAsync($"{SalesInvoiceApiV1.ExportPath}?format=xlsx&from=2026-11-01&to=2026-11-30");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new SalesInvoiceExportQuery("xlsx", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)), asked);
        Assert.Equal(SalesInvoiceApiV1.XlsxMediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("satis-faturalari-2026-11-01-2026-11-30.xlsx", response.Content.Headers.ContentDisposition?.FileName);
        Assert.True(response.Headers.CacheControl is { NoStore: true, Private: true });
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("default-src 'none'; sandbox", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));

        using var archive = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var sheet = await reader.ReadToEndAsync();
        Assert.Contains("Sipariş No(lar)", sheet, StringComparison.Ordinal);
        Assert.Contains("SS-2026-000001, SS-2026-000002", sheet, StringComparison.Ordinal);
        Assert.Contains("<row r=\"3\">", sheet, StringComparison.Ordinal);
        Assert.DoesNotContain("<row r=\"4\">", sheet, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("from=2026-11-01&to=2026-11-30")]
    [InlineData("format=xlsx&to=2026-11-30")]
    [InlineData("format=xlsx&from=2026-11-01")]
    [InlineData("format=xlsx&from=2026-11-01&to=2026-11-30&to=2026-12-01")]
    [InlineData("format=xlsx&from=2026-11-01&to=2026-11-30&state=Finalized")]
    [InlineData("format=xlsx&from=01.11.2026&to=2026-11-30")]
    [InlineData("format=xlsx&from=2026-11-01&to=2026-13-01")]
    public async Task An_export_query_that_is_not_strict_answers_400_before_the_service(string query)
    {
        var service = new StubInvoiceService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.GetAsync($"{SalesInvoiceApiV1.ExportPath}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_failed", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, service.ExportCalls);
    }

    [Theory]
    [InlineData(400, "validation_failed")]
    [InlineData(422, "sales_invoice_export_too_large")]
    [InlineData(403, "forbidden")]
    [InlineData(503, "service_unavailable")]
    public async Task An_export_refusal_carries_its_stable_code_and_403_and_503_stay_code_less(int status, string code)
    {
        var service = new StubInvoiceService
        {
            Export = _ => throw new SalesDraftServiceException(status, code, status is 400 or 422 ? [new("to", "range_too_large")] : null),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.GetAsync($"{SalesInvoiceApiV1.ExportPath}?format=xml&from=2026-11-01&to=2026-11-30");

        Assert.Equal(status, (int)response.StatusCode);
        Assert.NotEqual(SalesInvoiceApiV1.XlsxMediaType, response.Content.Headers.ContentType?.MediaType);
        if (status is 400 or 422)
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
            Assert.Contains("range_too_large", body.RootElement.GetProperty("errors").GetRawText(), StringComparison.Ordinal);
        }
    }

    private static SalesInvoiceExportV1 ExportAnswer()
    {
        var amounts = new SalesInvoiceAmountsView("10.00", "2.00", "0.00", "12.00", "12.00");
        var firstOrder = Guid.NewGuid();
        var secondOrder = Guid.NewGuid();
        SalesInvoiceExportLineV1 Line(int lineNo, Guid order, string orderNo) => new(lineNo, "IRS-1", lineNo, new DateOnly(2026, 11, 2),
            order, orderNo, Guid.NewGuid(), Guid.NewGuid(), "1.000", "10.00", null, null, null, null, "20", null, amounts, null);
        var invoice = new SalesInvoiceExportInvoiceV1(Guid.NewGuid(), "SF-2026-000001", null, null, new DateOnly(2026, 11, 3),
            new DateOnly(2026, 11, 3), Guid.NewGuid(), Account, Guid.NewGuid(), 2, null, amounts, null,
            [new SalesInvoiceFinalInstalmentView(1, new DateOnly(2026, 12, 3), "24.00", null, Guid.NewGuid(), Guid.NewGuid())],
            ["SS-2026-000001", "SS-2026-000002"], [Line(1, firstOrder, "SS-2026-000001"), Line(2, secondOrder, "SS-2026-000002")]);
        return new SalesInvoiceExportV1("xlsx", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), [invoice]);
    }

    private static HttpRequestMessage StatePost(Guid invoiceId, string route, string json, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{SalesEndpoints.InvoicesPath}/{invoiceId:D}/{route}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return request;
    }

    private WebApplicationFactory<Program> CreateHost(ISalesInvoiceService service) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesInvoiceService>();
            services.AddSingleton(service);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
        return client;
    }

    private static CreateSalesInvoiceRequest Body() => new(Account, [new SalesInvoiceShareRequest(Guid.Parse("66666666-6666-4666-8666-666666666666"))]);

    private static HttpRequestMessage Post(string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.InvoicesPath)
        {
            Content = new StringContent(JsonSerializer.Serialize(Body(), SalesDraftJson.Options), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static SalesInvoiceDetailView Detail(Guid id, bool replay)
    {
        var amounts = new SalesInvoiceAmountsView("10.00", "2.00", "0.00", "12.00", "12.00");
        return new SalesInvoiceDetailView(id, "SF-2026-000001", "Draft", "1", new DateOnly(2026, 11, 7), Guid.NewGuid(), Account,
            Guid.NewGuid(), 2, null, amounts, null, [], [], [], "SENTETIK-actor", DateTimeOffset.UnixEpoch, IsReplay: replay);
    }

    private sealed class StubInvoiceService : ISalesInvoiceService
    {
        public Func<CreateSalesInvoiceRequest, Task<SalesInvoiceCommandResult>> Create { get; set; } =
            _ => throw new InvalidOperationException("The service must not be called.");
        public Func<Guid, Task<SalesInvoiceDetailView>> Detail { get; set; } = _ => throw new InvalidOperationException("not called");
        public Func<SalesInvoiceListQuery, Task<SalesInvoiceListResponse>> List { get; set; } = _ => throw new InvalidOperationException("not called");
        public Func<string, Guid, Task<SalesInvoiceCommandResult>> State { get; set; } = (_, _) => throw new InvalidOperationException("not called");
        public Func<SalesInvoiceExportQuery, Task<SalesInvoiceExportV1>> Export { get; set; } = _ => throw new InvalidOperationException("not called");
        public int ExportCalls { get; private set; }
        public List<string> Keys { get; } = [];
        public List<string> StateBodies { get; } = [];

        public Task<SalesInvoiceCommandResult> SubmitInvoiceAsync(Guid invoiceId, SalesDraftStateCommandRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => Record("submit", invoiceId, request.ExpectedVersion, request.Reason, metadata);

        public Task<SalesInvoiceCommandResult> ApproveInvoiceAsync(Guid invoiceId, SalesDraftStateCommandRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => Record("approve", invoiceId, request.ExpectedVersion, request.Reason, metadata);

        public Task<SalesInvoiceCommandResult> RejectInvoiceAsync(Guid invoiceId, SalesDraftStateCommandRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => Record("reject", invoiceId, request.ExpectedVersion, request.Reason, metadata);

        public Task<SalesInvoiceCommandResult> CancelInvoiceAsync(Guid invoiceId, CancelSalesInvoiceRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            Record("cancel", invoiceId, request.ExpectedVersion, request.Reason + "|" + request.AcknowledgePaymentAllocationLift, metadata);

        public Task<SalesInvoiceCommandResult> MatchInvoiceAsync(Guid invoiceId, MatchSalesInvoiceRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            Record("match", invoiceId, request.ExpectedVersion,
                $"{request.OfficialInvoiceNo}|{request.Ettn}|{request.ApproveDifference}|{request.DifferenceReason}", metadata);

        public Task<SalesInvoiceCommandResult> FinalizeInvoiceAsync(Guid invoiceId, SalesDraftStateCommandRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            Record("finalize", invoiceId, request.ExpectedVersion, request.Reason, metadata);

        public Task<SalesInvoiceCommandResult> ResendFinanceAdmissionAsync(Guid invoiceId, SalesDraftStateCommandRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            Record("finance-resend", invoiceId, request.ExpectedVersion, request.Reason, metadata);

        private Task<SalesInvoiceCommandResult> Record(string command, Guid invoiceId, string version, string? reason, SalesCommandMetadata metadata)
        {
            Keys.Add(metadata.IdempotencyKey);
            StateBodies.Add($"{command}:{version}:{reason}");
            return State(command, invoiceId);
        }

        public Task<SalesInvoiceCommandResult> CreateInvoiceAsync(CreateSalesInvoiceRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Keys.Add(metadata.IdempotencyKey);
            return Create(request);
        }

        public Task<SalesInvoiceListResponse> GetInvoicesAsync(SalesInvoiceListQuery query, CancellationToken cancellationToken = default) =>
            List(query);

        public Task<SalesInvoiceDetailView> GetInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default) => Detail(invoiceId);

        public Task<SalesInvoiceExportV1> ExportInvoicesAsync(SalesInvoiceExportQuery query, CancellationToken cancellationToken = default)
        {
            ExportCalls++;
            return Export(query);
        }

        public Task<SalesDraftInvoicesResponse> GetDraftInvoicesAsync(Guid draftId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not called");
    }
}
