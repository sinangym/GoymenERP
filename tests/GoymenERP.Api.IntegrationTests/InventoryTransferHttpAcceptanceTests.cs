using System.Net;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Errors;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// B-22a HTTP boundary of the transfer plan, dispatch and read. The owner command and query are doubles: this proves
/// only the host contract (one canonical Idempotency-Key, strict JSON, 201/200 replay headers, code-less 401/403/404/503,
/// the closed 400/409/422 code set per operation, recorded dispatch refusals replayed with their headers). No accept
/// route is published.
/// </summary>
public sealed class InventoryTransferHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid TransferId = Guid.Parse("7a000000-0000-4000-8000-000000000001");
    private static readonly Guid LineId = Guid.Parse("7a000000-0000-4000-8000-000000000002");
    private static readonly Guid LabelId = Guid.Parse("7a000000-0000-4000-8000-000000000003");
    private static readonly Guid SourceLocation = Guid.Parse("7a000000-0000-4000-8000-000000000004");
    private static readonly Guid DestinationLocation = Guid.Parse("7a000000-0000-4000-8000-000000000005");
    private static readonly Guid OutgoingDocument = Guid.Parse("7a000000-0000-4000-8000-000000000006");
    private static readonly DateTimeOffset At = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Plan_answers_201_with_the_transfer_and_forwards_the_key_under_the_transfer_write_permission()
    {
        var command = new StubCommand();
        using var host = CreateHost(command);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(InventoryTransferApiV1.Path, PlanBody(), "plan-key-1"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        var (request, transport) = Assert.Single(command.Plans);
        Assert.Equal((TransferId, LineId, "1", "5"),
            (request.TransferId, request.Lines[0].LineId, request.Lines[0].ExpectedLabelVersion, request.Lines[0].PlannedKg));
        Assert.Equal(("plan-key-1", ErpPermissions.InventoryStockTransferWrite),
            (transport.IdempotencyKey, transport.PermissionCode));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(TransferId, document.RootElement.GetProperty("transferId").GetGuid());
        Assert.Equal("Planned", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Dispatch_replay_answers_200_with_the_original_correlation()
    {
        var command = new StubCommand { Replay = true };
        using var host = CreateHost(command);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(DispatchPath(TransferId), DispatchBody(), "dispatch-key-1"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal("original-correlation",
            Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        var (transferId, request, transport) = Assert.Single(command.Dispatches);
        Assert.Equal((TransferId, OutgoingDocument, "4", "dispatch-key-1"),
            (transferId, request.OutgoingDocumentId, request.ExpectedOutgoingDocumentVersion, transport.IdempotencyKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData(" padded-key")]
    [InlineData("a,b")]
    public async Task A_missing_or_non_canonical_idempotency_key_is_400_before_the_owner(string? key)
    {
        var command = new StubCommand();
        using var host = CreateHost(command);
        using var client = CreateClient(host);

        using var plan = await client.SendAsync(Post(InventoryTransferApiV1.Path, PlanBody(), key));
        using var dispatch = await client.SendAsync(Post(DispatchPath(TransferId), DispatchBody(), key));

        await AssertCodedProblem(plan, HttpStatusCode.BadRequest, InventoryTransferCodesV1.ValidationFailed);
        await AssertCodedProblem(dispatch, HttpStatusCode.BadRequest, InventoryTransferCodesV1.ValidationFailed);
        Assert.Empty(command.Plans);
        Assert.Empty(command.Dispatches);
    }

    [Theory]
    // An unknown member: the client may not name the outgoing document's kind or owner.
    [InlineData("{\"expectedTransferVersion\":\"1\",\"dispatchedAtUtc\":\"2026-11-11T10:00:00Z\","
        + "\"outgoingDocumentId\":\"7a000000-0000-4000-8000-000000000006\",\"expectedOutgoingDocumentVersion\":\"4\","
        + "\"kind\":\"logistics.transfer-outgoing-document\"}")]
    // A missing required member.
    [InlineData("{\"expectedTransferVersion\":\"1\",\"dispatchedAtUtc\":\"2026-11-11T10:00:00Z\","
        + "\"outgoingDocumentId\":\"7a000000-0000-4000-8000-000000000006\"}")]
    [InlineData("null")]
    [InlineData("{")]
    public async Task A_non_strict_dispatch_body_is_400_before_the_owner(string body)
    {
        var command = new StubCommand();
        using var host = CreateHost(command);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(DispatchPath(TransferId), body, "strict-key"));

        await AssertCodedProblem(response, HttpStatusCode.BadRequest, InventoryTransferCodesV1.ValidationFailed);
        Assert.Empty(command.Dispatches);
    }

    [Fact]
    public async Task A_non_json_body_is_415_before_the_owner()
    {
        var command = new StubCommand();
        using var host = CreateHost(command);
        using var client = CreateClient(host);
        using var request = new HttpRequestMessage(HttpMethod.Post, InventoryTransferApiV1.Path)
        {
            Content = new StringContent(PlanBody(), Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("Idempotency-Key", "media-key");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(command.Plans);
    }

    [Theory]
    [InlineData(401, InventoryTransferCodesV1.Unauthenticated, 401)]
    [InlineData(403, InventoryTransferCodesV1.Forbidden, 403)]
    [InlineData(404, InventoryTransferCodesV1.TransferNotFound, 404)]
    [InlineData(503, InventoryTransferCodesV1.PermissionUnregistered, 503)]
    [InlineData(503, InventoryTransferCodesV1.Unavailable, 503)]
    // A code outside the operation's closed set never leaks: it is a code-less 503.
    [InlineData(409, InventoryTransferCodesV1.DestinationNotFound, 503)]
    [InlineData(422, InventoryTransferCodesV1.WholeLabelRequired, 503)]
    public async Task Platform_and_unpublished_dispatch_refusals_are_code_less(int status, string code, int expected)
    {
        var command = new StubCommand { Failure = new InventoryTransferException(status, code) };
        using var host = CreateHost(command);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(DispatchPath(TransferId), DispatchBody(), "code-less-key"));

        await AssertPlatformProblem(response, (HttpStatusCode)expected);
        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
    }

    [Theory]
    [InlineData(409, InventoryTransferCodesV1.OutgoingDocumentNotFinal)]
    [InlineData(409, InventoryTransferCodesV1.OutgoingDocumentCancelled)]
    [InlineData(409, InventoryTransferCodesV1.OutgoingDocumentReversed)]
    [InlineData(409, InventoryTransferCodesV1.OutgoingDocumentVersionConflict)]
    [InlineData(409, InventoryTransferCodesV1.TransferVersionConflict)]
    [InlineData(422, InventoryTransferCodesV1.OutgoingDocumentNotFound)]
    [InlineData(422, InventoryTransferCodesV1.OutgoingDocumentBindingMismatch)]
    public async Task Owner_dispatch_refusals_carry_their_closed_code(int status, string code)
    {
        var command = new StubCommand { Failure = new InventoryTransferException(status, code) };
        using var host = CreateHost(command);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(DispatchPath(TransferId), DispatchBody(), "coded-key"));

        await AssertCodedProblem(response, (HttpStatusCode)status, code);
        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
    }

    [Fact]
    public async Task A_recorded_dispatch_refusal_replays_its_409_with_the_original_correlation()
    {
        var command = new StubCommand
        {
            Failure = new InventoryTransferException(409, InventoryTransferCodesV1.OutgoingDocumentNotFinal, true,
                "refusal-correlation"),
        };
        using var host = CreateHost(command);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(DispatchPath(TransferId), DispatchBody(), "refused-key"));

        await AssertCodedProblem(response, HttpStatusCode.Conflict, InventoryTransferCodesV1.OutgoingDocumentNotFinal);
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal("refusal-correlation",
            Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
    }

    [Theory]
    [InlineData(422, InventoryTransferCodesV1.DestinationInactive, 422)]
    [InlineData(409, InventoryTransferCodesV1.LabelVersionConflict, 409)]
    // Dispatch-only codes are not part of the plan contract.
    [InlineData(409, InventoryTransferCodesV1.OutgoingDocumentNotFinal, 503)]
    [InlineData(422, InventoryTransferCodesV1.OutgoingDocumentNotFound, 503)]
    public async Task Plan_refusals_follow_the_plan_closed_code_set(int status, string code, int expected)
    {
        var command = new StubCommand { Failure = new InventoryTransferException(status, code) };
        using var host = CreateHost(command);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(InventoryTransferApiV1.Path, PlanBody(), "plan-refusal-key"));

        if (expected == status)
        {
            await AssertCodedProblem(response, (HttpStatusCode)status, code);
        }
        else
        {
            await AssertPlatformProblem(response, (HttpStatusCode)expected);
        }
    }

    [Fact]
    public async Task Get_answers_the_transfer_private_no_store()
    {
        var query = new StubQuery();
        using var host = CreateHost(new StubCommand(), query);
        using var client = CreateClient(host);

        using var response = await client.GetAsync($"{InventoryTransferApiV1.Path}/{TransferId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
        Assert.Equal([TransferId], query.Reads);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("1", document.RootElement.GetProperty("version").GetString());
    }

    [Theory]
    [InlineData(401, 401)]
    [InlineData(403, 403)]
    [InlineData(404, 404)]
    [InlineData(503, 503)]
    [InlineData(409, 503)]
    public async Task Get_refusals_are_code_less(int status, int expected)
    {
        var query = new StubQuery { Failure = new InventoryTransferException(status, "any_code") };
        using var host = CreateHost(new StubCommand(), query);
        using var client = CreateClient(host);

        using var response = await client.GetAsync($"{InventoryTransferApiV1.Path}/{TransferId:D}");

        await AssertPlatformProblem(response, (HttpStatusCode)expected);
    }

    [Fact]
    public async Task The_accept_route_is_not_published()
    {
        var command = new StubCommand();
        using var host = CreateHost(command);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(
            $"{InventoryTransferApiV1.Path}/{TransferId:D}/accept-lines/{LineId:D}", "{}", "accept-key"));

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"Unexpected {response.StatusCode}.");
        Assert.Equal(0, command.AcceptCalls);
    }

    [Fact]
    public async Task The_edge_policy_refuses_a_caller_without_the_transfer_permission_before_the_owner()
    {
        var command = new StubCommand();
        var query = new StubQuery();
        await using var host = CreateHost(command, query, granted: []);
        using var client = CreateClient(host);

        using var plan = await client.SendAsync(Post(InventoryTransferApiV1.Path, PlanBody(), "plan-key"));
        using var dispatch = await client.SendAsync(Post(DispatchPath(TransferId), DispatchBody(), "dispatch-key"));
        using var read = await client.GetAsync($"{InventoryTransferApiV1.Path}/{TransferId:D}");

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            (plan.StatusCode, dispatch.StatusCode, read.StatusCode));
        Assert.Equal((0, 0, 0), (command.Plans.Count, command.Dispatches.Count, query.Reads.Count));
    }

    /// <summary>
    /// The edge policy (RequireAuthorization) is answered by a test-only authority: registration grants nothing in the
    /// product, so the host contract is exercised with the two transfer permissions granted here and nowhere else.
    /// </summary>
    private WebApplicationFactory<Program> CreateHost(StubCommand command, StubQuery? query = null,
        string[]? granted = null)
    {
        query ??= new StubQuery();
        var authority = new GrantingAuthority(granted
            ?? [ErpPermissions.InventoryStockTransferWrite, ErpPermissions.InventoryBalancesRead]);
        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IInventoryTransferCommandV1>();
            services.AddSingleton<IInventoryTransferCommandV1>(command);
            services.RemoveAll<IInventoryTransferQueryV1>();
            services.AddSingleton<IInventoryTransferQueryV1>(query);
            services.RemoveAll<IErpAuthorizationService>();
            services.AddSingleton<IErpAuthorizationService>(authority);
        }));
    }

    private sealed class GrantingAuthority(string[] granted) : IErpAuthorizationService
    {
        public Task<bool> HasPermissionAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(Allowed(permission, companyId));

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId,
            IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthorizationSnapshot("b22a-http-only", new HashSet<Guid> { TestCompanyIds.CompanyA },
                permissions.ToDictionary(permission => permission, permission => Scope(permission, companyId),
                    StringComparer.Ordinal)));

        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(Scope(permission, companyId));

        private bool Allowed(string permission, Guid companyId) =>
            companyId == TestCompanyIds.CompanyA && granted.Contains(permission, StringComparer.Ordinal);

        private PermissionScope Scope(string permission, Guid companyId) =>
            new(Allowed(permission, companyId), true, new HashSet<string>(StringComparer.Ordinal) { "B22A-HTTP" },
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private static HttpRequestMessage Post(string uri, string body, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    private static string DispatchPath(Guid transferId) => $"{InventoryTransferApiV1.Path}/{transferId:D}/dispatch";

    private static string PlanBody() => JsonSerializer.Serialize(new PlanInventoryTransferV1(TransferId, SourceLocation,
        "Company", TestCompanyIds.CompanyA, DestinationLocation, "Company", TestCompanyIds.CompanyA, At,
        [new PlanInventoryTransferLineV1(LineId, LabelId, "1", "5")]), new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string DispatchBody() => JsonSerializer.Serialize(
        new DispatchInventoryTransferV1("1", At.AddHours(1), OutgoingDocument, "4"),
        new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>
    /// A platform problem: the owner's code never reaches the wire; 401/403/503 carry only the platform's own safe code
    /// and 404 none.
    /// </summary>
    private static async Task AssertPlatformProblem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string? expected = status switch
        {
            HttpStatusCode.Unauthorized => ErpProblemCodes.AuthenticationRequired,
            HttpStatusCode.Forbidden => ErpProblemCodes.Forbidden,
            HttpStatusCode.ServiceUnavailable => ErpProblemCodes.ServiceUnavailable,
            _ => null,
        };
        Assert.Equal(expected, document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null);
    }

    private static async Task AssertCodedProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("correlationId").GetString()));
    }

    private static InventoryTransferViewV1 View(string version, string status) => new(TransferId,
        TestCompanyIds.CompanyA, version, status, SourceLocation, "Company", TestCompanyIds.CompanyA,
        DestinationLocation, "Company", TestCompanyIds.CompanyA, At, status == "Planned" ? null : At.AddHours(1),
        [new InventoryTransferLineViewV1(LineId, LabelId, "1", "5", null, null, status)]);

    private sealed class StubCommand : IInventoryTransferCommandV1
    {
        public bool Replay { get; init; }
        public InventoryTransferException? Failure { get; init; }
        public List<(PlanInventoryTransferV1, InventoryCommandMetadata)> Plans { get; } = [];
        public List<(Guid, DispatchInventoryTransferV1, InventoryCommandMetadata)> Dispatches { get; } = [];
        public int AcceptCalls { get; private set; }

        public Task<InventoryTransferResultV1> PlanAsync(PlanInventoryTransferV1 request,
            InventoryCommandMetadata transport, CancellationToken cancellationToken = default)
        {
            Plans.Add((request, transport));
            return Answer(View("1", "Planned"));
        }

        public Task<InventoryTransferResultV1> DispatchAsync(Guid transferId, DispatchInventoryTransferV1 request,
            InventoryCommandMetadata transport, CancellationToken cancellationToken = default)
        {
            Dispatches.Add((transferId, request, transport));
            return Answer(View("2", "InTransit"));
        }

        public Task<InventoryTransferResultV1> AcceptLineAsync(Guid transferId, Guid lineId,
            AcceptInventoryTransferLineV1 request, InventoryCommandMetadata transport,
            CancellationToken cancellationToken = default)
        {
            AcceptCalls++;
            throw new InvalidOperationException("Accept is not published in B-22a.");
        }

        private Task<InventoryTransferResultV1> Answer(InventoryTransferViewV1 view) => Failure is not null
            ? Task.FromException<InventoryTransferResultV1>(Failure)
            : Task.FromResult(new InventoryTransferResultV1(view, Replay, Replay ? "original-correlation" : "current"));
    }

    private sealed class StubQuery : IInventoryTransferQueryV1
    {
        public InventoryTransferException? Failure { get; init; }
        public List<Guid> Reads { get; } = [];

        public Task<InventoryTransferViewV1> GetAsync(Guid transferId, CancellationToken cancellationToken = default)
        {
            Reads.Add(transferId);
            return Failure is not null
                ? Task.FromException<InventoryTransferViewV1>(Failure)
                : Task.FromResult(View("1", "Planned"));
        }
    }
}

