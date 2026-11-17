using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Typed owner bridge source tests. Actual movement stamps and generation persistence need real SQL facts.</summary>
public sealed class ManufacturingInventoryCostMovementReaderTests
{
    [Fact]
    public async Task Foreign_receipt_invoice_proof_without_actual_order_priority_composition_is_not_usable_cost()
    {
        var fixture = new Fixture(); var issue = fixture.Owner.Value.Issues[0];
        fixture.Owner.Value = fixture.Owner.Value with { Issues = [issue with { Currency = new(InventoryIssueCostCurrencyStatusV1.Converted,
            Guid.NewGuid(), 2m, 40m, "synthetic-invoice:1", null) }] };
        Assert.Null(await new ManufacturingInventoryCostMovementReader(fixture.Company, fixture.User, fixture.Owner)
            .ReadAsync(fixture.CompanyId, fixture.OrderId, CancellationToken.None));
    }

    [Theory]
    [InlineData("100", "3", null, true)]
    [InlineData("141", "4", "35.25", true)]
    [InlineData("100", "3", "33.3333", false)]
    [InlineData("200", "6", null, false)]
    public async Task The_inventory_weighted_entry_rate_is_carried_exactly_or_refused_when_scalar_evidence_disagrees(string numerator, string denominator, string? scalar, bool valid)
    {
        var fixture = new Fixture(); var issue = fixture.Owner.Value.Issues[0];
        fixture.Owner.Value = fixture.Owner.Value with { Issues = [issue with { Currency = new(InventoryIssueCostCurrencyStatusV1.Converted,
            Guid.NewGuid(), 2m, scalar is null ? null : decimal.Parse(scalar, System.Globalization.CultureInfo.InvariantCulture),
            "synthetic-first-entry:1", null, null, new(numerator, denominator)) }] };
        var result = await fixture.Reader.ReadAsync(fixture.CompanyId, fixture.OrderId, CancellationToken.None);
        if (!valid) { Assert.Null(result); return; }
        var read = Assert.Single(result!.Issues);
        Assert.Equal(10m, read.StampedUnitCostTl);
        Assert.Equal(new ProductionExactRatioV1(numerator, denominator), read.Currency.ExchangeRateRatio);
        Assert.Equal(scalar is null ? (decimal?)null : decimal.Parse(scalar, System.Globalization.CultureInfo.InvariantCulture), read.Currency.ExchangeRate);
    }

    [Theory]
    [InlineData("1", "3", true)]
    [InlineData("1", "0", false)]
    [InlineData("2", "6", false)]
    public async Task An_exact_original_foreign_unit_ratio_remains_a_ratio_and_never_replaces_the_actual_tl_stamp(string numerator, string denominator, bool valid)
    {
        var fixture = new Fixture(); var issue = fixture.Owner.Value.Issues[0];
        fixture.Owner.Value = fixture.Owner.Value with { Issues = [issue with { Currency = new(InventoryIssueCostCurrencyStatusV1.Converted,
            Guid.NewGuid(), null, 40m, "synthetic-exact-order:1", null, new(numerator, denominator)) }] };
        var result = await fixture.Reader.ReadAsync(fixture.CompanyId, fixture.OrderId, CancellationToken.None);
        if (!valid) { Assert.Null(result); return; }
        var read = Assert.Single(result!.Issues); Assert.Equal(10m, read.StampedUnitCostTl);
        Assert.Null(read.Currency.ForeignUnitCostPerKg); Assert.Equal(new ProductionExactRatioV1("1", "3"), read.Currency.ForeignUnitCostRatio);
    }

    [Fact]
    public async Task An_issue_keeps_its_stamped_TL_and_separate_original_currency_proof_without_recalculation()
    {
        var fixture = new Fixture();
        var issue = fixture.Owner.Value.Issues[0];
        fixture.Owner.Value = fixture.Owner.Value with { Issues = [issue with {
            Currency = new(InventoryIssueCostCurrencyStatusV1.Converted, Guid.NewGuid(), 100m, 2m, "synthetic-order-fx:7", null) }] };
        var result = await fixture.Reader.ReadAsync(fixture.CompanyId, fixture.OrderId, CancellationToken.None);
        var read = Assert.Single(result!.Issues);
        Assert.Equal(IssueCostReadStatus.Value, read.Status); Assert.Equal(10m, read.StampedUnitCostTl);
        Assert.Equal((100m, 2m), (read.Currency.ForeignUnitCostPerKg, read.Currency.ExchangeRate));
        Assert.Equal(issue.FactId, read.FactId); Assert.Equal(issue.MovementId, read.MovementId);
    }

