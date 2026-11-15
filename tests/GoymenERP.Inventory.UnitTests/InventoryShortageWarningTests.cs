using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// KLN-D42 (STK-S31), without a database: the Sales demand source over fake Sales ports, the per-position free unreserved rule,
/// and the warning read's authority, fail-closed sources, kilogram-only summing and ordering over fakes. The SQL reads of the
/// stock reader are not exercised here. SENTETIK / NOT_ENV.
/// </summary>
public sealed class InventoryShortageWarningTests
{
    private static readonly Guid Company = Guid.Parse("00000000-0000-4000-8000-000000000101");
    private static readonly Guid OtherCompany = Guid.Parse("00000000-0000-4000-8000-000000000102");
    private static readonly Guid Kilogram = Guid.Parse("00000000-0000-4000-8000-000000000103");
    private static readonly Guid Piece = Guid.Parse("00000000-0000-4000-8000-000000000104");
    private static readonly Guid ProductA = Guid.Parse("00000000-0000-4000-8000-000000000105");
    private static readonly Guid ProductB = Guid.Parse("00000000-0000-4000-8000-000000000106");
    private static readonly Guid OrderA = Guid.Parse("00000000-0000-4000-8000-000000000107");
    private static readonly Guid LineA = Guid.Parse("00000000-0000-4000-8000-000000000108");
    private static readonly Guid LineB = Guid.Parse("00000000-0000-4000-8000-000000000109");
    private static readonly Guid Purchase = Guid.Parse("00000000-0000-4000-8000-000000000110");
    private static readonly Guid PurchaseLine = Guid.Parse("00000000-0000-4000-8000-000000000111");
    private static readonly Guid Label = Guid.Parse("00000000-0000-4000-8000-000000000112");
    private static readonly Guid Location = Guid.Parse("00000000-0000-4000-8000-000000000113");

    [Fact]
    public async Task The_demand_source_answers_shippable_minus_shipped_and_skips_cancelled_and_fully_shipped_lines()
    {
        var source = new InventoryShortageDemandSource(new Context(Company),
            new PagedSalesLines(
                [
                    SalesLine(LineA, "100.5", shippingCap: "80"),
                    SalesLine(LineB, "40"),
                    SalesLine(Guid.Parse("00000000-0000-4000-8000-000000000120"), "10",
                        remainderState: SalesRemainderChangeNamesV1.Cancelled),
                ]),
            new Deliveries(new SalesOrderDeliveryReadAnswerV1(Company, OrderA,
            [
                new SalesOrderLineDeliveryV1(LineA, Kilogram, 30m, 30m),
                new SalesOrderLineDeliveryV1(LineB, Kilogram, 40m, 40m),
            ])));

        var answer = await source.ListOpenDemandAsync();

        Assert.Equal(InventoryShortageSourceOutcomeV1.Answered, answer.Outcome);
        var line = Assert.Single(answer.Lines!);
        Assert.Equal(LineA, line.LineId);
        Assert.Equal("50", line.OpenQuantityText);
        Assert.Equal(new DateOnly(2026, 12, 1), line.DueDate);
        Assert.Equal("SENTETIK-SO-1", line.DocumentNo);
    }

    [Fact]
    public async Task The_demand_source_is_unavailable_without_ports_for_another_company_or_on_an_owner_failure()
    {
        var context = new Context(Company);
        Assert.Equal(InventoryShortageSourceOutcomeV1.Unavailable,
            (await new InventoryShortageDemandSource(context).ListOpenDemandAsync()).Outcome);
        Assert.Equal(InventoryShortageSourceOutcomeV1.Unavailable,
            (await new InventoryShortageDemandSource(context,
                new PagedSalesLines([SalesLine(LineA, "10") with { CompanyId = OtherCompany }]),
                new Deliveries(new SalesOrderDeliveryReadAnswerV1(Company, OrderA, []))).ListOpenDemandAsync()).Outcome);
        Assert.Equal(InventoryShortageSourceOutcomeV1.Unavailable,
            (await new InventoryShortageDemandSource(context, new PagedSalesLines([SalesLine(LineA, "10")]),
                new Deliveries(null)).ListOpenDemandAsync()).Outcome);
        Assert.Equal(InventoryShortageSourceOutcomeV1.Unavailable,
            (await new InventoryShortageDemandSource(context, new PagedSalesLines([SalesLine(LineA, "10")]),
                new Deliveries(new SalesOrderDeliveryReadAnswerV1(Company, OrderA,
                    [new SalesOrderLineDeliveryV1(LineA, Piece, 1m, 1m)]))).ListOpenDemandAsync()).Outcome);
        Assert.Equal(InventoryShortageSourceOutcomeV1.Unavailable,
            (await new InventoryShortageDemandSource(context, new PagedSalesLines([SalesLine(LineA, "1,5")]),
                new Deliveries(new SalesOrderDeliveryReadAnswerV1(Company, OrderA, []))).ListOpenDemandAsync()).Outcome);
    }

