using System.Net;
using System.Net.Http.Json;
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
/// The draft routes' HTTP boundary over a SENTETIK <see cref="ISalesDraftService"/> (no database,
/// no owner): what the wire carries for each service answer. The service's own rules are unit-tested
/// (<c>SalesDraftServiceTests</c>); the real host with the real store is the SQL Server class.
/// </summary>
public sealed class SalesDraftHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;

    [Theory]
    [InlineData("permission_unregistered")]
    [InlineData("counterparty_provider_unavailable")]
    [InlineData("settings_provider_unavailable")]
    [InlineData("reference_provider_invalid")]
    [InlineData("product_eligibility_unavailable")]
    [InlineData("service_unavailable")]
    public async Task A_503_of_the_draft_service_reaches_the_wire_only_as_the_platforms_public_code(string sourceCode)
    {
        var service = new StubDraftService { Create = _ => throw new SalesDraftServiceException(503, sourceCode) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var request = Post("SENTETIK-503");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal("service_unavailable", document.RootElement.GetProperty("code").GetString());
        if (!string.Equals(sourceCode, "service_unavailable", StringComparison.Ordinal))
        {
            Assert.DoesNotContain(sourceCode, body, StringComparison.Ordinal);
        }

        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
    }

    [Fact]
    public async Task A_created_draft_answers_201_with_its_absolute_detail_location_and_no_replay_header()
    {
        var draftId = Guid.NewGuid();
        var service = new StubDraftService { Create = _ => Task.FromResult(new SalesOrderDraftResponse(Detail(draftId), false, "SENTETIK-corr-1")) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var request = Post("SENTETIK-created");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(new Uri(client.BaseAddress!, $"{SalesEndpoints.DraftsPath.TrimStart('/')}/{draftId:D}"), response.Headers.Location);
        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal(draftId, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("draft").GetProperty("id").GetGuid());
        Assert.Equal("SENTETIK-created", Assert.Single(service.Keys));
    }

    [Fact]
    public async Task A_replayed_draft_and_a_replayed_refusal_carry_the_replay_headers_of_the_first_attempt()
    {
        var draftId = Guid.NewGuid();
        var service = new StubDraftService { Create = _ => Task.FromResult(new SalesOrderDraftResponse(Detail(draftId), true, "SENTETIK-first")) };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var request = Post("SENTETIK-replay"))
        using (var response = await client.SendAsync(request))
        {
            // ADR-011 §4: the persisted 201 is replayed, told apart by the headers only.
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
            Assert.Equal(new Uri(client.BaseAddress!, $"{SalesEndpoints.DraftsPath.TrimStart('/')}/{draftId:D}"), response.Headers.Location);
        }

        service.Create = _ => throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.VersionConflict,
            [new("lines[0].productId", "product_version_conflict")], isReplay: true, originalCorrelationId: "SENTETIK-first");
        using (var request = Post("SENTETIK-replay"))
        using (var response = await client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(SalesDraftProblemCodesV1.VersionConflict, problem.GetProperty("code").GetString());
            Assert.Equal("lines[0].productId", problem.GetProperty("errors")[0].GetProperty("field").GetString());
        }
    }

    [Fact]
    public async Task A_missing_repeated_or_over_long_key_is_refused_before_the_service()
    {
        var service = new StubDraftService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var none = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.DraftsPath) { Content = Body() })
        using (var response = await client.SendAsync(none))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.CommandMetadataInvalid, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        using (var twice = Post("SENTETIK-a"))
        {
            twice.Headers.Add("Idempotency-Key", "SENTETIK-b");
            using var response = await client.SendAsync(twice);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var over = Post(new string('k', 129)))
        using (var response = await client.SendAsync(over))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var exact = Post(new string('k', 128)))
        using (var response = await client.SendAsync(exact))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        Assert.Equal(new[] { new string('k', 128) }, service.Keys);
    }

    private static HttpRequestMessage Post(string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.DraftsPath) { Content = Body() };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static StringContent Body()
    {
        var line = new SalesOrderDraftLineRequest(Guid.NewGuid(), Guid.NewGuid(), "1", Guid.NewGuid(), "1.25", null, null);
        var request = new CreateSalesOrderDraftRequest("Order", Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 18), Guid.NewGuid(), [line]);
        return new StringContent(JsonSerializer.Serialize(request, SalesDraftJson.Options), Encoding.UTF8, "application/json");
    }

    private static SalesOrderDraftDetailView Detail(Guid draftId, string approvalState = "Preparing") => new(
        new SalesOrderDraftSummaryView(draftId, CompanyA, "SO-2026-000001", "Order", "Draft", "1", "Rev.0", Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), new DateOnly(2026, 9, 18), new DateTimeOffset(2026, 9, 18, 14, 0, 0, TimeSpan.Zero), 0, approvalState),
        null, null, [], null, null, null, null, null, null, new SalesDraftApprovalV1(null, null, null, null, null), false, false);

    private WebApplicationFactory<Program> CreateHost(ISalesDraftService service) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesDraftService>();
            services.AddSingleton(service);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString("D"));
        return client;
    }

    private sealed class StubDraftService : ISalesDraftService
    {
        public List<string> Keys { get; } = [];

        public Func<CreateSalesOrderDraftRequest, Task<SalesOrderDraftResponse>> Create { get; set; } =
            _ => Task.FromResult(new SalesOrderDraftResponse(Detail(Guid.NewGuid()), false, "SENTETIK-corr"));

        public Task<SalesOrderDraftResponse> CreateDraftAsync(CreateSalesOrderDraftRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Keys.Add(metadata.IdempotencyKey);
            return Create(request);
        }

        public Task<SalesOrderDraftDetailView> GetDraftDetailAsync(Guid draftId, CancellationToken cancellationToken = default) =>
            throw new SalesDraftServiceException(404, "not_found");

        public Task<SalesOrderDraftListResponse> GetDraftListAsync(SalesOrderDraftListQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesOrderDraftListResponse([], null));

        /// <summary>SALES-A1: what the stub answers a state command with; the command name and body reach <see cref="StateCalls"/>.</summary>
        public Func<string, Guid, SalesDraftStateCommandRequest, Task<SalesOrderDraftResponse>> State { get; set; } =
            (_, id, _) => Task.FromResult(new SalesOrderDraftResponse(Detail(id, "PendingApproval"), false, "SENTETIK-corr"));

        public List<(string Command, Guid DraftId, string ExpectedVersion, string? Reason, string Key)> StateCalls { get; } = [];

        public Func<Guid, SalesDraftCommercialRevisionRequest, Task<SalesOrderDraftResponse>> Commercial { get; set; } =
            (id, _) => Task.FromResult(new SalesOrderDraftResponse(Detail(id), false, "SENTETIK-corr"));

        public List<(Guid DraftId, SalesDraftCommercialRevisionRequest Request, string Key)> CommercialCalls { get; } = [];

        public Task<SalesOrderDraftResponse> ReviseCommercialAsync(Guid draftId, SalesDraftCommercialRevisionRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            CommercialCalls.Add((draftId, request, metadata.IdempotencyKey));
            return Commercial(draftId, request);
        }

        public Func<Guid, Task<SalesOrderDraftRevisionsResponse>> Revisions { get; set; } =
            _ => Task.FromResult(new SalesOrderDraftRevisionsResponse([]));

        public List<Guid> RevisionReads { get; } = [];

        public Task<SalesOrderDraftRevisionsResponse> GetDraftRevisionsAsync(Guid draftId, CancellationToken cancellationToken = default)
        {
            RevisionReads.Add(draftId);
            return Revisions(draftId);
        }

        /// <summary>DEC-SAL-003: the delivery-schedule update and its history; this stub answers nothing it was not given.</summary>
        public Func<Guid, SalesDraftDeliveryScheduleRequest, Task<SalesOrderDraftResponse>> Schedule { get; set; } =
            (_, _) => throw new SalesDraftServiceException(404, "not_found");

        public List<(Guid DraftId, SalesDraftDeliveryScheduleRequest Request, string Key)> ScheduleCalls { get; } = [];

        public Task<SalesOrderDraftResponse> RescheduleDeliveryAsync(Guid draftId, SalesDraftDeliveryScheduleRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            ScheduleCalls.Add((draftId, request, metadata.IdempotencyKey));
            return Schedule(draftId, request);
        }

        public Task<SalesOrderDraftDeliverySchedulesResponse> GetDraftDeliverySchedulesAsync(Guid draftId, CancellationToken cancellationToken = default) =>
            throw new SalesDraftServiceException(404, "not_found");

        /// <summary>AT-024: what the stub answers the delivery-progress read with; the draft ids asked reach <see cref="ProgressReads"/>.</summary>
        public Func<Guid, Task<SalesOrderDraftDeliveryProgressResponse>> Progress { get; set; } =
            _ => throw new SalesDraftServiceException(404, "not_found");

        public List<Guid> ProgressReads { get; } = [];

        public Task<SalesOrderDraftDeliveryProgressResponse> GetDraftDeliveryProgressAsync(Guid draftId, CancellationToken cancellationToken = default)
        {
            ProgressReads.Add(draftId);
            return Progress(draftId);
        }

        public Task<SalesOrderDraftResponse> SubmitDraftAsync(Guid draftId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default) => StateAsync("submit", draftId, request, metadata);

        public Task<SalesOrderDraftResponse> ApproveDraftAsync(Guid draftId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default) => StateAsync("approve", draftId, request, metadata);

        public Task<SalesOrderDraftResponse> WithdrawDraftAsync(Guid draftId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default) => StateAsync("withdraw", draftId, request, metadata);

        public Task<SalesOrderDraftResponse> RejectDraftAsync(Guid draftId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default) => StateAsync("reject", draftId, request, metadata);

        private Task<SalesOrderDraftResponse> StateAsync(string command, Guid draftId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata)
        {
            StateCalls.Add((command, draftId, request.ExpectedVersion, request.Reason, metadata.IdempotencyKey));
            return State(command, draftId, request);
        }
    }

    // ---- SALES-A1: the state command routes' wire ------------------------------------------------

    [Theory]
    [InlineData("submission", "submit")]
    [InlineData("approval", "approve")]
    [InlineData("rejection", "reject")]
    [InlineData("withdrawal", "withdraw")]
    public async Task A_state_command_answers_200_with_the_draft_and_the_replay_headers_only_on_a_replay(string segment, string command)
    {
        var draftId = Guid.NewGuid();
        var service = new StubDraftService
        {
            State = (_, id, request) => Task.FromResult(new SalesOrderDraftResponse(Detail(id, "PendingApproval"),
                request.ExpectedVersion == "2", "SENTETIK-first-corr")),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using (var first = await client.SendAsync(StateCommand(draftId, segment, "SENTETIK-s-1", "{\"expectedVersion\":\"1\"}")))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.True(first.Headers.CacheControl is { Private: true, NoStore: true });
            Assert.False(first.Headers.Contains("Idempotency-Replayed"));
            var body = await first.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(draftId, body.GetProperty("detail").GetProperty("draft").GetProperty("id").GetGuid());
            Assert.Equal("PendingApproval", body.GetProperty("detail").GetProperty("draft").GetProperty("approvalState").GetString());
            Assert.False(body.GetProperty("isReplay").GetBoolean());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("detail").GetProperty("approval").GetProperty("submittedBy").ValueKind);
        }

        using (var replay = await client.SendAsync(StateCommand(draftId, segment, "SENTETIK-s-2", "{\"expectedVersion\":\"2\"}")))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-first-corr", Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        Assert.Equal([(command, draftId, "1", null, "SENTETIK-s-1"), (command, draftId, "2", null, "SENTETIK-s-2")], service.StateCalls);
    }

    [Fact]
    public async Task A_state_command_without_a_canonical_key_a_json_body_or_the_strict_shape_never_reaches_the_service()
    {
        var service = new StubDraftService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var draftId = Guid.NewGuid();

        using (var noKey = new HttpRequestMessage(HttpMethod.Post, $"{SalesEndpoints.DraftsPath}/{draftId:D}/submission")
        {
            Content = new StringContent("{\"expectedVersion\":\"1\"}", Encoding.UTF8, "application/json"),
        })
        {
            using var response = await client.SendAsync(noKey);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.CommandMetadataInvalid,
                (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        using (var text = new HttpRequestMessage(HttpMethod.Post, $"{SalesEndpoints.DraftsPath}/{draftId:D}/approval")
        {
            Content = new StringContent("expectedVersion=1", Encoding.UTF8, "text/plain"),
        })
        {
            text.Headers.Add("Idempotency-Key", "SENTETIK-text");
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.SendAsync(text)).StatusCode);
        }

        // A missing version, a non-positive or non-integer one, an unknown member, and a reason outside rejection/withdrawal: 400.
        foreach (var (segment, json) in new[]
                 {
                     ("submission", "{}"), ("submission", "{\"expectedVersion\":\"0\"}"), ("approval", "{\"expectedVersion\":1}"),
                     ("approval", "{\"expectedVersion\":\"1\",\"lifecycle\":\"Active\"}"), ("submission", "{\"expectedVersion\":\"1\",\"reason\":\"x\"}"),
                     ("rejection", "{\"expectedVersion\":\"01\"}"),
                 })
        {
            using var response = await client.SendAsync(StateCommand(draftId, segment, "SENTETIK-shape", json));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_failed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        // The rejection carries its reason; null is "no reason", never a default text.
        using (var rejected = await client.SendAsync(StateCommand(draftId, "rejection", "SENTETIK-r-1", "{\"expectedVersion\":\"3\",\"reason\":\"SENTETIK neden\"}")))
        {
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        }

        Assert.Equal([("reject", draftId, "3", "SENTETIK neden", "SENTETIK-r-1")], service.StateCalls);
    }

    [Theory]
    [InlineData(409, SalesDraftProblemCodesV1.StateConflict, true)]
    [InlineData(409, SalesDraftProblemCodesV1.VersionConflict, true)]
    [InlineData(422, SalesDraftProblemCodesV1.ApprovalReasonTooLong, false)]
    [InlineData(404, "not_found", false)]
    public async Task A_state_commands_refusal_carries_its_code_and_a_replayed_terminal_refusal_its_headers(int status, string code, bool replayed)
    {
        var service = new StubDraftService
        {
            State = (_, _, _) => throw new SalesDraftServiceException(status, code, [new("approvalState", code)], isReplay: replayed,
                originalCorrelationId: replayed ? "SENTETIK-first-corr" : null),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(StateCommand(Guid.NewGuid(), "rejection", "SENTETIK-refused", "{\"expectedVersion\":\"1\"}"));

        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (status == 404)
        {
            // Platform 404 stays code-less on this wire: the service's code never reaches it.
            Assert.False(body.TryGetProperty("code", out var codeless) && codeless.GetString() == code);
        }
        else
        {
            Assert.Equal(code, body.GetProperty("code").GetString());
            Assert.Equal("approvalState", body.GetProperty("errors")[0].GetProperty("field").GetString());
        }

        Assert.Equal(replayed, response.Headers.Contains("Idempotency-Replayed"));
        if (replayed)
        {
            Assert.Equal("SENTETIK-first-corr", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }
    }

    [Fact]
    public async Task Commercial_revision_and_history_use_the_strict_owner_contract_and_replay_headers()
    {
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var priceUnitId = Guid.NewGuid();
        var moneyProfileId = Guid.NewGuid();
        var archivedAt = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);
        var service = new StubDraftService
        {
            Commercial = (_, _) => Task.FromResult(new SalesOrderDraftResponse(Detail(draftId), true, "SENTETIK-commercial-first")),
            Revisions = _ => Task.FromResult(new SalesOrderDraftRevisionsResponse([
                new SalesDraftRevisionV1("Rev.0", "SENTETIK-submitter", archivedAt.AddMinutes(-2), "SENTETIK-approver",
                    archivedAt.AddMinutes(-1), archivedAt, "SENTETIK-pricer", moneyProfileId, "7", 2,
                    [new SalesDraftRevisionLineV1(lineId, 1, Guid.NewGuid(), "3", Guid.NewGuid(), "2.5", "13.00", priceUnitId, "5", SalesPriceDisclosure.Visible)])
            ])),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);

        var command = new SalesDraftCommercialRevisionRequest("4", [new SalesDraftLineCommercialV1(lineId, "13.00", priceUnitId, "5")]);
        using (var request = new HttpRequestMessage(HttpMethod.Post, $"{SalesEndpoints.DraftsPath}/{draftId:D}/commercial-revision")
               {
                   Content = new StringContent(JsonSerializer.Serialize(command, SalesDraftJson.Options), Encoding.UTF8, "application/json"),
               })
        {
            request.Headers.Add("Idempotency-Key", "SENTETIK-commercial-1");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-commercial-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        var call = Assert.Single(service.CommercialCalls);
        Assert.Equal((draftId, "4", "SENTETIK-commercial-1"), (call.DraftId, call.Request.ExpectedVersion, call.Key));
        var sentLine = Assert.Single(call.Request.Lines);
        Assert.Equal((lineId, "13.00", priceUnitId, "5"),
            (sentLine.LineId, sentLine.UnitPrice, sentLine.PriceUnitId, sentLine.DiscountPercent));

        using var history = await client.GetAsync($"{SalesEndpoints.DraftsPath}/{draftId:D}/revisions");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var body = await history.Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal("Rev.0", item.GetProperty("revision").GetString());
        Assert.Equal("Visible", item.GetProperty("lines")[0].GetProperty("priceDisclosure").GetString());
        Assert.Equal("13.00", item.GetProperty("lines")[0].GetProperty("unitPrice").GetString());
        Assert.Equal([draftId], service.RevisionReads);
    }

    [Fact]
    public async Task Delivery_progress_is_a_private_read_of_the_service_answer_and_its_404_and_503_stay_code_less()
    {
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var service = new StubDraftService
        {
            Progress = id => Task.FromResult(new SalesOrderDraftDeliveryProgressResponse("5",
                [new SalesDraftLineDeliveryProgressV1(lineId, 1, unitId, "6", "4", "2.5", "3.5", "0")])),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        var path = $"{SalesEndpoints.DraftsPath}/{draftId:D}/delivery-progress";

        using (var response = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(["draftVersion", "lines"], body.EnumerateObject().Select(member => member.Name));
            Assert.Equal("5", body.GetProperty("draftVersion").GetString());
            var line = Assert.Single(body.GetProperty("lines").EnumerateArray());
            Assert.Equal(["lineId", "lineNo", "orderUnitId", "ordered", "shipped", "delivered", "remaining", "excess"],
                line.EnumerateObject().Select(member => member.Name));
            Assert.Equal((lineId.ToString("D"), 1, "6", "4", "2.5", "3.5", "0"),
                (line.GetProperty("lineId").GetString(), line.GetProperty("lineNo").GetInt32(), line.GetProperty("ordered").GetString(),
                    line.GetProperty("shipped").GetString(), line.GetProperty("delivered").GetString(), line.GetProperty("remaining").GetString(),
                    line.GetProperty("excess").GetString()));
        }

        Assert.Equal([draftId], service.ProgressReads);

        service.Progress = _ => throw new SalesDraftServiceException(404, "not_found");
        using (var missing = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.DoesNotContain("not_found", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        foreach (var code in new[] { SalesDraftProblemCodesV1.DeliveryProviderUnavailable, SalesDraftProblemCodesV1.ReferenceProviderInvalid })
        {
            service.Progress = _ => throw new SalesDraftServiceException(503, code);
            using var unavailable = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.DoesNotContain(code, await unavailable.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Commercial_revision_refuses_invalid_envelopes_before_the_service_and_replays_a_terminal_conflict()
    {
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var priceUnitId = Guid.NewGuid();
        var service = new StubDraftService();
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var path = $"{SalesEndpoints.DraftsPath}/{draftId:D}/commercial-revision";

        using (var noKey = new HttpRequestMessage(HttpMethod.Post, path)
               {
                   Content = new StringContent("{\"expectedVersion\":\"1\",\"lines\":[]}", Encoding.UTF8, "application/json"),
               })
        using (var response = await client.SendAsync(noKey))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        foreach (var json in new[]
                 {
                     "{}",
                     "{\"expectedVersion\":1,\"lines\":[]}",
                     $"{{\"expectedVersion\":\"1\",\"lines\":[{{\"lineId\":\"{lineId:D}\",\"unitPrice\":\"13.00\"}}]}}",
                     $"{{\"expectedVersion\":\"1\",\"lines\":[{{\"lineId\":\"{lineId:D}\",\"unitPrice\":\"13.00\",\"priceUnitId\":\"{priceUnitId:D}\",\"discountPercent\":null,\"extra\":true}}]}}",
                 })
        {
            using var invalid = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            invalid.Headers.Add("Idempotency-Key", "SENTETIK-invalid");
            using var response = await client.SendAsync(invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_failed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        Assert.Empty(service.CommercialCalls);

        service.Commercial = (_, _) => throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.VersionConflict,
            [new("expectedVersion", SalesDraftProblemCodesV1.VersionConflict)], true, "SENTETIK-commercial-first");
        var valid = new SalesDraftCommercialRevisionRequest("4", [new SalesDraftLineCommercialV1(lineId, "13.00", priceUnitId, null)]);
        using var conflict = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(valid, SalesDraftJson.Options), Encoding.UTF8, "application/json"),
        };
        conflict.Headers.Add("Idempotency-Key", "SENTETIK-terminal");
        using var refused = await client.SendAsync(conflict);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("true", Assert.Single(refused.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal("SENTETIK-commercial-first", Assert.Single(refused.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(SalesDraftProblemCodesV1.VersionConflict, problem.GetProperty("code").GetString());
        Assert.Equal("expectedVersion", problem.GetProperty("errors")[0].GetProperty("field").GetString());
    }

    private static HttpRequestMessage StateCommand(Guid draftId, string segment, string idempotencyKey, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{SalesEndpoints.DraftsPath}/{draftId:D}/{segment}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }
}
