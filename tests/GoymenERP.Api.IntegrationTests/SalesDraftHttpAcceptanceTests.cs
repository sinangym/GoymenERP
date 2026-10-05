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
        null, null, [], null, null, null, null, null, null, new SalesDraftApprovalV1(null, null, null, null, null),
        new SalesDraftCustomerAcceptanceV1(false, null, null, null, false), false, false);

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

        public Task<SalesOrderDraftResponse> ApprovePriceFloorExceptionsAsync(Guid draftId,
            SalesDraftPriceFloorExceptionApprovalRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesOrderDraftResponse(Detail(draftId), false, "SENTETIK-corr"));

        public Func<Guid, Task<SalesOrderDraftRevisionsResponse>> Revisions { get; set; } =
            _ => Task.FromResult(new SalesOrderDraftRevisionsResponse([]));

        public List<Guid> RevisionReads { get; } = [];

        /// <summary>AT-018: the HTTP surface of the line-pricing reads is proven with the real service; the stub answers 404.</summary>
        public Task<SalesOrderDraftLinePricingResponse> GetDraftLinePricingAsync(Guid draftId, CancellationToken cancellationToken = default) =>
            Task.FromException<SalesOrderDraftLinePricingResponse>(new SalesDraftServiceException(404, "not_found"));

        public Task<SalesOrderDraftPriceReferencesResponse> GetLinePriceReferencesAsync(Guid draftId, Guid lineId,
            CancellationToken cancellationToken = default) =>
            Task.FromException<SalesOrderDraftPriceReferencesResponse>(new SalesDraftServiceException(404, "not_found"));

        public Task<SalesOrderDraftRevisionsResponse> GetDraftRevisionsAsync(Guid draftId, CancellationToken cancellationToken = default)
        {
            RevisionReads.Add(draftId);
            return Revisions(draftId);
        }

        /// <summary>DEC-SAL-002: what the stub answers a quantity revision with; the calls reach <see cref="QuantityCalls"/>.</summary>
        public Func<Guid, SalesDraftQuantityRevisionRequest, Task<SalesOrderDraftResponse>> Quantity { get; set; } =
            (_, _) => throw new SalesDraftServiceException(404, "not_found");

        public List<(Guid DraftId, SalesDraftQuantityRevisionRequest Request, string Key)> QuantityCalls { get; } = [];

        public Task<SalesOrderDraftResponse> ReviseQuantityAsync(Guid draftId, SalesDraftQuantityRevisionRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            QuantityCalls.Add((draftId, request, metadata.IdempotencyKey));
            return Quantity(draftId, request);
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

        public Task<SalesOrderDraftResponse> RecordCustomerAcceptanceAsync(Guid draftId, SalesDraftStateCommandRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            StateAsync("customer-acceptance", draftId, request, metadata);

        public Task<SalesOrderDraftResponse> WithdrawDraftAsync(Guid draftId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default) => StateAsync("withdraw", draftId, request, metadata);

        public Task<SalesOrderDraftResponse> RejectDraftAsync(Guid draftId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default) => StateAsync("reject", draftId, request, metadata);

        /// <summary>DEC-SAL-005: the preview, the command and the record; what the stub answers and what reached it.</summary>
        public Func<Guid, Task<SalesOrderDraftCancellationImpactResponse>> Impact { get; set; } =
            _ => throw new SalesDraftServiceException(404, "not_found");

        public Func<Guid, SalesDraftCancelRequest, Task<SalesOrderDraftResponse>> Cancel { get; set; } =
            (_, _) => throw new SalesDraftServiceException(404, "not_found");

        public Func<Guid, Task<SalesOrderDraftCancellationResponse>> Cancellation { get; set; } =
            _ => Task.FromResult(new SalesOrderDraftCancellationResponse(null));

        public List<(Guid DraftId, SalesDraftCancelRequest Request, string Key)> CancelCalls { get; } = [];

        public Task<SalesOrderDraftCancellationImpactResponse> GetCancellationImpactAsync(Guid draftId, CancellationToken cancellationToken = default) =>
            Impact(draftId);

        public Task<SalesOrderDraftResponse> CancelDraftAsync(Guid draftId, SalesDraftCancelRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            CancelCalls.Add((draftId, request, metadata.IdempotencyKey));
            return Cancel(draftId, request);
        }

        public Task<SalesOrderDraftCancellationResponse> GetDraftCancellationAsync(Guid draftId, CancellationToken cancellationToken = default) =>
            Cancellation(draftId);

        public Func<Guid, SalesDraftRemainderChangeRequest, Task<SalesOrderDraftResponse>> RemainderRequest { get; set; } =
            (_, _) => throw new SalesDraftServiceException(404, "not_found");

        public Func<string, Guid, int, SalesDraftRemainderDecisionRequest, Task<SalesOrderDraftResponse>> RemainderDecision { get; set; } =
            (_, _, _, _) => throw new SalesDraftServiceException(404, "not_found");

        public Func<Guid, Task<SalesOrderDraftRemainderChangesResponse>> RemainderRead { get; set; } =
            _ => throw new SalesDraftServiceException(404, "not_found");

        public List<(Guid DraftId, SalesDraftRemainderChangeRequest Request, string Key)> RemainderRequests { get; } = [];
        public List<(string Command, Guid DraftId, int ChangeNo, SalesDraftRemainderDecisionRequest Request, string Key)> RemainderDecisions { get; } = [];
        public List<Guid> RemainderReads { get; } = [];

        public Task<SalesOrderDraftResponse> RequestRemainderChangeAsync(Guid draftId, SalesDraftRemainderChangeRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            RemainderRequests.Add((draftId, request, metadata.IdempotencyKey));
            return RemainderRequest(draftId, request);
        }

        public Task<SalesOrderDraftResponse> ApproveRemainderChangeAsync(Guid draftId, int changeNo,
            SalesDraftRemainderDecisionRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            RemainderDecisionAsync("approve", draftId, changeNo, request, metadata);

        public Task<SalesOrderDraftResponse> RejectRemainderChangeAsync(Guid draftId, int changeNo,
            SalesDraftRemainderDecisionRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            RemainderDecisionAsync("reject", draftId, changeNo, request, metadata);

        public Task<SalesOrderDraftRemainderChangesResponse> GetRemainderChangesAsync(Guid draftId,
            CancellationToken cancellationToken = default)
        {
            RemainderReads.Add(draftId);
            return RemainderRead(draftId);
        }

        private Task<SalesOrderDraftResponse> RemainderDecisionAsync(string command, Guid draftId, int changeNo,
            SalesDraftRemainderDecisionRequest request, SalesCommandMetadata metadata)
        {
            RemainderDecisions.Add((command, draftId, changeNo, request, metadata.IdempotencyKey));
            return RemainderDecision(command, draftId, changeNo, request);
        }

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
    [InlineData("customer-acceptance", "customer-acceptance")]
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
    public async Task Remainder_routes_preserve_the_strict_commands_replay_headers_and_private_read_shape()
    {
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var service = new StubDraftService
        {
            RemainderRequest = (id, _) => Task.FromResult(new SalesOrderDraftResponse(Detail(id, "Approved"), false, "SENTETIK-request")),
            RemainderDecision = (_, id, _, _) => Task.FromResult(new SalesOrderDraftResponse(Detail(id, "Approved"), true, "SENTETIK-decision-first")),
            RemainderRead = _ => Task.FromResult(new SalesOrderDraftRemainderChangesResponse("7", SalesRemainderChangeNamesV1.Readable,
                [new(lineId, 1, unitId, "5", SalesRemainderChangeNamesV1.Open, "3", "2", "1", "3")], [])),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var path = $"{SalesEndpoints.DraftsPath}/{draftId:D}/remainder-changes";

        using (var invalid = StateCommand(draftId, "remainder-changes", "SENTETIK-invalid",
                   $"{{\"expectedVersion\":\"7\",\"lineId\":\"{lineId:D}\",\"kind\":\"Cancel\",\"quantity\":\"1\",\"reason\":\"x\"}}"))
        using (var response = await client.SendAsync(invalid))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Empty(service.RemainderRequests);

        var requestBody = JsonSerializer.Serialize(
            new SalesDraftRemainderChangeRequest("7", lineId, SalesRemainderChangeNamesV1.Revise, "4", "SENTETIK kalan"),
            SalesDraftJson.Options);
        using (var response = await client.SendAsync(StateCommand(draftId, "remainder-changes", "SENTETIK-request", requestBody)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        }
        var requested = Assert.Single(service.RemainderRequests);
        Assert.Equal((draftId, "7", lineId, "Revise", "4", "SENTETIK-request"),
            (requested.DraftId, requested.Request.ExpectedVersion, requested.Request.LineId, requested.Request.Kind,
                requested.Request.Quantity, requested.Key));

        var decisionBody = JsonSerializer.Serialize(new SalesDraftRemainderDecisionRequest("8"), SalesDraftJson.Options);
        using (var response = await client.SendAsync(StateCommand(draftId, "remainder-changes/1/approval", "SENTETIK-approve", decisionBody)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-decision-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        var rejectionBody = JsonSerializer.Serialize(new SalesDraftRemainderDecisionRequest("9", "SENTETIK ret"), SalesDraftJson.Options);
        using (var response = await client.SendAsync(StateCommand(draftId, "remainder-changes/2/rejection", "SENTETIK-reject", rejectionBody)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.Equal([("approve", draftId, 1, "8", (string?)null, "SENTETIK-approve"),
            ("reject", draftId, 2, "9", "SENTETIK ret", "SENTETIK-reject")],
            service.RemainderDecisions.Select(call =>
                (call.Command, call.DraftId, call.ChangeNo, call.Request.ExpectedVersion, call.Request.Reason, call.Key)));

        using var read = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True(read.Headers.CacheControl is { Private: true, NoStore: true });
        var body = await read.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["draftVersion", "floorStatus", "lines", "items"], body.EnumerateObject().Select(member => member.Name));
        Assert.Equal("Readable", body.GetProperty("floorStatus").GetString());
        Assert.Equal([draftId], service.RemainderReads);
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
                    [new SalesDraftRevisionLineV1(lineId, 1, Guid.NewGuid(), "3", Guid.NewGuid(), "2.5", "13.00", priceUnitId, "5", SalesPriceDisclosure.Visible)],
                    null)
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
        // DEC-SAL-002: the member is present (a price revision archives with no reason).
        Assert.Equal(JsonValueKind.Null, item.GetProperty("reason").ValueKind);
        Assert.Equal([draftId], service.RevisionReads);
    }

    /// <summary>
    /// DEC-SAL-002 / AT-023: the quantity-revision route's wire — one canonical key and the strict body {expectedVersion, reason,
    /// lines[{lineId, quantity}]} before the service is reached (no unit, price or slice member, no numeric quantity); 200
    /// with the detail after the change and the replay headers on a replay; a blocked revision is a 409 that names each
    /// blocking source and carries the replay headers when it is replayed; a 503 stays code-less.
    /// </summary>
    [Fact]
    public async Task Quantity_revision_uses_the_strict_owner_contract_replay_headers_and_a_named_blocked_conflict()
    {
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var service = new StubDraftService
        {
            Quantity = (_, _) => Task.FromResult(new SalesOrderDraftResponse(Detail(draftId), true, "SENTETIK-quantity-first")),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var path = $"{SalesEndpoints.DraftsPath}/{draftId:D}/quantity-revision";

        foreach (var json in new[]
                 {
                     "{}",
                     $"{{\"expectedVersion\":\"4\",\"lines\":[{{\"lineId\":\"{lineId:D}\",\"quantity\":\"2\"}}]}}",
                     $"{{\"expectedVersion\":\"4\",\"reason\":\"r\",\"lines\":[{{\"lineId\":\"{lineId:D}\",\"quantity\":2}}]}}",
                     $"{{\"expectedVersion\":\"4\",\"reason\":\"r\",\"lines\":[{{\"lineId\":\"{lineId:D}\",\"quantity\":\"2\",\"orderUnitId\":\"{lineId:D}\"}}]}}",
                     $"{{\"expectedVersion\":\"4\",\"reason\":\"r\",\"lines\":[{{\"lineId\":\"{lineId:D}\",\"quantity\":\"0\"}}]}}",
                 })
        {
            using var invalid = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            invalid.Headers.Add("Idempotency-Key", "SENTETIK-invalid");
            using var response = await client.SendAsync(invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_failed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        var body = new SalesDraftQuantityRevisionRequest("4", "SENTETIK neden", [new SalesDraftLineQuantityV1(lineId, "2.5")]);
        using (var noKey = new HttpRequestMessage(HttpMethod.Post, path)
               {
                   Content = new StringContent(JsonSerializer.Serialize(body, SalesDraftJson.Options), Encoding.UTF8, "application/json"),
               })
        using (var response = await client.SendAsync(noKey))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Empty(service.QuantityCalls);

        using (var request = new HttpRequestMessage(HttpMethod.Post, path)
               {
                   Content = new StringContent(JsonSerializer.Serialize(body, SalesDraftJson.Options), Encoding.UTF8, "application/json"),
               })
        {
            request.Headers.Add("Idempotency-Key", "SENTETIK-quantity-1");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-quantity-first", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        var call = Assert.Single(service.QuantityCalls);
        Assert.Equal((draftId, "4", "SENTETIK neden", "SENTETIK-quantity-1"), (call.DraftId, call.Request.ExpectedVersion, call.Request.Reason, call.Key));
        Assert.Equal((lineId, "2.5"), (Assert.Single(call.Request.Lines).LineId, call.Request.Lines[0].Quantity));

        service.Quantity = (_, _) => throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.RevisionBlocked,
            [new("inventory.active-allocations", "Present"), new("logistics.loading-without-dispatch-note", "Present")], true, "SENTETIK-quantity-first");
        using (var blocked = new HttpRequestMessage(HttpMethod.Post, path)
               {
                   Content = new StringContent(JsonSerializer.Serialize(body, SalesDraftJson.Options), Encoding.UTF8, "application/json"),
               })
        {
            blocked.Headers.Add("Idempotency-Key", "SENTETIK-quantity-2");
            using var refused = await client.SendAsync(blocked);
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal("true", Assert.Single(refused.Headers.GetValues("Idempotency-Replayed")));
            var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(SalesDraftProblemCodesV1.RevisionBlocked, problem.GetProperty("code").GetString());
            Assert.Equal(["inventory.active-allocations", "logistics.loading-without-dispatch-note"],
                problem.GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("field").GetString()));
        }

        service.Quantity = (_, _) => throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.RevisionImpactUnavailable);
        using var unavailableRequest = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, SalesDraftJson.Options), Encoding.UTF8, "application/json"),
        };
        unavailableRequest.Headers.Add("Idempotency-Key", "SENTETIK-quantity-3");
        using var unavailable = await client.SendAsync(unavailableRequest);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.DoesNotContain(SalesDraftProblemCodesV1.RevisionImpactUnavailable, await unavailable.Content.ReadAsStringAsync(), StringComparison.Ordinal);
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

    /// <summary>
    /// DEC-SAL-005 / AT-007: the three cancellation routes' wire — the preview and the record are private reads whose 404/503
    /// stay code-less; the command needs one canonical key and the strict body {expectedVersion, reason, impactToken} before
    /// the service is reached, answers 200 with the detail after the change, and carries the replay headers on a replayed
    /// result and on a replayed terminal refusal (the blocked cancellation names its blocking source).
    /// </summary>
    [Fact]
    public async Task Cancellation_preview_command_and_record_use_the_strict_owner_contract_and_replay_headers()
    {
        var draftId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var token = new string('d', 64);
        var service = new StubDraftService
        {
            Impact = id => Task.FromResult(new SalesOrderDraftCancellationImpactResponse("4", "Rev.0", false, token,
                [new(SalesCancellationImpactSourcesV1.ActiveAllocations, SalesCancellationImpactStatusesV1.Present),
                 new(SalesCancellationImpactSourcesV1.LinkedPurchasing, SalesCancellationImpactStatusesV1.NoSource)],
                [new(lineId, 1, unitId, "6", 1, "2.5", null, null, null, null, null)])),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        var impactPath = $"{SalesEndpoints.DraftsPath}/{draftId:D}/cancellation-impact";
        var path = $"{SalesEndpoints.DraftsPath}/{draftId:D}/cancellation";

        using (var preview = await client.GetAsync(impactPath))
        {
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.True(preview.Headers.CacheControl is { Private: true, NoStore: true });
            var body = await preview.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(["draftVersion", "revision", "cancellable", "impactToken", "sources", "lines"], body.EnumerateObject().Select(member => member.Name));
            Assert.Equal(["source", "status"], body.GetProperty("sources")[0].EnumerateObject().Select(member => member.Name));
            var line = Assert.Single(body.GetProperty("lines").EnumerateArray());
            Assert.Equal(["lineId", "lineNo", "orderUnitId", "ordered", "activeAllocations", "allocatedKg", "loadingInstructionLines",
                "instructed", "loaded", "shipped", "delivered"], line.EnumerateObject().Select(member => member.Name));
            // An owner that could not be read is null on the wire, never 0.
            Assert.Equal((1, "2.5", JsonValueKind.Null), (line.GetProperty("activeAllocations").GetInt32(), line.GetProperty("allocatedKg").GetString(),
                line.GetProperty("instructed").ValueKind));
        }

        foreach (var status in new[] { 404, 503 })
        {
            service.Impact = _ => throw new SalesDraftServiceException(status, status == 404 ? "not_found" : SalesDraftProblemCodesV1.CancellationImpactUnavailable);
            using var refused = await client.GetAsync(impactPath);
            Assert.Equal(status, (int)refused.StatusCode);
            Assert.DoesNotContain(SalesDraftProblemCodesV1.CancellationImpactUnavailable, await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using (var noKey = new HttpRequestMessage(HttpMethod.Post, path)
               {
                   Content = new StringContent($"{{\"expectedVersion\":\"4\",\"reason\":\"x\",\"impactToken\":\"{token}\"}}", Encoding.UTF8, "application/json"),
               })
        using (var response = await client.SendAsync(noKey))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        foreach (var json in new[]
                 {
                     "{}",
                     $"{{\"expectedVersion\":4,\"reason\":\"x\",\"impactToken\":\"{token}\"}}",
                     $"{{\"expectedVersion\":\"4\",\"impactToken\":\"{token}\"}}",
                     "{\"expectedVersion\":\"4\",\"reason\":\"x\",\"impactToken\":\" \"}",
                     $"{{\"expectedVersion\":\"4\",\"reason\":\"x\",\"impactToken\":\"{token}\",\"extra\":true}}",
                     $"{{\"expectedVersion\":\"04\",\"reason\":\"x\",\"impactToken\":\"{token}\"}}",
                 })
        {
            using var invalid = StateCommand(draftId, "cancellation", "SENTETIK-invalid", json);
            using var response = await client.SendAsync(invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_failed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        Assert.Empty(service.CancelCalls);

        var valid = JsonSerializer.Serialize(new SalesDraftCancelRequest("4", "SENTETIK neden", token), SalesDraftJson.Options);
        service.Cancel = (id, request) => Task.FromResult(new SalesOrderDraftResponse(Detail(id, "Approved"), request.Reason == "replayed", "SENTETIK-cancel-first"));
        using (var cancelled = await client.SendAsync(StateCommand(draftId, "cancellation", "SENTETIK-cancel", valid)))
        {
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            Assert.False(cancelled.Headers.Contains("Idempotency-Replayed"));
        }

        Assert.Equal((draftId, "4", "SENTETIK neden", token, "SENTETIK-cancel"),
            (service.CancelCalls[0].DraftId, service.CancelCalls[0].Request.ExpectedVersion, service.CancelCalls[0].Request.Reason,
                service.CancelCalls[0].Request.ImpactToken, service.CancelCalls[0].Key));

        using (var replayed = await client.SendAsync(StateCommand(draftId, "cancellation", "SENTETIK-cancel-2",
                   JsonSerializer.Serialize(new SalesDraftCancelRequest("4", "replayed", token), SalesDraftJson.Options))))
        {
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal("SENTETIK-cancel-first", Assert.Single(replayed.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        service.Cancel = (_, _) => throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.CancellationBlocked,
            [new(SalesCancellationImpactSourcesV1.ActiveAllocations, SalesCancellationImpactStatusesV1.Present)], true, "SENTETIK-blocked-first");
        using (var blocked = await client.SendAsync(StateCommand(draftId, "cancellation", "SENTETIK-blocked", valid)))
        {
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Equal("true", Assert.Single(blocked.Headers.GetValues("Idempotency-Replayed")));
            var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(SalesDraftProblemCodesV1.CancellationBlocked, problem.GetProperty("code").GetString());
            Assert.Equal(SalesCancellationImpactSourcesV1.ActiveAllocations, problem.GetProperty("errors")[0].GetProperty("field").GetString());
        }

        service.Cancel = (_, _) => throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.CancellationImpactUnavailable);
        using (var unavailable = await client.SendAsync(StateCommand(draftId, "cancellation", "SENTETIK-503", valid)))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.DoesNotContain(SalesDraftProblemCodesV1.CancellationImpactUnavailable, await unavailable.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        service.Cancellation = id => Task.FromResult(new SalesOrderDraftCancellationResponse(new SalesDraftCancellationV1("Rev.0", "SENTETIK-canceller",
            new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero), "SENTETIK neden", token,
            [new(SalesCancellationImpactSourcesV1.ActiveAllocations, SalesCancellationImpactStatusesV1.None)])));
        using (var record = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, record.StatusCode);
            var body = await record.Content.ReadFromJsonAsync<JsonElement>();
            var cancellation = body.GetProperty("cancellation");
            Assert.Equal(["revision", "cancelledBy", "cancelledAtUtc", "reason", "impactToken", "sources"], cancellation.EnumerateObject().Select(member => member.Name));
            Assert.Equal("SENTETIK neden", cancellation.GetProperty("reason").GetString());
        }
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
