using GoymenERP.Modules.Inventory.Domain.FutureSupply;

namespace GoymenERP.Inventory.UnitTests;

public sealed class FutureSupplyReceiptReversalTests
{
    [Theory]
    [InlineData(100, 100, 100, 0)]
    [InlineData(100, 30, 30, 70)]
    [InlineData(100, 0, 0, 100)]
    public void Only_the_share_fitting_the_locked_cap_reopens_and_the_rest_is_explicitly_released(
        decimal undone, decimal cap, decimal reopened, decimal released)
    {
        var plan = FutureSupplyReceiptReversal.Plan(100m, 100m, 0m, undone, 100m, true, cap, 100m);
        Assert.Equal((undone, reopened, released, 0m),
            (plan.UndoneQuantity, plan.ReopenedQuantity, plan.OverflowReleasedQuantity, plan.UnheldQuantity));
    }

    [Fact]
    public void A_previously_released_future_share_never_returns_and_identity_is_kept()
    {
        var id = Guid.NewGuid();
        var allocation = FutureSupplyAllocation.Restore(id, Guid.NewGuid(), FutureSupplyQuantity.Of(100m),
            FutureSupplyQuantity.Zero, FutureSupplyQuantity.Of(20m), FutureSupplyAllocationStatus.Consumed, 4);
        allocation.ReverseConsumption(4, FutureSupplyQuantity.Of(50m), FutureSupplyQuantity.Of(30m));
        Assert.Equal((id, 30m, 40m, 30m, 5L, FutureSupplyAllocationStatus.Active),
            (allocation.Id, allocation.Remaining.Value, allocation.Released.Value, allocation.Consumed.Value,
                allocation.Version, allocation.Status));
    }

    [Fact]
    public void A_closed_demand_or_a_released_physical_hold_is_recorded_as_unheld_without_reviving_it()
    {
        var closed = FutureSupplyReceiptReversal.Plan(100m, 100m, 0m, 20m, 20m, false, 100m, 100m);
        var released = FutureSupplyReceiptReversal.Plan(100m, 100m, 0m, 20m, 0m, true, 100m, 100m);
        Assert.Equal((0m, 0m, 20m), (closed.ReopenedQuantity, closed.OverflowReleasedQuantity, closed.UnheldQuantity));
        Assert.Equal(closed, released);
    }

    [Fact]
    public void A_shrunk_physical_hold_only_reopens_its_still_active_part()
    {
        var plan = FutureSupplyReceiptReversal.Plan(100m, 100m, 0m, 40m, 15m, true, 10m, 100m);
        Assert.Equal((10m, 5m, 25m), (plan.ReopenedQuantity, plan.OverflowReleasedQuantity, plan.UnheldQuantity));
    }

    [Fact]
    public void Original_conversion_ratios_are_exact_and_a_partial_share_is_never_rounded()
    {
        Assert.Equal(4m, FutureSupplyReceiptReversal.ExactRatio(10m, 8m, 20m));
        Assert.Throws<FutureSupplyReceiptReversalPrecisionException>(() => FutureSupplyReceiptReversal.ExactRatio(1m, 1m, 3m));
        Assert.Throws<ArgumentException>(() => FutureSupplyReceiptReversal.Plan(100m, 100m, 90m, 11m, 100m, true, 100m, 100m));
    }

    [Fact]
    public void An_old_version_or_an_excess_undo_changes_nothing()
    {
        var allocation = FutureSupplyAllocation.Restore(Guid.NewGuid(), Guid.NewGuid(), FutureSupplyQuantity.Of(50m),
            FutureSupplyQuantity.Zero, FutureSupplyQuantity.Zero, FutureSupplyAllocationStatus.Consumed, 2);
        Assert.Throws<FutureSupplyRuleException>(() => allocation.ReverseConsumption(1, FutureSupplyQuantity.Of(10m), FutureSupplyQuantity.Of(10m)));
        Assert.Throws<FutureSupplyRuleException>(() => allocation.ReverseConsumption(2, FutureSupplyQuantity.Of(51m), FutureSupplyQuantity.Of(10m)));
        Assert.Equal((0m, 0m, 50m, 2L), (allocation.Remaining.Value, allocation.Released.Value, allocation.Consumed.Value, allocation.Version));
    }
}
