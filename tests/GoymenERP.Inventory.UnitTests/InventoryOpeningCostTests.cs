using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// SAT-D304 / SAT-D372 opening-stock cost without a database: the cost text, the fenced submit/decide command (two
/// distinct permissions, maker≠checker replay rule), the closed HTTP codes and the owner registration. The stored flow
/// (replay, pending, self-approval, the ledger opened only by an approval, company fence) is proven by the relational
/// acceptance tests.
/// </summary>
public sealed class InventoryOpeningCostTests
{
    private static readonly Guid OpeningCostId = Guid.Parse("62000000-0000-0000-0000-000000000001");

    [Theory]
    [InlineData("12.5")]
    [InlineData("0.0001")]
    [InlineData("125")]
    [InlineData("99999999999999.9999")]
    public void A_positive_canonical_cost_at_the_internal_scale_is_accepted(string text)
    {
        Assert.True(InventoryStore.TryOpeningCost(text, out var unitCost));
        Assert.Equal(decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture), unitCost);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0.0000")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1,5")]
    [InlineData("1.2.3")]
    [InlineData("1e3")]
    [InlineData(" 1")]
    [InlineData("0.00001")]
    [InlineData("100000000000000")]
    public void A_missing_zero_signed_localized_or_over_scale_cost_is_refused(string? text)
    {
        // SAT-D304 (2): a missing cost is "yok", never zero; zero is not a cost.
        Assert.False(InventoryStore.TryOpeningCost(text, out var unitCost));
        Assert.Equal(0m, unitCost);
    }

    [Theory]
    [InlineData("checker", null)]
    [InlineData("maker", InventoryOpeningCostCodesV1.SelfApproval)]
    [InlineData("third", InventoryOpeningCostCodesV1.AlreadyDecided)]
    public void A_decision_is_replayed_only_to_its_decider(string actor, string? refusal)
    {
        Assert.Equal(refusal, InventoryStore.DecisionReplayRefusal("checker", "maker", actor,
            InventoryOpeningCostCodesV1.SelfApproval, InventoryOpeningCostCodesV1.AlreadyDecided));
    }

    [Fact]
    public async Task Submit_measures_the_submit_permission_through_the_writer_fence_and_derives_the_metadata()
    {
        var world = new World();
        world.Authority.Roles = ["stok.b", "stok.a"];
        var transport = Transport(world.User.UserId!) with
        {
            PermissionCode = InventoryPermissionManifestV1.OpeningCostApprove,
            RoleCodes = ["system.admin"],
        };

        var result = await world.Service().SubmitAsync(SubmitRequest(), transport);

        Assert.Same(world.Writer.Result, result);
        var call = Assert.Single(world.Writer.Calls);
        Assert.Equal(("submit", OpeningCostId, InventoryPermissionManifestV1.OpeningCostSubmit, world.User.UserId!, "Web"),
            (call.Operation, call.OpeningCostId, call.Metadata.PermissionCode, call.Metadata.ActorId, call.Metadata.SourceChannel));
        Assert.Equal(new[] { "stok.a", "stok.b" }, call.Metadata.RoleCodes);
        Assert.Equal(Enumerable.Repeat(InventoryPermissionManifestV1.OpeningCostSubmit, 3), world.Authority.Measured);
    }

    [Fact]
    public async Task Decide_measures_the_approve_permission_never_the_submit_one()
    {
        var world = new World();

        await world.Service().DecideAsync(OpeningCostId, DecideRequest(), Transport(world.User.UserId!));

        var call = Assert.Single(world.Writer.Calls);
        Assert.Equal(("decide", OpeningCostId, InventoryPermissionManifestV1.OpeningCostApprove),
            (call.Operation, call.OpeningCostId, call.Metadata.PermissionCode));
        Assert.Equal(Enumerable.Repeat(InventoryPermissionManifestV1.OpeningCostApprove, 3), world.Authority.Measured);
    }

    [Fact]
    public async Task Holding_only_the_submit_permission_never_decides_and_other_stock_authority_is_not_cost_authority()
    {
        var world = new World();
        world.Authority.GrantedPermissions = [InventoryPermissionManifestV1.OpeningCostSubmit];

        await world.Service().SubmitAsync(SubmitRequest(), Transport(world.User.UserId!));
        await Refused(world.Service().DecideAsync(OpeningCostId, DecideRequest(), Transport(world.User.UserId!)), 403,
            InventoryOpeningCostCodesV1.Forbidden);
        Assert.Equal("submit", Assert.Single(world.Writer.Calls).Operation);

        world = new World();
        world.Authority.GrantedPermissions =
        [
            InventoryPermissionManifestV1.OpeningStockRecord, InventoryPermissionManifestV1.StockScrapApprove,
            InventoryPermissionManifestV1.StockCountApprove,
        ];
        await Refused(world.Service().SubmitAsync(SubmitRequest(), Transport(world.User.UserId!)), 403,
            InventoryOpeningCostCodesV1.Forbidden);
        await Refused(world.Service().DecideAsync(OpeningCostId, DecideRequest(), Transport(world.User.UserId!)), 403,
            InventoryOpeningCostCodesV1.Forbidden);
        Assert.Empty(world.Writer.Calls);
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("site-scoped")]
    [InlineData("other-actor")]
    [InlineData("lost-between")]
    [InlineData("company-changed")]
    public async Task Denied_scoped_foreign_or_lost_authority_never_reaches_the_writer(string fault)
    {
        foreach (var decide in new[] { false, true })
        {
            var world = new World();
            var actor = world.User.UserId!;
            switch (fault)
            {
                case "denied": world.Authority.GrantedPermissions = []; break;
                case "site-scoped": world.Authority.CompanyWide = false; break;
                case "other-actor": actor = "someone-else"; break;
                case "lost-between": world.Authority.AfterFirst = () => world.Authority.GrantedPermissions = []; break;
                case "company-changed": world.Authority.AfterFirst = () => world.Context.Set(CompanyB); break;
            }

            await Refused(decide
                    ? world.Service().DecideAsync(OpeningCostId, DecideRequest(), Transport(actor))
                    : world.Service().SubmitAsync(SubmitRequest(), Transport(actor)),
                403, InventoryOpeningCostCodesV1.Forbidden);
            Assert.Empty(world.Writer.Calls);
        }
    }

    [Theory]
    [InlineData("permission-revoked")]
    [InlineData("actor-changed")]
    [InlineData("company-changed")]
    public async Task Authority_lost_inside_the_writer_fence_is_403_and_no_result_is_returned(string fault)
    {
        foreach (var decide in new[] { false, true })
        {
            var world = new World();
            world.Writer.BeforeFence = fault switch
            {
                "permission-revoked" => () => world.Authority.GrantedPermissions = [],
                "actor-changed" => () => world.User.UserId = Guid.NewGuid().ToString("D"),
                "company-changed" => () => world.Context.Set(CompanyB),
                _ => throw new InvalidOperationException(fault),
            };

            await Refused(decide
                    ? world.Service().DecideAsync(OpeningCostId, DecideRequest(), Transport(world.User.UserId!))
                    : world.Service().SubmitAsync(SubmitRequest(), Transport(world.User.UserId!)),
                403, InventoryOpeningCostCodesV1.Forbidden);
            Assert.Single(world.Writer.Calls);
        }
    }

    [Fact]
    public async Task Cancellation_inside_the_writer_fence_propagates_without_a_result()
    {
        foreach (var decide in new[] { false, true })
        {
            var world = new World();
            using var cancellation = new CancellationTokenSource();
            world.Writer.BeforeFence = cancellation.Cancel;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => decide
                ? world.Service().DecideAsync(OpeningCostId, DecideRequest(), Transport(world.User.UserId!), cancellation.Token)
                : world.Service().SubmitAsync(SubmitRequest(), Transport(world.User.UserId!), cancellation.Token));
            Assert.Single(world.Writer.Calls);
        }
    }

    [Fact]
    public async Task No_identity_is_401_and_an_unregistered_name_is_a_setup_gap()
    {
        var world = new World();
        world.User.IsAuthenticated = false;
        await Refused(world.Service().SubmitAsync(SubmitRequest(), Transport("x")), 401,
            InventoryOpeningCostCodesV1.Unauthenticated);

        world = new World { Known = false };
        await Refused(world.Service().DecideAsync(OpeningCostId, DecideRequest(), Transport(world.User.UserId!)), 503,
            InventoryOpeningCostCodesV1.PermissionUnregistered);
        Assert.Empty(world.Authority.Measured);
        Assert.Empty(world.Writer.Calls);
    }

    [Theory]
    [InlineData(409, InventoryOpeningCostCodesV1.SelfApproval, 409)]
    [InlineData(409, InventoryOpeningCostCodesV1.LedgerAlreadyTracked, 409)]
    [InlineData(409, InventoryOpeningCostCodesV1.NoCostedStock, 409)]
    [InlineData(409, InventoryOpeningCostCodesV1.AlreadyDecided, 409)]
    [InlineData(404, InventoryOpeningCostCodesV1.NotFound, 404)]
    [InlineData(400, InventoryOpeningCostCodesV1.ValidationFailed, 400)]
    [InlineData(400, InventoryOpeningCostCodesV1.Unavailable, 503)]
    [InlineData(503, InventoryOpeningCostCodesV1.Unavailable, 503)]
    public async Task Writer_refusals_pass_through_except_setup_states(int status, string code, int answered)
    {
        var world = new World();
        world.Writer.Failure = new InventoryOpeningCostException(status, code);

        await Refused(world.Service().DecideAsync(OpeningCostId, DecideRequest(), Transport(world.User.UserId!)), answered, code);
    }

    [Fact]
    public void Http_contract_is_pinned_and_each_routes_codes_are_disjoint_by_status()
    {
        Assert.Equal("/api/v1/inventory/opening-costs", InventoryOpeningCostApiV1.Path);
        Assert.Equal("/api/v1/inventory/opening-costs/{openingCostId}/decision", InventoryOpeningCostApiV1.DecisionPathTemplate);
        Assert.Equal(("SubmitInventoryOpeningCostV1", "DecideInventoryOpeningCostV1"),
            (InventoryOpeningCostApiV1.SubmitOperationId, InventoryOpeningCostApiV1.DecideOperationId));
        foreach (var route in new[]
                 {
                     new[] { InventoryOpeningCostHttpCodesV1.Status400Codes, InventoryOpeningCostHttpCodesV1.SubmitStatus409Codes,
                         InventoryOpeningCostHttpCodesV1.Status503Codes },
                     new[] { InventoryOpeningCostHttpCodesV1.Status400Codes, InventoryOpeningCostHttpCodesV1.DecideStatus404Codes,
                         InventoryOpeningCostHttpCodesV1.DecideStatus409Codes, InventoryOpeningCostHttpCodesV1.Status503Codes },
                 })
        {
            var all = route.SelectMany(set => set).ToArray();
            Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
        }

        Assert.Contains(InventoryOpeningCostCodesV1.SelfApproval, InventoryOpeningCostHttpCodesV1.DecideStatus409Codes);
        Assert.DoesNotContain(InventoryOpeningCostCodesV1.SelfApproval, InventoryOpeningCostHttpCodesV1.SubmitStatus409Codes);
        Assert.Contains(InventoryOpeningCostCodesV1.PendingExists, InventoryOpeningCostHttpCodesV1.SubmitStatus409Codes);
        Assert.Contains(InventoryOpeningCostCodesV1.LedgerAlreadyTracked, InventoryOpeningCostHttpCodesV1.SubmitStatus409Codes);
        Assert.Contains(InventoryOpeningCostCodesV1.LedgerAlreadyTracked, InventoryOpeningCostHttpCodesV1.DecideStatus409Codes);
    }

    [Fact]
    public void Permission_names_are_the_owners_own_registered_distinct_and_not_critical()
    {
        Assert.Equal(("inventory.opening-cost.submit", "inventory.opening-cost.approve"),
            (InventoryPermissionManifestV1.OpeningCostSubmit, InventoryPermissionManifestV1.OpeningCostApprove));
        Assert.Equal((ErpPermissions.InventoryOpeningCostSubmit, ErpPermissions.InventoryOpeningCostApprove),
            (InventoryPermissionManifestV1.OpeningCostSubmit, InventoryPermissionManifestV1.OpeningCostApprove));
        Assert.True(ErpPermissions.IsKnown(InventoryPermissionManifestV1.OpeningCostSubmit));
        Assert.True(ErpPermissions.IsKnown(InventoryPermissionManifestV1.OpeningCostApprove));
        var names = new[]
        {
            InventoryPermissionManifestV1.OpeningCostSubmit, InventoryPermissionManifestV1.OpeningCostApprove,
            InventoryPermissionManifestV1.OpeningStockRecord, InventoryPermissionManifestV1.StockScrapRecord,
            InventoryPermissionManifestV1.StockScrapApprove, InventoryPermissionManifestV1.StockCountRecord,
            InventoryPermissionManifestV1.StockCountApprove,
        };
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Requests_refuse_unknown_or_missing_members_and_the_decision_travels_as_its_name()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var decision = JsonSerializer.Deserialize<DecideInventoryOpeningCostV1>("""{"decision":"Reject","reason":"Fatura yok"}""", web);
        Assert.Equal(InventoryOpeningCostDecisionKindV1.Reject, decision!.Decision);
        Assert.Contains("\"decision\":\"Approve\"", JsonSerializer.Serialize(DecideRequest(), web), StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SubmitInventoryOpeningCostV1>(
            """{"openingCostId":"62000000-0000-0000-0000-000000000001","locationId":"10000000-0000-0000-0000-000000000001","productId":"20000000-0000-0000-0000-000000000001","unitCostPerKg":"12.5","reason":"x","currency":"EUR"}""",
            web));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SubmitInventoryOpeningCostV1>(
            """{"openingCostId":"62000000-0000-0000-0000-000000000001","locationId":"10000000-0000-0000-0000-000000000001","productId":"20000000-0000-0000-0000-000000000001","reason":"x"}""",
            web));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DecideInventoryOpeningCostV1>("""{"decision":"Approve"}""", web));
    }

    [Fact]
    public void Owner_registration_adds_the_writer_and_the_fenced_command()
    {
        var services = new ServiceCollection().AddInventoryOpeningCosts();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryOpeningCostCommandV1)
            && descriptor.ImplementationType == typeof(InventoryOpeningCostCommandService)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryOpeningCostWriterV1)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    private static SubmitInventoryOpeningCostV1 SubmitRequest() =>
        new(OpeningCostId, Warehouse1, BandProduct, "12.5", "Devir maliyeti");

    private static DecideInventoryOpeningCostV1 DecideRequest() => new(InventoryOpeningCostDecisionKindV1.Approve, "Uygun");

    private static InventoryCommandMetadata Transport(string actorId) =>
        new(actorId, string.Empty, [], "corr-1", null, "key-1", "Web");

    private static InventoryOpeningCostViewV1 View() => new(
        OpeningCostId, CompanyA, Warehouse1, BandProduct, "12.5", "Devir maliyeti", At,
        InventoryOpeningCostStatusesV1.PendingApproval, null);

    private static async Task Refused(Task task, int status, string code)
    {
        var error = await Assert.ThrowsAsync<InventoryOpeningCostException>(() => task);
        Assert.Equal((status, code), (error.Status, error.Code));
    }

    private sealed class World
    {
        public Context Context { get; } = new();
        public User User { get; } = new();
        public Authority Authority { get; } = new();
        public Writer Writer { get; } = new();
        public bool Known { get; init; } = true;

        public InventoryOpeningCostCommandService Service() =>
            new(Context, User, Authority, Writer, permission => Known
                && permission is InventoryPermissionManifestV1.OpeningCostSubmit or InventoryPermissionManifestV1.OpeningCostApprove);
    }

    private sealed class Context : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = CompanyA;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = Guid.NewGuid().ToString();
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA, CompanyB };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public string[] GrantedPermissions { get; set; } =
            [InventoryPermissionManifestV1.OpeningCostSubmit, InventoryPermissionManifestV1.OpeningCostApprove];

        public bool CompanyWide { get; set; } = true;
        public string[] Roles { get; set; } = [];
        public Action? AfterFirst { get; set; }
        public List<string> Measured { get; } = [];

        public async Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Measured.Add(permission);
            await Task.Yield();
            var answer = new PermissionScope(GrantedPermissions.Contains(permission), CompanyWide,
                Roles.ToHashSet(StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
            if (Measured.Count == 1)
            {
                AfterFirst?.Invoke();
            }

            return answer;
        }
    }

    private sealed class Writer : IInventoryOpeningCostWriterV1
    {
        public List<(string Operation, Guid OpeningCostId, InventoryCommandMetadata Metadata)> Calls { get; } = [];
        public InventoryOpeningCostException? Failure { get; set; }
        public InventoryOpeningCostResultV1 Result { get; } = new(View(), false, "corr-1");
        public Action? BeforeFence { get; set; }

        public async Task<InventoryOpeningCostResultV1> SubmitOpeningCostAsync(
            SubmitInventoryOpeningCostV1 request,
            InventoryCommandMetadata metadata,
            Func<CancellationToken, Task> freshAuthorityFence,
            CancellationToken cancellationToken = default) =>
            await Answer("submit", request.OpeningCostId, metadata, freshAuthorityFence, cancellationToken);

        public async Task<InventoryOpeningCostResultV1> DecideOpeningCostAsync(
            Guid openingCostId,
            DecideInventoryOpeningCostV1 request,
            InventoryCommandMetadata metadata,
            Func<CancellationToken, Task> freshAuthorityFence,
            CancellationToken cancellationToken = default) =>
            await Answer("decide", openingCostId, metadata, freshAuthorityFence, cancellationToken);

        private async Task<InventoryOpeningCostResultV1> Answer(
            string operation,
            Guid openingCostId,
            InventoryCommandMetadata metadata,
            Func<CancellationToken, Task> freshAuthorityFence,
            CancellationToken cancellationToken)
        {
            Calls.Add((operation, openingCostId, metadata));
            BeforeFence?.Invoke();
            await freshAuthorityFence(cancellationToken);
            return Failure is null ? Result : throw Failure;
        }
    }
}
