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
        null, null, [], null, null, null, null, null, null, new SalesDraftApprovalV1(null, null, null, null, null), false);

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

        public Task<SalesOrderDraftResponse> ReviseCommercialAsync(Guid draftId, SalesDraftCommercialRevisionRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("Not reached by these cases.");

        public Task<SalesOrderDraftRevisionsResponse> GetDraftRevisionsAsync(Guid draftId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not reached by these cases.");

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
