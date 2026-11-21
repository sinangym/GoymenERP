using System.Net;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Errors;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// B-22c HTTP boundary of the transfer receipt (KLN-D21/KLN-D23/KLN-D24). The owner command and query are doubles: this
/// proves only the host contract (one canonical Idempotency-Key, strict JSON, 201/200 replay headers, each command under
/// its own permission, the closed 400/409/422 code set per operation, code-less 401/403/404/503).
/// </summary>
public sealed class InventoryTransferReceiptHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid TransferId = Guid.Parse("7b000000-0000-4000-8000-000000000001");
    private static readonly Guid LineId = Guid.Parse("7b000000-0000-4000-8000-000000000002");
    private static readonly Guid LabelId = Guid.Parse("7b000000-0000-4000-8000-000000000003");
    private static readonly Guid SourceLocation = Guid.Parse("7b000000-0000-4000-8000-000000000004");
    private static readonly Guid DestinationLocation = Guid.Parse("7b000000-0000-4000-8000-000000000005");
    private static readonly Guid IncomingDocument = Guid.Parse("7b000000-0000-4000-8000-000000000006");
    private static readonly DateTimeOffset At = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly string[] AllReceiptGrants =
    [
        ErpPermissions.InventoryStockTransferWrite,
        ErpPermissions.InventoryStockTransferOverageApprove,
        ErpPermissions.InventoryStockTransferLossRecord,
        ErpPermissions.InventoryStockTransferLossApprove,
        ErpPermissions.InventoryBalancesRead,
    ];

    [Fact]
    public async Task Receive_answers_201_with_the_transfer_and_facts_and_forwards_the_key_under_the_write_permission()
    {
        var command = new StubCommand();
        await using var host = CreateHost(command);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(ReceivePath(), ReceiveBody("9.5"), "receive-key-1"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        var call = Assert.Single(command.Calls);
        Assert.Equal(("receive", TransferId, LineId, "receive-key-1", ErpPermissions.InventoryStockTransferWrite),
            (call.Operation, call.TransferId, call.LineId, call.Transport.IdempotencyKey, call.Transport.PermissionCode));
        Assert.Equal(("2", "9.5", IncomingDocument), (command.LastReceive!.ExpectedTransferVersion,
            command.LastReceive.ReceivedKg, command.LastReceive.IncomingDocument.Id));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(TransferId, document.RootElement.GetProperty("transfer").GetProperty("transferId").GetGuid());
        Assert.Equal("Accepted", document.RootElement.GetProperty("facts").GetProperty("status").GetString());
        Assert.Equal("0.5", document.RootElement.GetProperty("facts").GetProperty("receipts")[0]
            .GetProperty("shortageKg").GetString());
    }

    [Fact]
    public async Task Receive_replay_answers_200_with_the_original_correlation()
    {
        var command = new StubCommand { Replay = true };
        await using var host = CreateHost(command);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(ReceivePath(), ReceiveBody("9.5"), "receive-key-1"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal("original-correlation",
            Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
    }

    [Theory]
    [InlineData("overage", ErpPermissions.InventoryStockTransferOverageApprove)]
    [InlineData("loss-request", ErpPermissions.InventoryStockTransferLossRecord)]
    [InlineData("loss-decision", ErpPermissions.InventoryStockTransferLossApprove)]
    [InlineData("overage-renewal", ErpPermissions.InventoryStockTransferWrite)]
    [InlineData("loss-withdrawal", ErpPermissions.InventoryStockTransferLossRecord)]
    public async Task Each_decision_route_forwards_the_key_under_its_own_permission(string operation, string permission)
    {
        var command = new StubCommand();
        await using var host = CreateHost(command, granted: [permission, ErpPermissions.InventoryBalancesRead]);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(PathOf(operation), BodyOf(operation), $"{operation}-key"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var call = Assert.Single(command.Calls);
        Assert.Equal((operation, TransferId, LineId, $"{operation}-key", permission),
            (call.Operation, call.TransferId, call.LineId, call.Transport.IdempotencyKey, call.Transport.PermissionCode));
    }

    [Fact]
    public async Task The_overage_approval_alone_does_not_open_the_overage_renewal_route()
    {
        var command = new StubCommand();
        await using var host = CreateHost(command,
            granted: [ErpPermissions.InventoryStockTransferOverageApprove, ErpPermissions.InventoryBalancesRead]);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(
            Post(PathOf("overage-renewal"), BodyOf("overage-renewal"), "overage-renewal-key"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(command.Calls);
    }

    [Theory]
    [InlineData("overage")]
    [InlineData("loss-request")]
    [InlineData("loss-decision")]
    [InlineData("loss-withdrawal")]
    public async Task The_transfer_write_permission_alone_does_not_open_a_decision_route(string operation)
    {
        var command = new StubCommand();
        await using var host = CreateHost(command,
            granted: [ErpPermissions.InventoryStockTransferWrite, ErpPermissions.InventoryBalancesRead]);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(PathOf(operation), BodyOf(operation), $"{operation}-key"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(command.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("a,b")]
    public async Task A_missing_or_non_canonical_idempotency_key_is_400_before_the_owner(string? key)
    {
        var command = new StubCommand();
        await using var host = CreateHost(command);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(ReceivePath(), ReceiveBody("9.5"), key));

        await AssertCodedProblem(response, HttpStatusCode.BadRequest, InventoryTransferCodesV1.ValidationFailed);
        Assert.Empty(command.Calls);
    }

    [Theory]
    // An unknown member: the client may not state the shortage; it is derived from the stored dispatch.
    [InlineData("{\"expectedTransferVersion\":\"2\",\"receivedAtUtc\":\"2026-11-15T11:00:00Z\","
        + "\"incomingDocument\":{\"kind\":\"inventory.transfer.incoming\",\"id\":\"7b000000-0000-4000-8000-000000000006\","
        + "\"lineId\":null},\"receivedKg\":\"9.5\",\"overageReason\":null,\"shortageKg\":\"0.5\"}")]
    // A missing required member.
    [InlineData("{\"expectedTransferVersion\":\"2\",\"receivedAtUtc\":\"2026-11-15T11:00:00Z\","
        + "\"incomingDocument\":{\"kind\":\"inventory.transfer.incoming\",\"id\":\"7b000000-0000-4000-8000-000000000006\","
        + "\"lineId\":null}}")]
    [InlineData("null")]
    [InlineData("{")]
    public async Task A_non_strict_receive_body_is_400_before_the_owner(string body)
    {
        var command = new StubCommand();
        await using var host = CreateHost(command);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(ReceivePath(), body, "strict-key"));

        await AssertCodedProblem(response, HttpStatusCode.BadRequest, InventoryTransferCodesV1.ValidationFailed);
        Assert.Empty(command.Calls);
    }

    [Fact]
    public async Task A_non_json_receive_body_is_415_before_the_owner()
    {
        var command = new StubCommand();
        await using var host = CreateHost(command);
        using var client = CreateClient(host);
        using var request = new HttpRequestMessage(HttpMethod.Post, ReceivePath())
        {
            Content = new StringContent(ReceiveBody("9.5"), Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("Idempotency-Key", "media-key");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(command.Calls);
    }

    [Theory]
    [InlineData("receive", 422, InventoryTransferReceiptCodesV1.OverageReasonRequired)]
    [InlineData("receive", 422, InventoryTransferReceiptCodesV1.OverageReasonNotAllowed)]
    [InlineData("receive", 409, InventoryTransferReceiptCodesV1.MultiBlockFullReceiptOnly)]
    [InlineData("receive", 409, InventoryTransferReceiptCodesV1.LossPending)]
    [InlineData("overage", 409, InventoryTransferReceiptCodesV1.SelfApproval)]
    [InlineData("overage", 409, InventoryTransferReceiptCodesV1.OverageNotPending)]
    [InlineData("loss-request", 409, InventoryTransferReceiptCodesV1.LossAlreadyRequested)]
    [InlineData("loss-decision", 409, InventoryTransferReceiptCodesV1.SelfApproval)]
    [InlineData("loss-decision", 409, InventoryTransferReceiptCodesV1.AlreadyDecided)]
    [InlineData("loss-decision", 409, InventoryTransferReceiptCodesV1.LossWithdrawn)]
    [InlineData("overage-renewal", 409, InventoryTransferReceiptCodesV1.OverageAlreadyRequested)]
    [InlineData("overage-renewal", 422, InventoryTransferReceiptCodesV1.OverageRenewalExceedsDeclared)]
    [InlineData("loss-withdrawal", 409, InventoryTransferReceiptCodesV1.AlreadyDecided)]
    [InlineData("loss-withdrawal", 409, InventoryTransferReceiptCodesV1.LossWithdrawn)]
    public async Task An_owner_refusal_in_the_operation_code_set_keeps_its_code(string operation, int status, string code)
    {
        var command = new StubCommand { Failure = new InventoryTransferException(status, code) };
        await using var host = CreateHost(command);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(PathOf(operation), BodyOf(operation), $"{operation}-key"));

        await AssertCodedProblem(response, (HttpStatusCode)status, code);
    }

    [Theory]
    [InlineData("receive", 409, InventoryTransferReceiptCodesV1.SelfApproval)]
    [InlineData("overage", 422, InventoryTransferReceiptCodesV1.OverageReasonRequired)]
    [InlineData("loss-request", 409, InventoryTransferReceiptCodesV1.SelfApproval)]
    [InlineData("overage-renewal", 409, InventoryTransferReceiptCodesV1.SelfApproval)]
    [InlineData("loss-withdrawal", 422, InventoryTransferReceiptCodesV1.OverageRenewalExceedsDeclared)]
    public async Task An_owner_code_outside_the_operation_code_set_is_a_code_less_503(
        string operation,
        int status,
        string code)
    {
        var command = new StubCommand { Failure = new InventoryTransferException(status, code) };
        await using var host = CreateHost(command);
        using var client = CreateClient(host);

        using var response = await client.SendAsync(Post(PathOf(operation), BodyOf(operation), $"{operation}-key"));

        await AssertPlatformProblem(response, HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(503)]
    public async Task Receipt_facts_refusals_are_code_less_platform_problems(int status)
    {
        var query = new StubQuery { Failure = new InventoryTransferException(status, InventoryTransferCodesV1.Forbidden) };
        await using var host = CreateHost(new StubCommand(), query);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(FactsPath());

        await AssertPlatformProblem(response, (HttpStatusCode)status);
    }

    [Fact]
    public async Task Receipt_facts_answer_200_under_the_balances_read_permission()
    {
        var query = new StubQuery();
        await using var host = CreateHost(new StubCommand(), query, [ErpPermissions.InventoryBalancesRead]);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(FactsPath());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
        Assert.Equal(TransferId, Assert.Single(query.Reads));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(TransferId, document.RootElement.GetProperty("transferId").GetGuid());
    }

    /// <summary>
    /// The edge policy (RequireAuthorization) is answered by a test-only authority: registration grants nothing in the
    /// product, so the host contract is exercised with the permissions granted here and nowhere else.
    /// </summary>
    private WebApplicationFactory<Program> CreateHost(StubCommand command, StubQuery? query = null,
        string[]? granted = null)
    {
        query ??= new StubQuery();
        var authority = new GrantingAuthority(granted ?? AllReceiptGrants);
        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IInventoryTransferReceiptCommandV1>();
            services.AddSingleton<IInventoryTransferReceiptCommandV1>(command);
            services.RemoveAll<IInventoryTransferReceiptQueryV1>();
            services.AddSingleton<IInventoryTransferReceiptQueryV1>(query);
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
            Task.FromResult(new AuthorizationSnapshot("b22c-http-only", new HashSet<Guid> { TestCompanyIds.CompanyA },
                permissions.ToDictionary(permission => permission, permission => Scope(permission, companyId),
                    StringComparer.Ordinal)));

        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(Scope(permission, companyId));

        private bool Allowed(string permission, Guid companyId) =>
            companyId == TestCompanyIds.CompanyA && granted.Contains(permission, StringComparer.Ordinal);

        private PermissionScope Scope(string permission, Guid companyId) =>
            new(Allowed(permission, companyId), true, new HashSet<string>(StringComparer.Ordinal) { "B22C-HTTP" },
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

    private static string ReceivePath() => $"{InventoryTransferApiV1.Path}/{TransferId:D}/receive-lines/{LineId:D}";

    private static string FactsPath() => $"{InventoryTransferApiV1.Path}/{TransferId:D}/receipt-facts";

    private static string PathOf(string operation) => operation switch
    {
        "receive" => ReceivePath(),
        "overage" => $"{ReceivePath()}/overage-decision",
        "loss-request" => $"{InventoryTransferApiV1.Path}/{TransferId:D}/lines/{LineId:D}/loss-request",
        "loss-decision" => $"{InventoryTransferApiV1.Path}/{TransferId:D}/lines/{LineId:D}/loss-decision",
        "overage-renewal" => $"{ReceivePath()}/overage-renewal",
        "loss-withdrawal" => $"{InventoryTransferApiV1.Path}/{TransferId:D}/lines/{LineId:D}/loss-withdrawal",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };

    private static string BodyOf(string operation) => operation switch
    {
        "receive" => ReceiveBody("9.5"),
        "overage" => JsonSerializer.Serialize(new DecideInventoryTransferOverageV1(
            InventoryTransferDecisionKindV1.Approve, At.AddHours(3), "SENTETIK tartı onayı"), Web),
        "loss-request" => JsonSerializer.Serialize(
            new RequestInventoryTransferLossV1("2", At.AddHours(3), "SENTETIK yolda kayıp"), Web),
        "loss-decision" => JsonSerializer.Serialize(new DecideInventoryTransferLossV1("3",
            InventoryTransferDecisionKindV1.Reject, At.AddHours(4), "SENTETIK kayıp reddi"), Web),
        "overage-renewal" => JsonSerializer.Serialize(new RenewInventoryTransferOverageV1("3", At.AddHours(5), "0.4",
            "SENTETIK düzeltilmiş fazla talebi"), Web),
        "loss-withdrawal" => JsonSerializer.Serialize(new WithdrawInventoryTransferLossV1("3", 1, At.AddHours(5),
            "SENTETIK talep geri çekildi"), Web),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };

    private static string ReceiveBody(string receivedKg) => JsonSerializer.Serialize(
        new ReceiveInventoryTransferLineV1("2", At.AddHours(2),
            new InventoryTransferDocumentReferenceV1("inventory.transfer.incoming", IncomingDocument, null),
            receivedKg, null), Web);

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

    private static InventoryTransferViewV1 View() => new(TransferId, TestCompanyIds.CompanyA, "3", "Accepted",
        SourceLocation, "Company", TestCompanyIds.CompanyA, DestinationLocation, "Company", TestCompanyIds.CompanyA, At,
        At.AddHours(1), [new InventoryTransferLineViewV1(LineId, LabelId, "1", "10", "10", "0", "Accepted")]);

    private static InventoryTransferReceiptFactsViewV1 Facts() => new(TransferId, TestCompanyIds.CompanyA, "3",
        "Accepted",
        [
            new InventoryTransferReceiptViewV1(LineId, "10", "9.5", "0.5", "0", Guid.NewGuid(), null, At.AddHours(2),
                "receiver", "None", null, null, null, null),
        ],
        [],
        []);

    private sealed class StubCommand : IInventoryTransferReceiptCommandV1
    {
        public bool Replay { get; init; }
        public InventoryTransferException? Failure { get; init; }
        public List<(string Operation, Guid TransferId, Guid LineId, InventoryCommandMetadata Transport)> Calls { get; } = [];
        public ReceiveInventoryTransferLineV1? LastReceive { get; private set; }

        public Task<InventoryTransferReceiptResultV1> ReceiveLineAsync(Guid transferId, Guid lineId,
            ReceiveInventoryTransferLineV1 request, InventoryCommandMetadata transport,
            CancellationToken cancellationToken = default)
        {
            LastReceive = request;
            return Answer("receive", transferId, lineId, transport);
        }

        public Task<InventoryTransferReceiptResultV1> DecideOverageAsync(Guid transferId, Guid lineId,
            DecideInventoryTransferOverageV1 request, InventoryCommandMetadata transport,
            CancellationToken cancellationToken = default) => Answer("overage", transferId, lineId, transport);

        public Task<InventoryTransferReceiptResultV1> RequestLossAsync(Guid transferId, Guid lineId,
            RequestInventoryTransferLossV1 request, InventoryCommandMetadata transport,
            CancellationToken cancellationToken = default) => Answer("loss-request", transferId, lineId, transport);

        public Task<InventoryTransferReceiptResultV1> DecideLossAsync(Guid transferId, Guid lineId,
            DecideInventoryTransferLossV1 request, InventoryCommandMetadata transport,
            CancellationToken cancellationToken = default) => Answer("loss-decision", transferId, lineId, transport);

        public Task<InventoryTransferReceiptResultV1> RenewOverageAsync(Guid transferId, Guid lineId,
            RenewInventoryTransferOverageV1 request, InventoryCommandMetadata transport,
            CancellationToken cancellationToken = default) => Answer("overage-renewal", transferId, lineId, transport);

        public Task<InventoryTransferReceiptResultV1> WithdrawLossAsync(Guid transferId, Guid lineId,
            WithdrawInventoryTransferLossV1 request, InventoryCommandMetadata transport,
            CancellationToken cancellationToken = default) => Answer("loss-withdrawal", transferId, lineId, transport);

        private Task<InventoryTransferReceiptResultV1> Answer(string operation, Guid transferId, Guid lineId,
            InventoryCommandMetadata transport)
        {
            Calls.Add((operation, transferId, lineId, transport));
            return Failure is not null
                ? Task.FromException<InventoryTransferReceiptResultV1>(Failure)
                : Task.FromResult(new InventoryTransferReceiptResultV1(View(), Facts(), Replay,
                    Replay ? "original-correlation" : "current"));
        }
    }

    private sealed class StubQuery : IInventoryTransferReceiptQueryV1
    {
        public InventoryTransferException? Failure { get; init; }
        public List<Guid> Reads { get; } = [];

        public Task<InventoryTransferReceiptFactsViewV1> GetReceiptFactsAsync(Guid transferId,
            CancellationToken cancellationToken = default)
        {
            Reads.Add(transferId);
            return Failure is not null
                ? Task.FromException<InventoryTransferReceiptFactsViewV1>(Failure)
                : Task.FromResult(Facts());
        }
    }
}