    [Fact]
    public void Free_unreserved_stock_is_summed_per_position_and_an_over_reserved_position_adds_nothing()
    {
        var otherLabel = Guid.Parse("00000000-0000-4000-8000-000000000121");
        var free = InventoryShortagePositionFactsFreeUnreserved(
            [Position(Label, ProductA, 100m), Position(otherLabel, ProductA, 20m)],
            [Position(Label, Guid.Empty, 30m), Position(otherLabel, Guid.Empty, 25m)]);

        Assert.Equal(70m, free[ProductA]);
    }

    [Fact]
    public async Task Rule_one_shortage_is_answered_with_exact_figures_and_purchase_dates_not_answered()
    {
        var service = Service(
            demand: Answered(Demand(ProductA, LineA, "300", Kilogram)),
            supply: SupplyAnswered(false, Supply(ProductA, Kilogram, "150")),
            freeKg: new Dictionary<Guid, decimal> { [ProductA] = 100m });

        var result = await service.ReadAsync(new InventoryShortageWarningQueryV1());

        Assert.Equal(Company, result.CompanyId);
        Assert.Equal(InventoryShortageSupplyDatesV1.NotAnswered, result.SupplyDates);
        var item = Assert.Single(result.Items);
        Assert.Equal(InventoryShortageStatusNamesV1.Shortage, item.Status);
        Assert.Equal("300", item.OpenDemandKg);
        Assert.Equal("100", item.FreeStockKg);
        Assert.Equal("150", item.OpenSupplyKg);
        Assert.Equal("50", item.ShortageKg);
        Assert.Equal(InventoryShortageSimilarStatusNamesV1.NotConfigured, item.SimilarStock.Status);
        Assert.Equal(InventoryShortageCoverageNamesV1.Uncovered, Assert.Single(item.Lines).Coverage);
    }

    [Fact]
    public async Task A_purchase_with_an_unknown_date_is_coverage_date_unknown_and_covered_products_are_hidden_by_default()
    {
        var service = Service(
            demand: Answered(Demand(ProductA, LineA, "60", Kilogram), Demand(ProductB, LineB, "5", Kilogram)),
            supply: SupplyAnswered(false, Supply(ProductA, Kilogram, "100")),
            freeKg: new Dictionary<Guid, decimal> { [ProductA] = 10m, [ProductB] = 5m });

        var warnings = await service.ReadAsync(new InventoryShortageWarningQueryV1());
        var all = await service.ReadAsync(new InventoryShortageWarningQueryV1(OnlyWarnings: false));

        var item = Assert.Single(warnings.Items);
        Assert.Equal(ProductA, item.ProductId);
        Assert.Equal(InventoryShortageStatusNamesV1.CoverageDateUnknown, item.Status);
        Assert.Equal(2, all.Items.Count);
        Assert.Equal(InventoryShortageStatusNamesV1.Covered, all.Items[1].Status);
    }

    [Fact]
    public async Task A_line_not_in_kilograms_makes_the_product_unconvertible_with_no_figures()
    {
        var service = Service(
            demand: Answered(Demand(ProductA, LineA, "3", Piece)),
            supply: SupplyAnswered(false),
            freeKg: new Dictionary<Guid, decimal>());

        var item = Assert.Single((await service.ReadAsync(new InventoryShortageWarningQueryV1())).Items);

        Assert.Equal(InventoryShortageStatusNamesV1.Unconvertible, item.Status);
        Assert.Null(item.OpenDemandKg);
        Assert.Null(item.ShortageKg);
        Assert.Empty(item.Lines);
    }