/// <summary>
/// B-22a: the Organization destination adapter (DEC-ORG-004). Missing or foreign is NotFound, passive is Inactive;
/// an internal location is held by the transaction company, an external one by its operator party; anything else is a
/// context mismatch. An owner fault propagates (the command answers 503 without a stock effect).
/// </summary>
public sealed class InventoryTransferDestinationHostPortTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Location = Guid.Parse("4f000000-0000-4000-8000-000000000011");
    private static readonly Guid Operator = Guid.Parse("71000000-0000-4000-8000-000000000011");
    private static readonly Guid OtherParty = Guid.Parse("71000000-0000-4000-8000-000000000012");

    [Fact]
    public async Task A_missing_location_or_another_location_identity_is_not_found()
    {
        var missing = new Destinations(null);
        Assert.Equal(InventoryTransferDestinationStatusV1.NotFound, await new OrganizationInventoryTransferDestinationPort(
            missing).CheckAsync(Company, Location, "Company", Company));
        Assert.Equal([(Company, Location)], missing.Reads);

        var other = new Destinations(new OrganizationTransferDestinationV1(Guid.NewGuid(), true, false, null, 1));
        Assert.Equal(InventoryTransferDestinationStatusV1.NotFound, await new OrganizationInventoryTransferDestinationPort(
            other).CheckAsync(Company, Location, "Company", Company));
    }

    [Fact]
    public async Task Empty_identities_are_not_found_without_a_read()
    {
        var source = new Destinations(new OrganizationTransferDestinationV1(Location, true, false, null, 1));
        var port = new OrganizationInventoryTransferDestinationPort(source);

        Assert.Equal(InventoryTransferDestinationStatusV1.NotFound,
            await port.CheckAsync(Guid.Empty, Location, "Company", Company));
        Assert.Equal(InventoryTransferDestinationStatusV1.NotFound,
            await port.CheckAsync(Company, Guid.Empty, "Company", Company));
        Assert.Empty(source.Reads);
    }

    [Fact]
    public async Task A_passive_location_is_inactive_before_its_custody_is_judged()
    {
        var port = new OrganizationInventoryTransferDestinationPort(
            new Destinations(new OrganizationTransferDestinationV1(Location, false, true, null, 1)));

        Assert.Equal(InventoryTransferDestinationStatusV1.Inactive,
            await port.CheckAsync(Company, Location, "Counterparty", OtherParty));
    }

    [Theory]
    [InlineData("Company", true, InventoryTransferDestinationStatusV1.Active)]
    [InlineData("Company", false, InventoryTransferDestinationStatusV1.ContextMismatch)]
    [InlineData("Counterparty", true, InventoryTransferDestinationStatusV1.ContextMismatch)]
    [InlineData("company", true, InventoryTransferDestinationStatusV1.ContextMismatch)]
    public async Task An_internal_location_is_held_only_by_the_transaction_company(string kind, bool sameCompany,
        InventoryTransferDestinationStatusV1 expected)
    {
        var port = new OrganizationInventoryTransferDestinationPort(
            new Destinations(new OrganizationTransferDestinationV1(Location, true, false, null, 1)));

        Assert.Equal(expected, await port.CheckAsync(Company, Location, kind, sameCompany ? Company : OtherParty));
    }

    [Fact]
    public async Task An_external_location_is_held_only_by_its_operator_party()
    {
        var port = new OrganizationInventoryTransferDestinationPort(
            new Destinations(new OrganizationTransferDestinationV1(Location, true, true, Operator, 1)));

        Assert.Equal(InventoryTransferDestinationStatusV1.Active,
            await port.CheckAsync(Company, Location, "Counterparty", Operator));
        Assert.Equal(InventoryTransferDestinationStatusV1.ContextMismatch,
            await port.CheckAsync(Company, Location, "Counterparty", OtherParty));
        Assert.Equal(InventoryTransferDestinationStatusV1.ContextMismatch,
            await port.CheckAsync(Company, Location, "Company", Company));
        Assert.Equal(InventoryTransferDestinationStatusV1.ContextMismatch,
            await port.CheckAsync(Company, Location, "Counterparty", Guid.Empty));
    }

    [Fact]
    public async Task An_external_location_without_an_operator_is_a_context_mismatch()
    {
        var port = new OrganizationInventoryTransferDestinationPort(
            new Destinations(new OrganizationTransferDestinationV1(Location, true, true, null, 1)));

        Assert.Equal(InventoryTransferDestinationStatusV1.ContextMismatch,
            await port.CheckAsync(Company, Location, "Counterparty", Operator));
    }

    [Fact]
    public async Task An_owner_fault_is_never_read_as_an_active_destination()
    {
        var port = new OrganizationInventoryTransferDestinationPort(
            new Destinations(null, new OrganizationCompanyContextMismatchException()));

        await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(
            () => port.CheckAsync(Company, Location, "Company", Company));
    }

    private sealed class Destinations(OrganizationTransferDestinationV1? answer, Exception? failure = null)
        : IOrganizationTransferDestinationSourceV1
    {
        public List<(Guid, Guid)> Reads { get; } = [];

        public Task<OrganizationTransferDestinationV1?> ReadAsync(Guid companyId, Guid locationId,
            CancellationToken cancellationToken = default)
        {
            Reads.Add((companyId, locationId));
            return failure is not null
                ? Task.FromException<OrganizationTransferDestinationV1?>(failure)
                : Task.FromResult(answer);
        }
    }
}