    [Fact]
    public async Task A_missing_legacy_stamp_stays_unreadable_and_a_missing_FX_keeps_its_distinct_reason_input()
    {
        var fixture = new Fixture(); var issue = fixture.Owner.Value.Issues[0];
        fixture.Owner.Value = fixture.Owner.Value with { Issues = [issue with { Tl = MissingStamp() }] };
        var missingCost = Assert.Single((await fixture.Reader.ReadAsync(fixture.CompanyId, fixture.OrderId, CancellationToken.None))!.Issues);
        Assert.Equal(IssueCostReadStatus.Unreadable, missingCost.Status); Assert.Null(missingCost.StampedUnitCostTl);
        fixture.Owner.Value = fixture.Owner.Value with { Issues = [fixture.Owner.Value.Issues[0] with {
            Currency = new(InventoryIssueCostCurrencyStatusV1.RateMissing, Guid.NewGuid(), 2m, null, "synthetic-missing-order-fx:1", "rate_missing") }] };
        var missingRate = Assert.Single((await fixture.Reader.ReadAsync(fixture.CompanyId, fixture.OrderId, CancellationToken.None))!.Issues);
        Assert.Equal(IssueCostReadStatus.Value, missingRate.Status); Assert.Equal(IssueCostCurrencyStatus.RateMissing, missingRate.Currency.Status);
        Assert.Null(missingRate.StampedUnitCostTl); Assert.Null(missingRate.Currency.ExchangeRate);
    }

    [Theory]
    [InlineData("owner-company")]
    [InlineData("owner-order")]
    [InlineData("source-kind")]
    [InlineData("movement-kind")]
    [InlineData("wrong-TL-currency")]
    [InlineData("scope")]
    [InlineData("actor")]
    public async Task A_foreign_scope_or_wrong_movement_proof_is_not_used_as_cost(string fault)
    {
        var fixture = new Fixture(); var value = fixture.Owner.Value;
        if (fault == "owner-company") value = value with { CompanyId = Guid.NewGuid() };
        if (fault == "owner-order") value = value with { ManufacturingOrderId = Guid.NewGuid() };
        if (fault == "source-kind") value = value with { Issues = [value.Issues[0] with { SourceKind = "sales.shipment" }] };
        if (fault == "movement-kind") value = value with { Issues = [value.Issues[0] with { MovementKind = StockMovementKind.Receipt }] };
        if (fault == "wrong-TL-currency") value = value with { Issues = [value.Issues[0] with { Tl = value.Issues[0].Tl with { CurrencyId = Guid.NewGuid() } }] };
        fixture.Owner.Value = value;
        fixture.Owner.BeforeRead = () => { if (fault == "scope") fixture.Company.Set(Guid.NewGuid()); if (fault == "actor") fixture.User.Actor = "another-synthetic-actor"; };
        Assert.Null(await fixture.Reader.ReadAsync(fixture.CompanyId, fixture.OrderId, CancellationToken.None));
    }

    private static InventoryMovementCostStampV1 MissingStamp() => new(InventoryMovementCostStampStatusV1.Unavailable,
        Guid.Empty, null, null, null, "synthetic-legacy-missing:1", "not_calculated");
    private sealed class Fixture
    {
        public Guid CompanyId { get; } = Guid.NewGuid(); public Guid OrderId { get; } = Guid.NewGuid();
        public CompanyContext Company { get; } = new(); public User User { get; } public Owner Owner { get; }
        public ManufacturingInventoryCostMovementReader Reader { get; }
        public Fixture()
        {
            Company.Set(CompanyId); User = new(CompanyId);
            Owner = new() { Value = new(InventoryManufacturingCostReadOutcomeV1.Available, CompanyId, OrderId,
                [new(Guid.NewGuid(), Guid.NewGuid(), InventoryManufacturingRealizationContractV1.InputIssueSourceKind, StockMovementKind.Issue,
                    Guid.NewGuid(), Guid.NewGuid(), "2", 100m,
                    new(InventoryMovementCostStampStatusV1.Known, SettingsCurrencyIdentitiesV1.TurkishLira, 10m, 1000m, 7, "synthetic-TL-stamp:7", null),
                    MissingStamp(), new(InventoryIssueCostCurrencyStatusV1.FunctionalCurrency, null, null, null, "synthetic-functional-proof:1", null))], [], [], "synthetic-owner-source:1") };
            // These bridge tests isolate stamp/representation mapping; real owner priority composition has its own tests.
            Reader = new(Company, User, Owner, new SyntheticPriorityProjection());
        }
    }
    private sealed class Owner : IInventoryManufacturingCostReadPortV1
    {
        public required InventoryManufacturingCostReadV1 Value { get; set; } public Action? BeforeRead { get; set; }
        public Task<InventoryManufacturingCostReadV1> GetAsync(Guid companyId, Guid manufacturingOrderId, CancellationToken cancellationToken = default)
        { BeforeRead?.Invoke(); return Task.FromResult(Value); }
    }
    private sealed class SyntheticPriorityProjection : IManufacturingInputRatePriorityReadPortV1
    {
        public Task<ManufacturingInputRateProofV1?> ReadAsync(Guid companyId, Guid orderId, Guid factId, Guid labelId,
            IssueCostCurrencyInput recordedCurrency, CancellationToken ct) =>
            Task.FromResult<ManufacturingInputRateProofV1?>(new(recordedCurrency, "synthetic-priority:1", "{}"));
    }
    private sealed class User(Guid companyId) : ICurrentUser
    {
        public string Actor { get; set; } = "synthetic-cost-reader"; public bool IsAuthenticated => true; public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { companyId }; public bool CanAccessCompany(Guid value) => value == companyId;
    }
}