    [Fact]
    public async Task An_unavailable_or_foreign_source_fails_the_whole_read_with_503()
    {
        var unavailableSupply = Service(Answered(Demand(ProductA, LineA, "1", Kilogram)),
            new InventoryShortageSupplyAnswerV1(InventoryShortageSourceOutcomeV1.Unavailable, null, false),
            new Dictionary<Guid, decimal>());
        var foreignDemand = Service(Answered(Demand(ProductA, LineA, "1", Kilogram) with { CompanyId = OtherCompany }),
            SupplyAnswered(false), new Dictionary<Guid, decimal>());
        var datedWithoutDates = Service(Answered(Demand(ProductA, LineA, "1", Kilogram)),
            SupplyAnswered(false, Supply(ProductA, Kilogram, "1") with { ExpectedDate = new DateOnly(2026, 12, 1) }),
            new Dictionary<Guid, decimal>());

        foreach (var service in new[] { unavailableSupply, foreignDemand, datedWithoutDates })
        {
            var error = await Assert.ThrowsAsync<InventoryShortageWarningException>(() =>
                service.ReadAsync(new InventoryShortageWarningQueryV1()));
            Assert.Equal(503, error.StatusCode);
            Assert.Equal(InventoryShortageWarningCodesV1.Unavailable, error.Code);
        }
    }

    [Fact]
    public async Task The_read_needs_both_stock_read_names_company_wide_and_an_empty_product_id_is_400()
    {
        var denied = Service(Answered(), SupplyAnswered(false), new Dictionary<Guid, decimal>(),
            new Authority { Denied = ErpPermissions.InventoryBalancesRead });
        var forbidden = await Assert.ThrowsAsync<InventoryShortageWarningException>(() =>
            denied.ReadAsync(new InventoryShortageWarningQueryV1()));
        Assert.Equal(403, forbidden.StatusCode);

        var anonymous = Service(Answered(), SupplyAnswered(false), new Dictionary<Guid, decimal>(),
            user: new User { IsAuthenticated = false });
        Assert.Equal(401, (await Assert.ThrowsAsync<InventoryShortageWarningException>(() =>
            anonymous.ReadAsync(new InventoryShortageWarningQueryV1()))).StatusCode);

        var invalid = Service(Answered(), SupplyAnswered(false), new Dictionary<Guid, decimal>());
        var badRequest = await Assert.ThrowsAsync<InventoryShortageWarningException>(() =>
            invalid.ReadAsync(new InventoryShortageWarningQueryV1(Guid.Empty)));
        Assert.Equal(400, badRequest.StatusCode);
        Assert.Equal(InventoryShortageWarningCodesV1.InvalidQuery, badRequest.Code);
    }

    [Fact]
    public void Kilogram_texts_are_exact_and_without_trailing_zeros()
    {
        Assert.Equal("1.5", InventoryShortageWarningService.Text(1.500m));
        Assert.Equal("0.001", InventoryShortageWarningService.Text(0.001m));
        Assert.Equal("0", InventoryShortageWarningService.Text(0.000m));
        Assert.Equal("120", InventoryShortageWarningService.Text(120.000m));
    }

    private static IReadOnlyDictionary<Guid, decimal> InventoryShortagePositionFactsFreeUnreserved(
        IReadOnlyList<InventoryShortagePositionFact> balances, IReadOnlyList<InventoryShortagePositionFact> allocations) =>
        SqlInventoryShortageStockReader.FreeUnreserved(balances, allocations);

    private static InventoryShortagePositionFact Position(Guid labelId, Guid productId, decimal kilograms) =>
        new(labelId, 1, Company, 1, Company, Location, productId, kilograms);

    private static InventoryShortageWarningService Service(
        InventoryShortageDemandAnswerV1 demand,
        InventoryShortageSupplyAnswerV1 supply,
        IReadOnlyDictionary<Guid, decimal> freeKg,
        Authority? authority = null,
        User? user = null) =>
        new(new Context(Company), user ?? new User(), authority ?? new Authority(), new FixedDemand(demand),
            new FixedSupply(supply), new InventoryNotConfiguredShortageSimilarStockSourceV1(), new Units(),
            new FixedStock(new InventoryShortageStockSnapshot(freeKg,
                new Dictionary<(Guid OrderId, Guid LineId), decimal>())));

