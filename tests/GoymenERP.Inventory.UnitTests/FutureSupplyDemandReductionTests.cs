using GoymenERP.Modules.Inventory.Domain.FutureSupply;

namespace GoymenERP.Inventory.UnitTests;

public sealed class FutureSupplyDemandReductionTests
{
    private static readonly Guid First = Guid.Parse("8a170000-0000-4000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("8a170000-0000-4000-8000-000000000002");

    [Theory]
    [InlineData(70, 60, 0)]
    [InlineData(70, 100, 30)]
    [InlineData(50, 60, 10)]
    [InlineData(0, 60, 60)]
    [InlineData(40, 60, 20)]
    public void Reduction_releases_only_the_excess_over_new_open_demand(decimal open, decimal future, decimal expected)
    {
        var plan = FutureSupplyDemandReduction.Plan(open, 0m, [Allocation(First, future)]);
        Assert.Equal(expected, plan.Sum(item => item.Quantity));
    }

    [Fact]
    public void The_latest_purchase_delivery_is_released_first_and_an_explicit_choice_overrides_it()
    {
        var december = Allocation(First, 10m) with { ExpectedDeliveryDate = new DateOnly(2026, 12, 15) };
        var january = Allocation(Second, 20m) with { ExpectedDeliveryDate = new DateOnly(2027, 1, 5) };
        Assert.Equal(new FutureSupplyReductionStep(Second, 1, 10m),
            Assert.Single(FutureSupplyDemandReduction.Plan(20m, 0m, [december, january])));
        Assert.Equal(new FutureSupplyReductionStep(First, 1, 10m),
            Assert.Single(FutureSupplyDemandReduction.Plan(20m, 0m, [december, january], [new(First, 1, 10m, false)])));
    }

    [Fact]
    public void Physical_stock_stays_allocated_when_future_supply_can_cover_the_excess()
    {
        Assert.Equal(5m, Assert.Single(FutureSupplyDemandReduction.Plan(15m, 10m, [Allocation(First, 10m)])).Quantity);
        AssertFailure(FutureSupplyReductionFailure.PhysicalReleaseUnavailable,
            () => FutureSupplyDemandReduction.Plan(5m, 10m, [Allocation(First, 10m)]));
        AssertFailure(FutureSupplyReductionFailure.PhysicalReleaseUnavailable,
            () => FutureSupplyDemandReduction.Plan(15m, 10m, [Allocation(First, 10m)], [new(Second, 1, 5m, true)]));
    }

    [Fact]
    public void Explicit_selection_checks_version_remainder_and_the_exact_excess()
    {
        var allocations = new[] { Allocation(First, 20m) };
        AssertFailure(FutureSupplyReductionFailure.VersionConflict,
            () => FutureSupplyDemandReduction.Plan(10m, 0m, allocations, [new(First, 2, 10m, false)]));
        AssertFailure(FutureSupplyReductionFailure.InvalidSelection,
            () => FutureSupplyDemandReduction.Plan(10m, 0m, allocations, [new(First, 1, 11m, false)]));
        AssertFailure(FutureSupplyReductionFailure.InvalidSelection,
            () => FutureSupplyDemandReduction.Plan(10m, 0m, allocations, [new(First, 1, 21m, false)]));
        AssertFailure(FutureSupplyReductionFailure.InvalidSelection,
            () => FutureSupplyDemandReduction.Plan(10m, 0m, allocations, [new(First, 1, 5m, false), new(First, 1, 5m, false)]));
    }

    [Fact]
    public void Different_units_share_the_kilogram_cap_and_an_inexact_purchase_quantity_is_refused()
    {
        var tonnes = Allocation(First, 2m) with { KilogramsPerUnit = 1000m };
        Assert.Equal(0.5m, Assert.Single(FutureSupplyDemandReduction.Plan(1500m, 0m, [tonnes])).Quantity);
        var pieces = Allocation(First, 20m) with { KilogramsPerUnit = 3m };
        AssertFailure(FutureSupplyReductionFailure.ConversionUnavailable,
            () => FutureSupplyDemandReduction.Plan(59m, 0m, [pieces]));
    }

    private static FutureSupplyReductionAllocation Allocation(Guid id, decimal quantity) =>
        new(id, 1, quantity, 1m, new DateOnly(2026, 12, 1), DateTimeOffset.Parse("2026-11-16T00:00:00Z"));

    private static void AssertFailure(FutureSupplyReductionFailure expected, Action action) =>
        Assert.Equal(expected, Assert.Throws<FutureSupplyReductionException>(action).Failure);
}
