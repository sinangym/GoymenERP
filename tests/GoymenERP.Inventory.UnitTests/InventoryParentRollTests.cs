using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryParentRollTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Own_stock_is_a_draft_reference_even_if_blocked_or_documents_pending(bool blocked, bool pending)
    {
        var world = new World();
        world.Reader.Record = Record(blocked: blocked, pending: pending);
        var result = await world.Service().ValidateAsync(new(world.Reader.Record.Label.Id, "3"));
        Assert.True(result.DraftSelectable);
        Assert.Null(result.DraftRefusalCode);
        Assert.True(result.ProductionUseRequiresRevalidation);
        Assert.Equal("10.125", result.PhysicalKg);
        Assert.Equal(blocked ? "0" : "10.125", result.UnblockedPhysicalKg);
        Assert.Equal(blocked ? "10.125" : "0", result.BlockedPhysicalKg);
        Assert.Equal(pending ? "PendingDocuments" : "Complete", result.DocumentStatus);
        Assert.Null(result.CoilNumber);
        Assert.Null(result.Lot.CoilNumber);
        Assert.Equal(new[] { ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead,
            ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead }, world.Authority.Calls);
    }

    [Fact]
    public async Task Customer_owned_facts_remain_visible_but_cannot_be_selected_without_context()
    {
        var world = new World();
        world.Reader.Record = Record(customer: true);
        var item = Assert.Single((await world.Service().SearchAsync(new())).Items);
        Assert.False(item.DraftSelectable);
        Assert.Equal("Counterparty", Assert.Single(item.Positions).OwnerKind);
        Assert.Equal(InventoryParentRollCodesV1.ContextRequired, item.DraftRefusalCode);
        await Refused(world.Service().ValidateAsync(new(item.LabelId, item.LabelVersion)), 422, item.DraftRefusalCode!);
    }

    [Fact]
    public async Task Empty_label_does_not_invent_owner_location_or_stock()
    {
        var world = new World();
        world.Reader.Record = Record() with { Balances = [] };
        var item = Assert.Single((await world.Service().SearchAsync(new())).Items);
        Assert.Equal("0", item.PhysicalKg);
        Assert.Empty(item.Positions);
        Assert.False(item.DraftSelectable);
        await Refused(world.Service().ValidateAsync(new(item.LabelId, "3")), 422, InventoryParentRollCodesV1.NoPhysicalPosition);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("03")]
    [InlineData(" 3")]
    [InlineData("+3")]
    [InlineData("3e0")]
    [InlineData("9223372036854775808")]
    public async Task Version_is_canonical_positive_Int64_text(string version)
    {
        var world = new World();
        await Refused(world.Service().ValidateAsync(new(world.Reader.Record!.Label.Id, version)), 400,
            InventoryParentRollCodesV1.InvalidVersion);
        Assert.Equal(0, world.Reader.Reads);
    }

    [Fact]
    public async Task Stale_version_has_its_own_code_and_missing_or_foreign_labels_disclose_no_facts()
    {
        var world = new World();
        var id = world.Reader.Record!.Label.Id;
        await Refused(world.Service().ValidateAsync(new(id, "2")), 409, InventoryParentRollCodesV1.VersionConflict);
        world.Reader.Record = null;
        await Refused(world.Service().ValidateAsync(new(id, "3")), 404, InventoryParentRollCodesV1.InvalidReference);
        world.Reader.Record = Record(company: Guid.NewGuid());
        await Refused(world.Service().ValidateAsync(new(id, "3")), 404, InventoryParentRollCodesV1.InvalidReference);
        await Refused(world.Service().SearchAsync(new()), 503, InventoryParentRollCodesV1.Unavailable);
    }

    [Theory]
    [InlineData("labels")]
    [InlineData("balances")]
    [InlineData("scoped")]
    public async Task Both_company_wide_grants_are_required_before_read(string denial)
    {
        var world = new World();
        world.Authority.Denied = denial == "labels" ? ErpPermissions.InventoryLabelsRead : ErpPermissions.InventoryBalancesRead;
        world.Authority.CompanyWide = denial != "scoped";
        await Refused(world.Service().SearchAsync(new()), 403, InventoryParentRollCodesV1.Forbidden);
        Assert.Equal(0, world.Reader.Reads);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("company")]
    [InlineData("actor")]
    [InlineData("membership")]
    [InlineData("authentication")]
    public async Task Awaited_read_cannot_release_facts_after_authority_or_scope_moves(string change)
    {
        var world = new World();
        world.Reader.During = () =>
        {
            switch (change)
            {
                case "permission": world.Authority.Denied = ErpPermissions.InventoryBalancesRead; break;
                case "company": world.Context.Set(Guid.NewGuid()); break;
                case "actor": world.User.UserId = Guid.NewGuid().ToString(); break;
                case "membership": world.User.Access = false; break;
                case "authentication": world.User.IsAuthenticated = false; break;
            }
        };
        await Refused(world.Service().SearchAsync(new()), 403, InventoryParentRollCodesV1.Forbidden);
        Assert.Equal(1, world.Reader.Reads);
    }

    [Fact]
    public async Task Missing_provider_and_reader_failures_are_unavailable_but_late_denial_takes_precedence()
    {
        var world = new World();
        await Refused(new InventoryParentRollService(world.Context, world.User, world.Authority, null)
            .SearchAsync(new()), 503, InventoryParentRollCodesV1.Unavailable);
        world.Reader.Fail = true;
        await Refused(world.Service().SearchAsync(new()), 503, InventoryParentRollCodesV1.Unavailable);
        world.Reader.During = () => world.Authority.Denied = ErpPermissions.InventoryLabelsRead;
        await Refused(world.Service().SearchAsync(new()), 403, InventoryParentRollCodesV1.Forbidden);
    }

    [Fact]
    public async Task Unresolved_company_and_unauthenticated_actor_never_reach_the_reader()
    {
        var world = new World();
        world.Context.Set(Guid.Empty);
        await Refused(world.Service().SearchAsync(new()), 403, InventoryParentRollCodesV1.Forbidden);
        world.User.IsAuthenticated = false;
        await Refused(world.Service().SearchAsync(new()), 401, InventoryParentRollCodesV1.Unauthenticated);
        Assert.Equal(0, world.Reader.Reads);
    }

    [Fact]
    public async Task Concurrent_capture_keeps_retryable_read_conflict_distinct_from_terminal_stale_version()
    {
        var world = new World();
        world.Reader.During = () => throw new InventoryParentRollException(409, InventoryParentRollCodesV1.ReadConflict);
        await Refused(world.Service().ValidateAsync(new(world.Reader.Record!.Label.Id, "3")), 409,
            InventoryParentRollCodesV1.ReadConflict);
        Assert.Equal(4, world.Authority.Calls.Count);
    }

    [Fact]
    public async Task Search_is_bounded_and_window_flag_is_not_an_availability_or_exhaustiveness_claim()
    {
        var world = new World();
        foreach (var limit in new[] { 0, 101 })
            await Refused(world.Service().SearchAsync(new(null, limit)), 400, InventoryParentRollCodesV1.InvalidQuery);
        await Refused(world.Service().SearchAsync(new(new string('x', 65))), 400, InventoryParentRollCodesV1.InvalidQuery);
        world.Reader.HasMore = true;
        var result = await world.Service().SearchAsync(new(" SYN ", 1));
        Assert.True(result.SearchWindowFull);
        Assert.Equal((Company, "SYN", 1), world.Reader.Query);
    }

    private static async Task Refused(Task task, int status, string code)
    {
        var error = await Assert.ThrowsAsync<InventoryParentRollException>(() => task);
        Assert.Equal((status, code), (error.StatusCode, error.Code));
    }

    private static InventoryParentRollRecord Record(bool blocked = false, bool pending = false, bool customer = false, Guid? company = null)
    {
        var companyId = company ?? Company;
        var lot = new MaterialLot(Guid.NewGuid(), companyId, Guid.NewGuid());
        var label = new MaterialLabel(Guid.NewGuid(), companyId, "SYN-R13", lot.ProductId, lot.Id,
            requiredDocuments: pending ? ["MTC"] : []);
        var position = new StockPosition(companyId, new(customer ? StockPartyKind.Counterparty : StockPartyKind.Company,
            customer ? Guid.NewGuid() : companyId), new(StockPartyKind.Company, companyId), Guid.NewGuid(), label,
            blocked ? BlockState.Blocked : BlockState.Free);
        return new(3, label, lot, [new StockBalance(position, Kilograms.Positive(10.125m))]);
    }

    private sealed class World
    {
        public Context Context { get; } = new();
        public User User { get; } = new();
        public Authority Authority { get; } = new();
        public Reader Reader { get; } = new();
        public InventoryParentRollService Service() => new(Context, User, Authority, Reader);
    }
    private sealed class Context : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = Company;
        public void Set(Guid id) => CompanyId = id;
    }
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = Guid.NewGuid().ToString();
        public bool Access { get; set; } = true;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid id) => Access && AllowedCompanyIds.Contains(id);
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public string? Denied { get; set; }
        public bool CompanyWide { get; set; } = true;
        public List<string> Calls { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken ct = default)
        {
            Calls.Add(permission);
            return Task.FromResult(new PermissionScope(permission != Denied, CompanyWide,
                new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }
    private sealed class Reader : IInventoryParentRollReader
    {
        public InventoryParentRollRecord? Record { get; set; } = InventoryParentRollTests.Record();
        public Action? During { get; set; }
        public bool Fail { get; set; }
        public bool HasMore { get; set; }
        public int Reads { get; private set; }
        public (Guid, string, int) Query { get; private set; }
        public async Task<InventoryParentRollRecord?> ReadAsync(Guid company, Guid id, CancellationToken ct)
        {
            Reads++;
            await Task.Yield();
            During?.Invoke();
            if (Fail) throw new InvalidOperationException("Provider failure");
            return Record;
        }
        public async Task<InventoryParentRollWindow> SearchAsync(Guid company, string q, int limit, CancellationToken ct)
        {
            Query = (company, q, limit);
            var record = await ReadAsync(company, Guid.Empty, ct);
            return new(record is null ? [] : [record], HasMore);
        }
    }
}
