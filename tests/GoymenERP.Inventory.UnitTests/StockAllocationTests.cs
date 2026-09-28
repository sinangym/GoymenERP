using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Inventory.UnitTests;

public sealed class StockAllocationTests
{
    private static readonly Guid Company = Guid.Parse("89000000-0000-0000-0000-000000000001");
    private static readonly Guid AllocationId = Guid.Parse("89000000-0000-0000-0000-000000000002");
    private static readonly Guid LabelId = Guid.Parse("89000000-0000-0000-0000-000000000003");

    [Fact]
    public void Create_starts_active_at_version_one_without_a_stock_movement()
    {
        var allocation = StockAllocation.Create(AllocationId, Position(), Kilograms.Positive(25.125m));

        Assert.Equal((25.125m, 25.125m, 1, StockAllocationStatus.Active),
            (allocation.Allocated.Value, allocation.Remaining.Value, allocation.Version, allocation.Status));
    }

    [Fact]
    public void Partial_consumption_advances_version_and_stays_active()
    {
        var allocation = Restore(remaining: 100m, version: 7);

        allocation.Consume(7, LabelId, Kilograms.Positive(40m));

        Assert.Equal(60m, allocation.Remaining.Value);
        Assert.Equal(8, allocation.Version);
        Assert.Equal(StockAllocationStatus.Active, allocation.Status);
    }

    [Fact]
    public void Exact_consumption_advances_version_and_closes_allocation()
    {
        var allocation = Restore(remaining: 100m, version: 7);

        allocation.Consume(7, LabelId, Kilograms.Positive(100m));

        Assert.Equal(0m, allocation.Remaining.Value);
        Assert.Equal(8, allocation.Version);
        Assert.Equal(StockAllocationStatus.Consumed, allocation.Status);
    }

    [Fact]
    public void Stale_version_has_no_effect()
    {
        var allocation = Restore(remaining: 100m, version: 7);

        AssertFailure(StockAllocationFailure.VersionConflict,
            () => allocation.Consume(6, LabelId, Kilograms.Positive(1m)));
        AssertUnchanged(allocation);
    }

    [Fact]
    public void Another_label_has_no_effect()
    {
        var allocation = Restore(remaining: 100m, version: 7);

        AssertFailure(StockAllocationFailure.LabelMismatch,
            () => allocation.Consume(7, Guid.NewGuid(), Kilograms.Positive(1m)));
        AssertUnchanged(allocation);
    }

    [Fact]
    public void Above_remaining_has_no_effect()
    {
        var allocation = Restore(remaining: 100m, version: 7);

        AssertFailure(StockAllocationFailure.Insufficient,
            () => allocation.Consume(7, LabelId, Kilograms.Positive(100.001m)));
        AssertUnchanged(allocation);
    }

    [Fact]
    public void Consumed_allocation_has_no_effect()
    {
        var allocation = Restore(remaining: 0m, version: 8, StockAllocationStatus.Consumed);

        AssertFailure(StockAllocationFailure.StateConflict,
            () => allocation.Consume(8, LabelId, Kilograms.Positive(1m)));
        Assert.Equal(0m, allocation.Remaining.Value);
        Assert.Equal(8, allocation.Version);
        Assert.Equal(StockAllocationStatus.Consumed, allocation.Status);
    }

    [Fact]
    public void Release_returns_every_remaining_kilogram_and_closes_without_consuming_stock()
    {
        var allocation = Restore(remaining: 60m, version: 8);

        var released = allocation.Release(8);

        Assert.Equal(60m, released.Value);
        Assert.Equal(0m, allocation.Remaining.Value);
        Assert.Equal(9, allocation.Version);
        Assert.Equal(StockAllocationStatus.Released, allocation.Status);
    }

    [Theory]
    [InlineData(7, StockAllocationStatus.Consumed, StockAllocationFailure.VersionConflict)]
    [InlineData(8, StockAllocationStatus.Consumed, StockAllocationFailure.StateConflict)]
    [InlineData(8, StockAllocationStatus.Released, StockAllocationFailure.StateConflict)]
    public void Release_rejects_stale_or_closed_state_without_effect(
        long expectedVersion,
        StockAllocationStatus status,
        StockAllocationFailure failure)
    {
        var allocation = Restore(remaining: 0m, version: 8, status);

        AssertFailure(failure, () => allocation.Release(expectedVersion));
        Assert.Equal((0m, 8, status), (allocation.Remaining.Value, allocation.Version, allocation.Status));
    }

    [Theory]
    [InlineData(101, 1, StockAllocationStatus.Active)]
    [InlineData(0, 1, StockAllocationStatus.Active)]
    [InlineData(1, 1, StockAllocationStatus.Consumed)]
    [InlineData(1, 1, StockAllocationStatus.Released)]
    [InlineData(0, 0, StockAllocationStatus.Consumed)]
    public void Inconsistent_stored_state_is_rejected(decimal remaining, long version, StockAllocationStatus status)
    {
        Assert.Throws<ArgumentException>(() => StockAllocation.Restore(AllocationId, Position(),
            Kilograms.Positive(100m), Kilograms.Of(remaining), version, status));
    }

