using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>The loading-instruction routes' wire contract over a stub service (the Sales HTTP acceptance pattern): metadata, statuses, headers, code boundary.</summary>
public sealed class LogisticsInstructionHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;

    [Theory]
    [InlineData("permission_unregistered")]
    [InlineData("counterparty_provider_unavailable")]
    [InlineData("sales_provider_unavailable")]
    [InlineData("reference_provider_invalid")]
    [InlineData("service_unavailable")]
    public async Task A_503_of_the_service_reaches_the_wire_only_as_the_platforms_public_code(string sourceCode)
    {
        var service = new StubService { Create = () => throw new LogisticsInstructionServiceException(503, sourceCode) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(string.Empty, "SENTETIK-503", CreateBody()));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("service_unavailable", JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
        if (sourceCode != "service_unavailable")
        {
            Assert.DoesNotContain(sourceCode, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_created_instruction_answers_201_with_its_location_and_a_replay_carries_the_first_attempts_headers()
    {
        var id = Guid.NewGuid();
        var service = new StubService { Create = () => Task.FromResult(new LoadingInstructionResponse(Detail(id), false, "SENTETIK-corr-1")) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var response = await client.SendAsync(Post(string.Empty, "SENTETIK-created", CreateBody())))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(new Uri(client.BaseAddress!, $"{LogisticsEndpoints.InstructionsPath.TrimStart('/')}/{id:D}"), response.Headers.Location);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
            Assert.Equal(id, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("instruction").GetProperty("id").GetGuid());
        }

        service.Create = () => Task.FromResult(new LoadingInstructionResponse(Detail(id), true, "SENTETIK-first"));
        using (var response = await client.SendAsync(Post(string.Empty, "SENTETIK-created", CreateBody())))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        service.Load = () => throw new LogisticsInstructionServiceException(409, LogisticsProblemCodesV1.LoadExceedsInstructed,
            [new("quantity", "logistics_load_exceeds_instructed")], isReplay: true, originalCorrelationId: "SENTETIK-first");
        using (var response = await client.SendAsync(Post($"{id:D}/lines/{Guid.NewGuid():D}/loads", "SENTETIK-load",
                   "{\"expectedVersion\":\"3\",\"loadId\":\"" + Guid.NewGuid().ToString("D") + "\",\"quantity\":\"1\",\"measurement\":\"Measured\"}")))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(LogisticsProblemCodesV1.LoadExceedsInstructed, problem.GetProperty("code").GetString());
            Assert.Equal("quantity", problem.GetProperty("errors")[0].GetProperty("field").GetString());
        }
    }

    [Fact]
    public async Task The_approved_order_lines_read_answers_the_price_free_page_and_refuses_a_loose_query_before_the_service()
    {
        // LOG-4b, Sinan §4 (#66 5738113623): the picker's read carries no price member on the wire — the JSON keys are pinned here.
        var service = new StubService
        {
            ApprovedLines = query => Task.FromResult(new LogisticsApprovedOrderLinePage(
                [new LogisticsApprovedOrderLineView(Guid.NewGuid(), "SENTETIK SO-0001", Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), "Sentetik Müşteri",
                    Guid.NewGuid(), "7", "GDC-001", "Sentetik rulo", Guid.NewGuid(), "kg", "12.500", new DateOnly(2026, 9, 25), 0)],
                query.Cursor is null ? "SENTETIK-next" : null)),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var response = await client.GetAsync($"{LogisticsEndpoints.InstructionsPath}/approved-order-lines?pageSize=10"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var item = Assert.Single(body.GetProperty("items").EnumerateArray());
            Assert.Equal(
                ["orderId", "documentNo", "lineId", "lineNo", "customerPartyId", "companyAccountId", "customerName", "productId", "productVersion", "productCode",
                    "productName", "unitId", "unitCode", "quantity", "requestedDeliveryDate", "revision"],
                item.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.Equal(("12.500", "SENTETIK-next"), (item.GetProperty("quantity").GetString(), body.GetProperty("nextCursor").GetString()));
        }

        using (var response = await client.GetAsync($"{LogisticsEndpoints.InstructionsPath}/approved-order-lines?pageSize=10&status=Draft"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task A_missing_key_a_wrong_media_type_and_a_loose_body_are_refused_before_the_service()
    {
        var service = new StubService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var none = new HttpRequestMessage(HttpMethod.Post, LogisticsEndpoints.InstructionsPath) { Content = new StringContent(CreateBody(), Encoding.UTF8, "application/json") })
        using (var response = await client.SendAsync(none))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(LogisticsProblemCodesV1.CommandMetadataInvalid, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        using (var text = Post(string.Empty, "SENTETIK-text", CreateBody(), "text/plain"))
        using (var response = await client.SendAsync(text))
        {
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }

        using (var loose = Post($"{Guid.NewGuid():D}/issue", "SENTETIK-loose", "{\"expectedVersion\":\"1\",\"reason\":\"x\"}"))
        using (var response = await client.SendAsync(loose))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_failed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task The_weighing_route_answers_the_instruction_with_its_record_replays_with_headers_and_refuses_a_loose_body_before_the_service()
    {
        // AT-053 wire: 200 with the instruction after the record (the weighing member always present), the replay headers on the
        // same key, the recorded terminal 409 with its code, and a body carrying a calculated member refused before the service.
        var id = Guid.NewGuid();
        var weighing = new LoadingInstructionWeighingView(Guid.NewGuid(), "TareFirst", "15000", "39120", "15000", "39120", "24120", "24000", "120",
            "SENTETIK-kantar", DateTimeOffset.UtcNow);
        var service = new StubService { Weigh = () => Task.FromResult(new LoadingInstructionResponse(Detail(id) with { Weighing = weighing }, false, "SENTETIK-corr")) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var body = $$"""{"expectedVersion":"4","weighingId":"{{weighing.WeighingId:D}}","direction":"TareFirst","firstWeighingKg":"15000","secondWeighingKg":"39120"}""";

        using (var response = await client.SendAsync(Post($"{id:D}/weighings", "SENTETIK-weigh", body)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var answered = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("weighing");
            Assert.Equal(("24120", "120", "TareFirst"), (answered.GetProperty("netKg").GetString(), answered.GetProperty("differenceKg").GetString(),
                answered.GetProperty("direction").GetString()));
        }

        service.Weigh = () => Task.FromResult(new LoadingInstructionResponse(Detail(id) with { Weighing = weighing }, true, "SENTETIK-first"));
        using (var response = await client.SendAsync(Post($"{id:D}/weighings", "SENTETIK-weigh", body)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        service.Weigh = () => throw new LogisticsInstructionServiceException(409, LogisticsProblemCodesV1.WeighingAlreadyRecorded,
            [new("weighingId", "logistics_weighing_already_recorded")]);
        using (var response = await client.SendAsync(Post($"{id:D}/weighings", "SENTETIK-weigh-2", body)))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(LogisticsProblemCodesV1.WeighingAlreadyRecorded, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        var calls = service.Calls;
        var loose = body.Replace("}", ",\"netKg\":\"24120\"}", StringComparison.Ordinal);
        using (var response = await client.SendAsync(Post($"{id:D}/weighings", "SENTETIK-weigh-loose", loose)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal(calls, service.Calls);

        // Without a weighing, the detail's member is present and null (the frontend decoder requires every member).
        service.Create = () => Task.FromResult(new LoadingInstructionResponse(Detail(id), false, "SENTETIK-corr"));
        using (var response = await client.SendAsync(Post(string.Empty, "SENTETIK-created", CreateBody())))
        {
            var detail = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail");
            Assert.Equal(JsonValueKind.Null, detail.GetProperty("weighing").ValueKind);
        }
    }

    [Fact]
    public async Task The_dispatch_note_route_answers_the_instruction_with_its_draft_replays_with_headers_and_refuses_a_loose_body_before_the_service()
    {
        // AT-055 wire: 200 with the instruction after the draft (the dispatchNotes member always present), the replay headers on the
        // same key, the recorded terminal 409 for a shipment already on a draft, and a body carrying a quantity refused before the service.
        var id = Guid.NewGuid();
        var shipment = Guid.NewGuid();
        var note = new DispatchNoteView(Guid.NewGuid(), "Draft", null, "SENTETIK-sevkiyat", DateTimeOffset.UtcNow, null, null, null, null, null, null,
            [new DispatchNoteLineView(1, shipment, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "3")]);
        var service = new StubService { Draft = () => Task.FromResult(new LoadingInstructionResponse(Detail(id) with { DispatchNotes = [note] }, false, "SENTETIK-corr")) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var body = $$"""{"expectedVersion":"6","dispatchNoteId":"{{note.DispatchNoteId:D}}","shipmentIds":["{{shipment:D}}"]}""";

        using (var response = await client.SendAsync(Post($"{id:D}/dispatch-notes", "SENTETIK-note", body)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var answered = Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("dispatchNotes").EnumerateArray());
            // Every member is on the wire, the absent ones as null: the system number and the e-İrsaliye number are two members.
            Assert.Equal(["dispatchNoteId", "status", "number", "createdBy", "createdAtUtc", "approvedBy", "approvedAtUtc", "cancelledBy", "cancelledAtUtc",
                    "cancelReason", "eDispatchNumber", "lines"],
                answered.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.Equal(JsonValueKind.Null, answered.GetProperty("number").ValueKind);
            var line = Assert.Single(answered.GetProperty("lines").EnumerateArray());
            Assert.Equal(["lineNo", "shipmentId", "instructionLineId", "sourceOrderId", "sourceOrderLineId", "productId", "unitId", "quantity"],
                line.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.Equal(("Draft", "3"), (answered.GetProperty("status").GetString(), line.GetProperty("quantity").GetString()));
        }

        service.Draft = () => Task.FromResult(new LoadingInstructionResponse(Detail(id) with { DispatchNotes = [note] }, true, "SENTETIK-first"));
        using (var response = await client.SendAsync(Post($"{id:D}/dispatch-notes", "SENTETIK-note", body)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        service.Draft = () => throw new LogisticsInstructionServiceException(409, LogisticsProblemCodesV1.ShipmentAlreadyDispatched,
            [new("shipmentIds", "logistics_shipment_already_dispatched")]);
        using (var response = await client.SendAsync(Post($"{id:D}/dispatch-notes", "SENTETIK-note-2", body)))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(LogisticsProblemCodesV1.ShipmentAlreadyDispatched, problem.GetProperty("code").GetString());
            Assert.Equal("shipmentIds", problem.GetProperty("errors")[0].GetProperty("field").GetString());
        }

        var calls = service.Calls;
        foreach (var loose in new[]
                 {
                     body.Replace("}", ",\"quantity\":\"3\"}", StringComparison.Ordinal),
                     $$"""{"expectedVersion":"6","dispatchNoteId":"{{note.DispatchNoteId:D}}","shipmentIds":[]}""",
                     $$"""{"expectedVersion":"6","dispatchNoteId":"{{note.DispatchNoteId:D}}","shipmentIds":["{{shipment:D}}","{{shipment:D}}"]}""",
                 })
        {
            using var response = await client.SendAsync(Post($"{id:D}/dispatch-notes", "SENTETIK-note-loose", loose));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal(calls, service.Calls);
    }

    [Fact]
    public async Task The_sales_order_dispatch_notes_read_answers_the_drafts_of_one_order_and_refuses_a_loose_query_before_the_service()
    {
        var order = Guid.NewGuid();
        var service = new StubService
        {
            OrderNotes = salesOrderId => Task.FromResult(new SalesOrderDispatchNotesView(salesOrderId,
                [new SalesOrderDispatchNoteView(Guid.NewGuid(), Guid.NewGuid(), "Approved", "IRS-000001", null, DateTimeOffset.UtcNow,
                    [new SalesOrderDispatchNoteLineView(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "2.5")])], false)),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var response = await client.GetAsync($"{LogisticsApiV1.SalesOrderDispatchNotesPath}?salesOrderId={order:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(["salesOrderId", "items", "hasMore"], body.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.Equal(order, body.GetProperty("salesOrderId").GetGuid());
            var item = Assert.Single(body.GetProperty("items").EnumerateArray());
            Assert.Equal(["dispatchNoteId", "instructionId", "status", "number", "eDispatchNumber", "createdAtUtc", "lines"],
                item.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.Equal(("Approved", "IRS-000001"), (item.GetProperty("status").GetString(), item.GetProperty("number").GetString()));
        }

        foreach (var query in new[] { "", "?salesOrderId=nope", $"?salesOrderId={order:D}&pageSize=1", $"?salesOrderId={order:D}&salesOrderId={order:D}",
                     "?salesOrderId=00000000-0000-0000-0000-000000000000" })
        {
            using var response = await client.GetAsync($"{LogisticsApiV1.SalesOrderDispatchNotesPath}{query}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task The_lifecycle_routes_bind_the_instruction_and_the_note_and_refuse_a_loose_body_before_the_service()
    {
        // AT-055 lifecycle wire (user decision 2026-09-30): approve / cancel / e-İrsaliye number answer 200 with the instruction; the
        // recorded terminal 409s carry their codes; a body with another member, a padded or over-long reason, or an e-İrsaliye
        // number out of the GİB shape is refused before the service.
        var id = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var approved = new DispatchNoteView(noteId, "Approved", "IRS-000001", "SENTETIK-sevkiyat", DateTimeOffset.UtcNow, "SENTETIK-onay", DateTimeOffset.UtcNow,
            null, null, null, null, [new DispatchNoteLineView(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "3")]);
        var service = new StubService { Lifecycle = _ => Task.FromResult(new LoadingInstructionResponse(Detail(id) with { DispatchNotes = [approved] }, false, "SENTETIK-corr")) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        foreach (var (segment, body) in new[]
                 {
                     ("approve", """{"expectedVersion":"7"}"""),
                     ("cancel", """{"expectedVersion":"7","reason":"Yanlış araç"}"""),
                     ("e-dispatch-number", """{"expectedVersion":"7","eDispatchNumber":"GYM2026000000001"}"""),
                 })
        {
            using var response = await client.SendAsync(Post($"{id:D}/dispatch-notes/{noteId:D}/{segment}", $"SENTETIK-{segment}", body));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var note = Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("dispatchNotes").EnumerateArray());
            Assert.Equal("IRS-000001", note.GetProperty("number").GetString());
        }

        Assert.Equal(["approve", "cancel", "e-dispatch-number"], service.LifecycleCalls.Select(call => call.Command).ToArray());
        Assert.All(service.LifecycleCalls, call => Assert.Equal((id, noteId), (call.InstructionId, call.DispatchNoteId)));
        Assert.Equal("Yanlış araç", Assert.IsType<CancelDispatchNoteRequest>(service.LifecycleCalls[1].Request).Reason);

        foreach (var code in new[] { LogisticsProblemCodesV1.DispatchNoteState, LogisticsProblemCodesV1.EDispatchNumberInUse })
        {
            service.Lifecycle = _ => throw new LogisticsInstructionServiceException(409, code, [new("status", code)]);
            using var response = await client.SendAsync(Post($"{id:D}/dispatch-notes/{noteId:D}/e-dispatch-number", $"SENTETIK-{code}",
                """{"expectedVersion":"8","eDispatchNumber":"GYM2026000000002"}"""));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        var calls = service.Calls;
        var longReason = new string('x', 501);
        foreach (var (segment, loose) in new[]
                 {
                     ("approve", """{"expectedVersion":"7","number":"IRS-000009"}"""),
                     ("approve", """{}"""),
                     ("cancel", """{"expectedVersion":"7"}"""),
                     ("cancel", """{"expectedVersion":"7","reason":"  "}"""),
                     ("cancel", """{"expectedVersion":"7","reason":" padded"}"""),
                     ("cancel", "{\"expectedVersion\":\"7\",\"reason\":\"" + longReason + "\"}"),
                     ("e-dispatch-number", """{"expectedVersion":"7","eDispatchNumber":"gym2026000000001"}"""),
                     ("e-dispatch-number", """{"expectedVersion":"7","eDispatchNumber":"GYM202600000001"}"""),
                     ("e-dispatch-number", """{"expectedVersion":"7","eDispatchNumber":"GYM2026000000001","number":"IRS-000001"}"""),
                 })
        {
            using var response = await client.SendAsync(Post($"{id:D}/dispatch-notes/{noteId:D}/{segment}", "SENTETIK-loose", loose));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal(calls, service.Calls);
        // A note id that is not a uuid never reaches a route.
        using var notGuid = await client.SendAsync(Post($"{id:D}/dispatch-notes/not-a-note/approve", "SENTETIK-not-guid", """{"expectedVersion":"7"}"""));
        Assert.Equal(HttpStatusCode.NotFound, notGuid.StatusCode);
        Assert.Equal(calls, service.Calls);
    }

    private static string CreateBody() =>
        $$"""{"customerPartyId":"{{Guid.NewGuid():D}}","companyAccountId":"{{Guid.NewGuid():D}}","effectiveOn":"2026-09-19","lines":[{"lineId":"{{Guid.NewGuid():D}}","sourceOrderId":"{{Guid.NewGuid():D}}","sourceOrderLineId":"{{Guid.NewGuid():D}}","unitId":"{{Guid.NewGuid():D}}","quantity":"6"}]}""";

    private static HttpRequestMessage Post(string segment, string key, string json, string mediaType = "application/json")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, segment.Length == 0 ? LogisticsEndpoints.InstructionsPath : $"{LogisticsEndpoints.InstructionsPath}/{segment}")
        {
            Content = new StringContent(json, Encoding.UTF8, mediaType),
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static LoadingInstructionDetailView Detail(Guid id) => new(
        new LoadingInstructionSummaryView(id, CompanyA, "Draft", "2", Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 19), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1),
        [new LoadingInstructionLineView(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid(), Guid.NewGuid(), "6", "0", "0", "0", "6", "0", "0", false)],
        [], null, []);

    private WebApplicationFactory<Program> CreateHost(StubService service) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Scoped<ILogisticsInstructionService>(_ => service))));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.CompanyA);
        client.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        return client;
    }

    private sealed class StubService : ILogisticsInstructionService
    {
        public int Calls { get; private set; }
        public Func<Task<LoadingInstructionResponse>> Create { get; set; } = () => throw new InvalidOperationException("not expected");
        public Func<Task<LoadingInstructionResponse>> Load { get; set; } = () => throw new InvalidOperationException("not expected");

        public Task<LoadingInstructionResponse> CreateInstructionAsync(CreateLoadingInstructionRequest request, LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Create();
        }

        public Task<LoadingInstructionResponse> IssueInstructionAsync(Guid instructionId, IssueLoadingInstructionRequest request, LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new LogisticsInstructionServiceException(404, "not_found");
        }

        public Task<LoadingInstructionResponse> RecordLoadAsync(Guid instructionId, Guid lineId, RecordLoadRequest request, LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Load();
        }

        public Func<Task<LoadingInstructionResponse>> Weigh { get; set; } = () => throw new InvalidOperationException("not expected");

        public Task<LoadingInstructionResponse> RecordWeighingAsync(Guid instructionId, RecordWeighingRequest request, LogisticsCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Weigh();
        }

        public Func<Task<LoadingInstructionResponse>> Draft { get; set; } = () => throw new InvalidOperationException("not expected");

        public Task<LoadingInstructionResponse> CreateDispatchNoteAsync(Guid instructionId, CreateDispatchNoteRequest request, LogisticsCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Draft();
        }

        public Func<string, Task<LoadingInstructionResponse>> Lifecycle { get; set; } = _ => throw new InvalidOperationException("not expected");

        public List<(string Command, Guid InstructionId, Guid DispatchNoteId, object Request)> LifecycleCalls { get; } = [];

        public Task<LoadingInstructionResponse> ApproveDispatchNoteAsync(Guid instructionId, Guid dispatchNoteId, ApproveDispatchNoteRequest request,
            LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Calls++;
            LifecycleCalls.Add(("approve", instructionId, dispatchNoteId, request));
            return Lifecycle("approve");
        }

        public Task<LoadingInstructionResponse> CancelDispatchNoteAsync(Guid instructionId, Guid dispatchNoteId, CancelDispatchNoteRequest request,
            LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Calls++;
            LifecycleCalls.Add(("cancel", instructionId, dispatchNoteId, request));
            return Lifecycle("cancel");
        }

        public Task<LoadingInstructionResponse> SetEDispatchNumberAsync(Guid instructionId, Guid dispatchNoteId, SetEDispatchNumberRequest request,
            LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Calls++;
            LifecycleCalls.Add(("e-dispatch-number", instructionId, dispatchNoteId, request));
            return Lifecycle("e-dispatch-number");
        }

        public Func<Guid, Task<SalesOrderDispatchNotesView>> OrderNotes { get; set; } = _ => throw new InvalidOperationException("not expected");

        public Task<SalesOrderDispatchNotesView> GetSalesOrderDispatchNotesAsync(Guid salesOrderId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return OrderNotes(salesOrderId);
        }

        public Task<LoadingInstructionDetailView> GetInstructionDetailAsync(Guid instructionId, CancellationToken cancellationToken = default) =>
            throw new LogisticsInstructionServiceException(404, "not_found");

        public Task<LoadingInstructionListResponse> GetInstructionListAsync(LoadingInstructionListQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LoadingInstructionListResponse([], null));

        public Func<LogisticsApprovedOrderLinesQuery, Task<LogisticsApprovedOrderLinePage>> ApprovedLines { get; set; } =
            _ => Task.FromResult(new LogisticsApprovedOrderLinePage([], null));

        public Task<LogisticsApprovedOrderLinePage> GetApprovedOrderLinesAsync(LogisticsApprovedOrderLinesQuery query, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ApprovedLines(query);
        }
    }
}
