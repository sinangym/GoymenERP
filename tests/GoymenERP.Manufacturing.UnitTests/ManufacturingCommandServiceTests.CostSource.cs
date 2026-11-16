using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed partial class ManufacturingCommandServiceTests
{
    [Fact]
    public async Task Cost_source_joins_real_fact_and_movement_identities_and_keeps_the_policy_and_input_provenance()
    {
        var fixture = new Fixture(); var order = await fixture.BalancedOrderAsync(); order.Close(order.Version, Actor, At.AddDays(1));
        var owner = new CostMovementOwner(order); var policy = new CostPolicyOwner(); var prices = new CostPrices();
        var source = new ManufacturingCostCompositionSourceV1(fixture.CompanyContext, fixture.Store, owner, policy, prices);
        var read = await source.ReadAsync(Company, order.Id, CancellationToken.None);
        Assert.Equal(ManufacturingScrapCostDistributionSourceStatusV1.Found, read.Status);
        Assert.Equal(owner.Snapshot.Issues[0].MovementId, Assert.Single(read.Input!.Cost.Issues).IssueMovementId);
        Assert.Equal(owner.Snapshot.Outputs[0].LabelId, Assert.Single(read.Input.Labels).OutputLabelId);
        Assert.NotEqual(order.Outputs[0].OutputId, read.Input.Labels[0].OutputLabelId);
        Assert.Equal("manufacturing.include-scrap-cost:7", read.Provenance!.ScrapPolicyVersion);
        Assert.Equal(order.Version, read.Provenance.OrderVersion); Assert.Equal(64, read.Provenance.InputFingerprint.Length);
        var result = await new ManufacturingProductionCostPort(source).ReadAsync(Company, order.Id, CancellationToken.None);
        Assert.Equal(ManufacturingScrapCostDistributionStatusV1.Available, result.Status);
        Assert.Equal(1000m, result.InputTotalTl); Assert.Equal(1000m, result.DistributedTotalTl);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("order-version")]
    [InlineData("fact-identity")]
    [InlineData("mass")]
    public async Task Cost_source_refuses_scope_or_factual_changes_without_silent_defaults(string fault)
    {
        var fixture = new Fixture(); var order = await fixture.BalancedOrderAsync(); var owner = new CostMovementOwner(order);
        if (fault == "company") owner.BeforeRead = () => fixture.CompanyContext.SwitchForTest(OtherCompany);
        if (fault == "order-version") owner.BeforeRead = () => order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(1m), At.AddMinutes(1));
        if (fault == "fact-identity") owner.Snapshot = owner.Snapshot with { Issues = [owner.Snapshot.Issues[0] with { FactId = Guid.NewGuid() }] };
        if (fault == "mass") owner.Snapshot = owner.Snapshot with { Issues = [owner.Snapshot.Issues[0] with { Mass = new Kilograms(99m) }] };
        var policy = new CostPolicyOwner();
        var read = await new ManufacturingCostCompositionSourceV1(fixture.CompanyContext, fixture.Store, owner, policy, new CostPrices())
            .ReadAsync(Company, order.Id, CancellationToken.None);
        Assert.Equal(ManufacturingScrapCostDistributionSourceStatusV1.Unavailable, read.Status); Assert.Null(read.Input);
        Assert.Equal(0, policy.Reads);
    }

    [Fact]
    public async Task Cost_source_selects_each_scrap_weighings_Istanbul_month_and_freezes_its_actual_price_version()
    {
        var fixture = new Fixture(); var order = await fixture.ApprovedOrderAsync();
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(100m), At);
        order.DeclareOutput(order.Version, Guid.NewGuid(), Roll, OutputClass.GoodProduct, new Kilograms(90m), At);
        var boundary = DateTimeOffset.Parse("2026-09-30T22:30:00Z");
        order.DeclareOutput(order.Version, Guid.NewGuid(), Roll, OutputClass.Scrap, new Kilograms(10m), boundary);
        order.Close(order.Version, Actor, boundary.AddDays(1));
        var october = MonthlyScrapPrice.Record(Guid.NewGuid(), Company, new(2026, 10), new(2m), "synthetic-author", boundary.AddDays(1));
        var prices = new CostPrices { Entries = [october] };
        var read = await new ManufacturingCostCompositionSourceV1(fixture.CompanyContext, fixture.Store, new CostMovementOwner(order), new CostPolicyOwner(), prices)
            .ReadAsync(Company, order.Id, CancellationToken.None);
        Assert.Equal(ManufacturingScrapCostDistributionSourceStatusV1.Found, read.Status);
        Assert.Equal(2m, Assert.Single(read.Input!.Cost.ScrapWeighings).Price.TlPerKg);
        var price = Assert.Single(read.Provenance!.MonthlyPrices); Assert.Equal((2026, 10, 1, october.Id), (price.Year, price.Month, price.Version, price.ScrapPriceId));
    }

    [Fact]
    public async Task Cost_source_keeps_a_missing_issue_stamp_unreadable_and_never_supplies_a_current_average()
    {
        var fixture = new Fixture(); var order = await fixture.BalancedOrderAsync(); order.Close(order.Version, Actor, At.AddDays(1));
        var owner = new CostMovementOwner(order);
        owner.Snapshot = owner.Snapshot with { Issues = [owner.Snapshot.Issues[0] with { Status = IssueCostReadStatus.Unreadable, StampedUnitCostTl = null }] };
        var source = new ManufacturingCostCompositionSourceV1(fixture.CompanyContext, fixture.Store, owner, new CostPolicyOwner(), new CostPrices());
        var result = await new ManufacturingProductionCostPort(source).ReadAsync(Company, order.Id, CancellationToken.None);
        Assert.Equal(ManufacturingScrapCostDistributionStatusV1.Unreadable, result.Status); Assert.Null(result.InputTotalTl); Assert.Empty(result.Lines);
    }

    private sealed class CostMovementOwner : IManufacturingCostMovementReadPortV1
    {
        public CostMovementOwner(ProductionOrder order) => Snapshot = new(Company, order.Id,
            order.Issues.Select(fact => new ManufacturingCostIssueSnapshotV1(fact.IssueId, Guid.NewGuid(), fact.ParentRollId, fact.Mass,
                IssueCostReadStatus.Value, 10m, IssueCostCurrencyInput.FunctionalCurrency, "synthetic-issue-stamp:1")).ToArray(), [],
            order.Outputs.Where(fact => fact.Class != OutputClass.ProcessLoss).Select(fact => new ManufacturingCostOutputSnapshotV1(fact.OutputId,
                Guid.NewGuid(), fact.ParentRollId, Guid.NewGuid(), "SYNTHETIC-" + fact.OutputId.ToString("N"), fact.Mass, "synthetic-output-stamp:1")).ToArray(),
            NonKgInputUse.None, "synthetic-movement-source:1");
        public ManufacturingCostMovementSnapshotV1 Snapshot { get; set; }
        public Action? BeforeRead { get; set; }
        public Task<ManufacturingCostMovementSnapshotV1?> ReadAsync(Guid companyId, Guid orderId, CancellationToken cancellationToken)
        { BeforeRead?.Invoke(); return Task.FromResult<ManufacturingCostMovementSnapshotV1?>(Snapshot); }
    }
    private sealed class CostPolicyOwner : IManufacturingScrapCostPolicyReadPortV1
    {
        public int Reads { get; private set; }
        public Task<ManufacturingScrapCostPolicySnapshotV1?> ReadAsync(Guid companyId, CancellationToken cancellationToken)
        { Reads++; return Task.FromResult<ManufacturingScrapCostPolicySnapshotV1?>(new(companyId, true, "manufacturing.include-scrap-cost:7")); }
    }
    private sealed class CostPrices : IManufacturingMonthlyScrapPriceStore
    {
        public IReadOnlyList<MonthlyScrapPrice> Entries { get; init; } = [];
        public Task<IReadOnlyList<MonthlyScrapPrice>> ListScrapPricesAsync(int year, CancellationToken cancellationToken = default) => Task.FromResult(Entries);
        public Task<MonthlyScrapPrice> RecordScrapPriceAsync(MonthlyScrapPrice price, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MonthlyScrapPrice> CorrectScrapPriceAsync(Guid correctionId, ScrapPricePeriod period, int expectedVersion, ScrapPriceTlPerKg price, string reason,
            ManufacturingCommandMetadata metadata, DateTimeOffset correctedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