    [Theory]
    [InlineData(100, 0, 100)]
    [InlineData(100, 60, 40)]
    [InlineData(100, 99.999, 0.001)]
    [InlineData(100, 100, 0)]
    [InlineData(30, 45.5, 0)]
    [InlineData(0, 0, 0)]
    public void Unreserved_is_the_free_position_less_its_active_remainders_and_never_below_zero(
        decimal freePhysical, decimal activelyAllocated, decimal expected)
    {
        var unreserved = StockAllocation.Unreserved(Kilograms.Of(freePhysical), Kilograms.Of(activelyAllocated));

        Assert.Equal(expected, unreserved.Value);
    }

    [Fact]
    public void Another_consumer_cannot_take_reserved_kilograms_but_a_release_makes_them_unreserved_again()
    {
        // A 100 kg Free label, 60 kg of it held by an active Sales allocation: another consumer fits in 40 kg only.
        var allocation = Restore(remaining: 60m, version: 1);
        var free = Kilograms.Of(100m);

        Assert.True(Kilograms.Positive(40m) <= StockAllocation.Unreserved(free, allocation.Remaining));
        Assert.False(Kilograms.Positive(40.001m) <= StockAllocation.Unreserved(free, allocation.Remaining));

        allocation.Release(1);

        Assert.Equal(100m, StockAllocation.Unreserved(free, allocation.Remaining).Value);
    }

    [Fact]
    public void Resize_down_keeps_the_identity_and_the_consumed_part_and_frees_the_difference()
    {
        // 100 kg allocated, 40 consumed, 60 still reserved; the demand now needs only 25 more.
        var allocation = Restore(remaining: 60m, version: 3);

        var difference = allocation.Resize(3, Kilograms.Positive(25m));

        Assert.Equal((-35m, 65m, 25m, 4L, StockAllocationStatus.Active),
            (difference, allocation.Allocated.Value, allocation.Remaining.Value, allocation.Version, allocation.Status));
        Assert.Equal(AllocationId, allocation.Id);
        Assert.Equal(40m, allocation.Allocated.Value - allocation.Remaining.Value);
    }

    [Fact]
    public void Resize_up_restates_the_allocated_total_and_keeps_the_consumed_part()
    {
        var allocation = Restore(remaining: 60m, version: 3);

        var difference = allocation.Resize(3, Kilograms.Positive(75.5m));

        Assert.Equal((15.5m, 115.5m, 75.5m, 4L),
            (difference, allocation.Allocated.Value, allocation.Remaining.Value, allocation.Version));
        Assert.Equal(40m, allocation.Allocated.Value - allocation.Remaining.Value);
    }

    [Fact]
    public void Resize_to_the_same_quantity_is_no_change_and_has_no_effect()
    {
        var allocation = Restore(remaining: 100m, version: 7);

        AssertFailure(StockAllocationFailure.Unchanged, () => allocation.Resize(7, Kilograms.Positive(100m)));
        AssertUnchanged(allocation);
    }

    [Fact]
    public void Resize_to_zero_is_a_release_not_a_change()
    {
        var allocation = Restore(remaining: 100m, version: 7);

        AssertFailure(StockAllocationFailure.Insufficient, () => allocation.Resize(7, Kilograms.Zero));
        AssertUnchanged(allocation);
    }

    [Theory]
    [InlineData(6, StockAllocationStatus.Active, StockAllocationFailure.VersionConflict)]
    [InlineData(8, StockAllocationStatus.Consumed, StockAllocationFailure.StateConflict)]
    [InlineData(8, StockAllocationStatus.Released, StockAllocationFailure.StateConflict)]
    public void Resize_rejects_a_stale_or_closed_allocation_without_effect(
        long expectedVersion,
        StockAllocationStatus status,
        StockAllocationFailure failure)
    {
        var remaining = status == StockAllocationStatus.Active ? 100m : 0m;
        var version = status == StockAllocationStatus.Active ? 7 : 8;
        var allocation = Restore(remaining, version, status);

        AssertFailure(failure, () => allocation.Resize(expectedVersion, Kilograms.Positive(5m)));
        Assert.Equal((remaining, (long)version, status), (allocation.Remaining.Value, allocation.Version, allocation.Status));
    }

    private static StockAllocation Restore(decimal remaining, long version,
        StockAllocationStatus status = StockAllocationStatus.Active) =>
        StockAllocation.Restore(AllocationId, Position(), Kilograms.Positive(100m), Kilograms.Of(remaining), version, status);

    private static StockPosition Position()
    {
        var label = new MaterialLabel(LabelId, Company, "SYN-ALLOCATION", Guid.NewGuid(), Guid.NewGuid());
        return new StockPosition(Company, new StockOwner(StockPartyKind.Company, Company),
            new StockCustodian(StockPartyKind.Company, Company), Guid.NewGuid(), label, BlockState.Free);
    }

    private static void AssertFailure(StockAllocationFailure failure, Action action) =>
        Assert.Equal(failure, Assert.Throws<StockAllocationException>(action).Failure);

    private static void AssertUnchanged(StockAllocation allocation)
    {
        Assert.Equal(100m, allocation.Remaining.Value);
        Assert.Equal(7, allocation.Version);
        Assert.Equal(StockAllocationStatus.Active, allocation.Status);
    }
}
