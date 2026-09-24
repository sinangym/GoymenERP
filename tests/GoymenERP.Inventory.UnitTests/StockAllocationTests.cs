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

    [Theory]
    [InlineData(101, 1, StockAllocationStatus.Active)]
    [InlineData(0, 1, StockAllocationStatus.Active)]
    [InlineData(1, 1, StockAllocationStatus.Consumed)]
    [InlineData(0, 0, StockAllocationStatus.Consumed)]
    public void Inconsistent_stored_state_is_rejected(decimal remaining, long version, StockAllocationStatus status)
    {
        Assert.Throws<ArgumentException>(() => StockAllocation.Restore(AllocationId, Position(),
            Kilograms.Positive(100m), Kilograms.Of(remaining), version, status));
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