    private static InventoryShortageDemandAnswerV1 Answered(params InventoryShortageDemandLineV1[] lines) =>
        new(InventoryShortageSourceOutcomeV1.Answered, lines);

    private static InventoryShortageSupplyAnswerV1 SupplyAnswered(bool datesAnswered,
        params InventoryShortageSupplyLineV1[] lines) =>
        new(InventoryShortageSourceOutcomeV1.Answered, lines, datesAnswered);

    private static InventoryShortageDemandLineV1 Demand(Guid productId, Guid lineId, string quantity, Guid unitId) =>
        new(Company, OrderA, "SENTETIK-SO-1", lineId, 1, productId, "SENTETIK-" + productId.ToString("N")[^4..],
            "SENTETIK ürün", unitId, new DateOnly(2026, 12, 1), quantity);

    private static InventoryShortageSupplyLineV1 Supply(Guid productId, Guid unitId, string quantity) =>
        new(Company, Purchase, PurchaseLine, productId, unitId, quantity, null);

    private static SalesApprovedLineReferenceV1 SalesLine(Guid lineId, string quantity, string? shippingCap = null,
        string remainderState = SalesRemainderChangeNamesV1.Open) =>
        new(Company, OrderA, "SENTETIK-SO-1", lineId, 1, Guid.Parse("00000000-0000-4000-8000-000000000130"),
            Guid.Parse("00000000-0000-4000-8000-000000000131"), ProductA, "1", "SENTETIK-P", "SENTETIK ürün", Kilogram, "KG",
            quantity, new DateOnly(2026, 12, 1), 1, remainderState, ShippingCapQuantityText: shippingCap);

    private sealed class Context(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = "SENTETIK-shortage-reader";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public string? Denied { get; set; }

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(permission != Denied, true, new HashSet<string>(), new HashSet<Guid>(),
                new HashSet<Guid>(), new HashSet<Guid>()));
    }

    private sealed class PagedSalesLines(IReadOnlyList<SalesApprovedLineReferenceV1> lines) : ISalesOrderLineReferencePortV1
    {
        public Task<SalesOrderLineReferenceAnswerV1> GetApprovedLineAsync(Guid orderId, Guid lineId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SalesApprovedLinePageV1> ListApprovedLinesAsync(int pageSize, string? cursor,
            CancellationToken cancellationToken = default) => Task.FromResult(new SalesApprovedLinePageV1(lines, null));
    }

    /// <summary>A null answer stands for an owner that fails: the read throws.</summary>
    private sealed class Deliveries(SalesOrderDeliveryReadAnswerV1? answer) : ISalesOrderDeliveryReadPortV1
    {
        public Task<SalesOrderDeliveryReadAnswerV1> ReadOrderDeliveriesAsync(Guid orderId,
            CancellationToken cancellationToken = default) =>
            answer is null ? throw new InvalidOperationException("SENTETIK owner failure") : Task.FromResult(answer);
    }

    private sealed class FixedDemand(InventoryShortageDemandAnswerV1 answer) : IInventoryShortageDemandSourceV1
    {
        public Task<InventoryShortageDemandAnswerV1> ListOpenDemandAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(answer);
    }

    private sealed class FixedSupply(InventoryShortageSupplyAnswerV1 answer) : IInventoryShortageSupplySourceV1
    {
        public Task<InventoryShortageSupplyAnswerV1> ListOpenSupplyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(answer);
    }

    private sealed class Units : ISettingsUnitIdentityCatalogV1
    {
        public Task<UnitIdentityV1?> ReadAsync(Guid unitId, CancellationToken cancellationToken = default) =>
            Task.FromResult<UnitIdentityV1?>(unitId == Kilogram
                ? new UnitIdentityV1(Kilogram, "KG", "MASS", true)
                : new UnitIdentityV1(unitId, "AD", "COUNT", false));

        public Task<IReadOnlyList<UnitIdentityV1>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedStock(InventoryShortageStockSnapshot snapshot) : IInventoryShortageStockReader
    {
        public Task<InventoryShortageStockSnapshot> ReadAsync(Guid companyId, IReadOnlyCollection<Guid> productIds,
            IReadOnlyCollection<Guid> salesOrderIds, CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }
}
