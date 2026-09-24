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

public sealed class InventoryStockPositionTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Physical_components_and_active_allocations_are_reported_without_double_counting()
    {
        var world = new World();
        world.Reader.Record = Record(freeKg: 10.125m, blockedKg: 2m, allocatedKg: 4.5m);

        var item = Assert.Single((await world.Service().SearchAsync(new())).Items);

        Assert.Equal(("12.125", "10.125", "2", "4.5", "5.625"),
            (item.PhysicalKg, item.FreePhysicalKg, item.BlockedPhysicalKg,
                item.ActiveAllocationKg, item.AvailableKg));
        Assert.Equal(("Company", Company, "Company", Company),
            (item.OwnerKind, item.OwnerId, item.CustodianKind, item.CustodianId));
        Assert.Equal("3", item.LabelVersion);
        Assert.Equal(new InventoryLotV1(item.Lot.Id, "HEAT-1", null), item.Lot);
    }

    [Fact]
    public async Task Customer_owned_stock_keeps_owner_separate_from_company_and_custodian()
    {
        var world = new World();
        world.Reader.Record = Record(customerOwned: true);

        var item = Assert.Single((await world.Service().SearchAsync(new())).Items);

        Assert.Equal("Counterparty", item.OwnerKind);
        Assert.NotEqual(Company, item.OwnerId);
        Assert.Equal(("Company", Company), (item.CustodianKind, item.CustodianId));
    }

    [Theory]
    [InlineData("place")]
    [InlineData("blocked")]
    [InlineData("negative")]
    [InlineData("over")]
    public async Task Corrupt_allocation_components_fail_closed(string corruption)
    {
        var world = new World();
        var record = Record(allocatedKg: corruption == "over" ? 11m : 1m);
        var allocation = Assert.Single(record.ActiveAllocations);
        world.Reader.Record = record with
        {
            ActiveAllocations = [allocation with
            {
                LocationId = corruption == "place" ? Guid.NewGuid() : allocation.LocationId,
                Block = corruption == "blocked" ? "Blocked" : allocation.Block,
                RemainingKg = corruption == "negative" ? -1m : allocation.RemainingKg,
            }],
        };

        await Refused(world.Service().SearchAsync(new()), 503, InventoryStockPositionCodesV1.Unavailable);
    }

    [Fact]
    public async Task Search_is_bounded_and_trimmed_and_empty_stock_is_a_valid_empty_answer()
    {
        var world = new World();
        foreach (var query in new[]
        {
            new InventoryStockPositionQueryV1(null, 0),
            new InventoryStockPositionQueryV1(null, 101),
            new InventoryStockPositionQueryV1(new string('x', 65), 25),
        })
        {
            await Refused(world.Service().SearchAsync(query), 400, InventoryStockPositionCodesV1.InvalidQuery);
        }

        world.Reader.HasMore = true;
        var page = await world.Service().SearchAsync(new(" SYN ", 1));
        Assert.True(page.SearchWindowFull);
        Assert.Equal((Company, "SYN", 1), world.Reader.Query);

        world.Reader.Record = null;
        page = await world.Service().SearchAsync(new());
        Assert.Empty(page.Items);
    }

    [Theory]
    [InlineData("labels")]
    [InlineData("balances")]
    [InlineData("scoped")]
    public async Task Both_company_wide_read_permissions_are_required_before_SQL(string denial)
    {
        var world = new World();
        world.Authority.Denied = denial == "labels"
            ? ErpPermissions.InventoryLabelsRead
            : ErpPermissions.InventoryBalancesRead;
        world.Authority.CompanyWide = denial != "scoped";

        await Refused(world.Service().SearchAsync(new()), 403, InventoryStockPositionCodesV1.Forbidden);

        Assert.Equal(0, world.Reader.Reads);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("company")]
    [InlineData("actor")]
    [InlineData("membership")]
    [InlineData("authentication")]
    public async Task Awaited_SQL_read_cannot_release_facts_after_scope_or_authority_changes(string change)
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

        await Refused(world.Service().SearchAsync(new()), 403, InventoryStockPositionCodesV1.Forbidden);

        Assert.Equal(1, world.Reader.Reads);
    }

    [Fact]
    public async Task Missing_or_failed_provider_is_unavailable_and_late_denial_has_precedence()
    {
        var world = new World();
        await Refused(new InventoryStockPositionService(world.Context, world.User, world.Authority, null)
            .SearchAsync(new()), 503, InventoryStockPositionCodesV1.Unavailable);

        world.Reader.Fail = true;
        await Refused(world.Service().SearchAsync(new()), 503, InventoryStockPositionCodesV1.Unavailable);

        world.Reader.During = () => world.Authority.Denied = ErpPermissions.InventoryLabelsRead;
        await Refused(world.Service().SearchAsync(new()), 403, InventoryStockPositionCodesV1.Forbidden);
    }

    [Fact]
    public async Task Unauthenticated_or_unresolved_scope_never_reads_SQL()
    {
        var world = new World();
        world.Context.Set(Guid.Empty);
        await Refused(world.Service().SearchAsync(new()), 403, InventoryStockPositionCodesV1.Forbidden);
        world.User.IsAuthenticated = false;
        await Refused(world.Service().SearchAsync(new()), 401, InventoryStockPositionCodesV1.Unauthenticated);
        Assert.Equal(0, world.Reader.Reads);
    }

    private static async Task Refused(Task task, int status, string code)
    {
        var error = await Assert.ThrowsAsync<InventoryStockPositionException>(() => task);
        Assert.Equal((status, code), (error.StatusCode, error.Code));
    }

    private static InventoryStockPositionRecord Record(
        decimal freeKg = 10m,
        decimal blockedKg = 0m,
        decimal allocatedKg = 0m,
        bool customerOwned = false)
    {
        var productId = Guid.NewGuid();
        var lot = new MaterialLot(Guid.NewGuid(), Company, productId, "HEAT-1", null);
        var label = new MaterialLabel(Guid.NewGuid(), Company, "SYN-ST-01", productId, lot.Id);
        var owner = new StockOwner(
            customerOwned ? StockPartyKind.Counterparty : StockPartyKind.Company,
            customerOwned ? Guid.NewGuid() : Company);
        var custodian = new StockCustodian(StockPartyKind.Company, Company);
        var locationId = Guid.NewGuid();
        var balances = new List<StockBalance>();
        if (freeKg > 0m)
        {
            balances.Add(new StockBalance(
                new StockPosition(Company, owner, custodian, locationId, label, BlockState.Free),
                Kilograms.Positive(freeKg)));
        }
        if (blockedKg > 0m)
        {
            balances.Add(new StockBalance(
                new StockPosition(Company, owner, custodian, locationId, label, BlockState.Blocked),
                Kilograms.Positive(blockedKg)));
        }

        var allocations = allocatedKg == 0m
            ? Array.Empty<InventoryActiveAllocationRecord>()
            : [new InventoryActiveAllocationRecord(
                owner.Kind.ToString(), owner.Id, custodian.Kind.ToString(), custodian.Id,
                locationId, BlockState.Free.ToString(), allocatedKg)];
        return new InventoryStockPositionRecord(3, label, lot, balances, allocations);
    }

    private sealed class World
    {
        public Context Context { get; } = new();
        public User User { get; } = new();
        public Authority Authority { get; } = new();
        public Reader Reader { get; } = new();
        public InventoryStockPositionService Service() => new(Context, User, Authority, Reader);
    }

    private sealed class Context : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = Company;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = Guid.NewGuid().ToString();
        public bool Access { get; set; } = true;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => Access && AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public string? Denied { get; set; }
        public bool CompanyWide { get; set; } = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(
                permission != Denied,
                CompanyWide,
                new HashSet<string>(),
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                new HashSet<Guid>()));
    }

    private sealed class Reader : IInventoryStockPositionReader
    {
        public InventoryStockPositionRecord? Record { get; set; } = InventoryStockPositionTests.Record();
        public Action? During { get; set; }
        public bool Fail { get; set; }
        public bool HasMore { get; set; }
        public int Reads { get; private set; }
        public (Guid CompanyId, string Q, int Limit) Query { get; private set; }

        public async Task<InventoryStockPositionWindow> SearchAsync(
            Guid companyId,
            string q,
            int limit,
            CancellationToken cancellationToken)
        {
            Reads++;
            Query = (companyId, q, limit);
            await Task.Yield();
            During?.Invoke();
            if (Fail)
            {
                throw new InvalidOperationException("Provider failure");
            }

            return new InventoryStockPositionWindow(Record is null ? [] : [Record], HasMore);
        }
    }
}
