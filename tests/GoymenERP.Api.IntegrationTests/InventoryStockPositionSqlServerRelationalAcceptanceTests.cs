using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Customer_owned_company_custodied_stock_position_preserves_exact_parties_without_a_read_effect()
    {
        await using var run = await Run.StartAsync();
        var customerId = Guid.NewGuid();
        var source = await SeedOtherPartyFreeSourceAsync(
            run,
            "SYN-ST01-CUSTOMER-HELD",
            new StockOwner(StockPartyKind.Counterparty, customerId),
            new StockCustodian(StockPartyKind.Company, CompanyA));
        var before = await StockPositionReadEffectCountsAsync(run);

        await using var context = run.Context(CompanyA);
        var result = await StockPositions(context, CompanyA).SearchAsync(
            new("SYN-ST01-CUSTOMER-HELD", 25));

        var item = Assert.Single(result.Items);
        Assert.Equal((source.LabelId, source.ProductId, "Counterparty", customerId, "Company", CompanyA),
            (item.LabelId, item.ProductId, item.OwnerKind, item.OwnerId,
                item.CustodianKind, item.CustodianId));
        Assert.Equal(("100", "100", "0", "0", "100"),
            (item.PhysicalKg, item.FreePhysicalKg, item.BlockedPhysicalKg,
                item.ActiveAllocationKg, item.AvailableKg));
        Assert.Equal(before, await StockPositionReadEffectCountsAsync(run));
    }

    [SqlServerFact]
    public async Task Stock_position_read_reports_physical_allocation_and_available_components_from_real_SQL()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ST01-COMPONENTS", 100m);
        await using (var write = run.Store(CompanyA))
        {
            await write.Store.CreateAsync(AllocationRequest(source, "30"), Metadata(run));
        }

        var before = await AllocationEffectCountsAsync(run);
        await using var context = run.Context(CompanyA);
        var result = await StockPositions(context, CompanyA).SearchAsync(new("SYN-ST01", 25));

        var item = Assert.Single(result.Items);
        Assert.Equal((CompanyA, source.LabelId, source.ProductId, "100", "100", "0", "30", "70"),
            (item.CompanyId, item.LabelId, item.ProductId, item.PhysicalKg, item.FreePhysicalKg,
                item.BlockedPhysicalKg, item.ActiveAllocationKg, item.AvailableKg));
        Assert.Equal(("Company", CompanyA, "Company", CompanyA),
            (item.OwnerKind, item.OwnerId, item.CustodianKind, item.CustodianId));
        Assert.False(result.SearchWindowFull);
        Assert.Equal(before, await AllocationEffectCountsAsync(run));
    }

    [SqlServerFact]
    public async Task Stock_position_search_is_bounded_and_never_leaks_another_companys_rows()
    {
        await using var run = await Run.StartAsync();
        var first = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ST01-A", 10m);
        var second = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ST01-B", 20m);
        await SeedFreePhysicalSourceAsync(run, CompanyB, "SYN-ST01-FOREIGN", 30m);

        await using var companyA = run.Context(CompanyA);
        var firstPage = await StockPositions(companyA, CompanyA).SearchAsync(new("SYN-ST01", 1));
        Assert.True(firstPage.SearchWindowFull);
        Assert.Equal(first.LabelId, Assert.Single(firstPage.Items).LabelId);
        Assert.NotNull(firstPage.NextCursor);

        var secondPage = await StockPositions(companyA, CompanyA).SearchAsync(
            new("SYN-ST01", 1, firstPage.NextCursor));
        Assert.False(secondPage.SearchWindowFull);
        Assert.Null(secondPage.NextCursor);
        Assert.Equal(second.LabelId, Assert.Single(secondPage.Items).LabelId);

        var all = await StockPositions(companyA, CompanyA).SearchAsync(new("SYN-ST01", 100));
        Assert.False(all.SearchWindowFull);
        Assert.Equal([first.LabelId, second.LabelId], all.Items.Select(item => item.LabelId));
        Assert.All(all.Items, item => Assert.Equal(CompanyA, item.CompanyId));
        Assert.DoesNotContain(all.Items, item => item.LabelCode.Contains("FOREIGN", StringComparison.Ordinal));
    }

    [SqlServerFact]
    public async Task Typed_product_filter_scans_real_SQL_before_public_pagination_and_resumes_from_the_match()
    {
        await using var run = await Run.StartAsync();
        var familyId = Guid.NewGuid();
        await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ST01-TYPED-A", 10m);
        var firstMatch = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ST01-TYPED-B", 20m);
        var secondMatch = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ST01-TYPED-C", 30m);
        var productFilter = new ProductSelectionFilter(
            CompanyA, familyId, firstMatch.ProductId, secondMatch.ProductId);

        await using var context = run.Context(CompanyA);
        var positions = StockPositions(context, CompanyA, productFilter);
        var firstPage = await positions.SearchAsync(new(
            Q: "SYN-ST01-TYPED", Limit: 1, ProductFamilyId: familyId));

        Assert.Equal(firstMatch.LabelId, Assert.Single(firstPage.Items).LabelId);
        Assert.True(firstPage.SearchWindowFull);
        Assert.NotNull(firstPage.NextCursor);

        var secondPage = await positions.SearchAsync(new(
            Q: "SYN-ST01-TYPED", Limit: 1, Cursor: firstPage.NextCursor, ProductFamilyId: familyId));

        Assert.Equal(secondMatch.LabelId, Assert.Single(secondPage.Items).LabelId);
        Assert.False(secondPage.SearchWindowFull);
        Assert.Null(secondPage.NextCursor);
    }

    [SqlServerFact]
    public async Task Stock_position_filters_and_classification_are_applied_by_real_SQL_without_losing_provenance()
    {
        await using var run = await Run.StartAsync();
        var selected = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ST01-FILTER-A", 10m);
        await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ST01-FILTER-B", 20m);
        await ExecuteAsync(run,
            "UPDATE inventory.Labels SET Classification = 2, Version = Version + 1 WHERE CompanyId = @company AND Id = @label",
            ("@company", CompanyA), ("@label", selected.LabelId));

        await using var context = run.Context(CompanyA);
        var baseline = Assert.Single((await StockPositions(context, CompanyA).SearchAsync(
            new("SYN-ST01-FILTER-A", 25))).Items);
        Assert.Equal("SecondQuality", baseline.Classification);
        Assert.False(string.IsNullOrWhiteSpace(baseline.ProductVersion));

        var filtered = await StockPositions(context, CompanyA).SearchAsync(new(
            "SYN-ST01-FILTER", 25, null, baseline.ProductId, baseline.LocationId,
            InventoryLabelClassificationV1.SecondQuality, baseline.OwnerKind, baseline.OwnerId));
        Assert.Equal(selected.LabelId, Assert.Single(filtered.Items).LabelId);

        var excluded = await StockPositions(context, CompanyA).SearchAsync(new(
            "SYN-ST01-FILTER", 25, null, Guid.NewGuid(), baseline.LocationId,
            InventoryLabelClassificationV1.SecondQuality, baseline.OwnerKind, baseline.OwnerId));
        Assert.Empty(excluded.Items);
    }

    [SqlServerFact]
    public async Task Stock_position_read_fails_closed_when_active_allocations_exceed_the_stored_free_bucket()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-ST01-CORRUPT", 100m);
        await using (var write = run.Store(CompanyA))
        {
            await write.Store.CreateAsync(AllocationRequest(source, "80"), Metadata(run));
        }

        await ExecuteAsync(run,
            "UPDATE inventory.Allocations SET AllocatedKg = 120, RemainingKg = 120 WHERE LabelId = @label AND Status = 1",
            ("@label", source.LabelId));
        await using var context = run.Context(CompanyA);

        var exception = await Assert.ThrowsAsync<InventoryStockPositionException>(() =>
            StockPositions(context, CompanyA).SearchAsync(new("SYN-ST01-CORRUPT", 25)));

        Assert.Equal((503, InventoryStockPositionCodesV1.Unavailable),
            (exception.StatusCode, exception.Code));
    }

    private static InventoryStockPositionService StockPositions(
        InventoryDbContext context,
        Guid companyId,
        IInventoryStockProductFilterV1? productFilter = null)
    {
        var company = new CompanyContext();
        company.Set(companyId);
        return new InventoryStockPositionService(
            company,
            new StockPositionUser(companyId),
            new StockPositionAuthority(),
            new SqlInventoryStockPositionReader(context),
            new InventoryStockPositionCursorCodec(
                new InventoryStockPositionCursorRuntimeOptions(
                    Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray())),
                TimeProvider.System),
            productFilter);
    }

    private static async Task<(int Balances, decimal BalanceKg, int Movements, int MovementLines,
        int Allocations, int Commands, int Audits)> StockPositionReadEffectCountsAsync(Run run) =>
        (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Balances"),
            await run.ScalarAsync<decimal>(
                "SELECT COALESCE(SUM(QuantityKg), 0) FROM inventory.Balances"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.MovementLines"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents")
        );

    private sealed class StockPositionUser(Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "stock-position-reader";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid requestedCompanyId) => requestedCompanyId == companyId;
    }

    private sealed class StockPositionAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(
                permission is ErpPermissions.InventoryLabelsRead or ErpPermissions.InventoryBalancesRead,
                true,
                new HashSet<string>(),
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                new HashSet<Guid>()));
    }

    private sealed class ProductSelectionFilter(
        Guid companyId,
        Guid familyId,
        params Guid[] matchingProductIds) : IInventoryStockProductFilterV1
    {
        private readonly HashSet<Guid> products = [.. matchingProductIds];

        public Task<IReadOnlySet<Guid>> MatchAsync(
            InventoryStockProductFilterRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(companyId, request.CompanyId);
            Assert.Equal(familyId, request.Filter.ProductFamilyId);
            Assert.False(string.IsNullOrWhiteSpace(request.ActorId));
            IReadOnlySet<Guid> matches = request.Selections
                .Where(selection => products.Contains(selection.ProductId))
                .Select(selection => selection.LabelId)
                .ToHashSet();
            return Task.FromResult(matches);
        }
    }
}
