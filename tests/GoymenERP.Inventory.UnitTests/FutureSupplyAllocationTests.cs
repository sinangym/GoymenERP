using GoymenERP.Modules.Inventory.Domain.FutureSupply;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// F-017 F017-b (STOK_F017 §3.2-§3.4, §4.1/§4.2): the future supply allocation, its ledger and its exact quantity, without a
/// database. What SQL Server itself enforces (checks, the ledger total, races) is proven by the relational acceptance tests.
/// </summary>
public sealed class FutureSupplyAllocationTests
{
    private static readonly Guid Company = Guid.Parse("8a170000-0000-0000-0000-000000000001");
    private static readonly Guid AllocationId = Guid.Parse("8a170000-0000-0000-0000-000000000002");
    private static readonly Guid PurchaseLineId = Guid.Parse("8a170000-0000-0000-0000-000000000003");

    [Fact]
    public void The_stored_status_numbers_never_change_meaning()
    {
        Assert.Equal((1, 2, 3), ((int)FutureSupplyAllocationStatus.Active, (int)FutureSupplyAllocationStatus.Consumed,
            (int)FutureSupplyAllocationStatus.Released));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("12.5")]
    [InlineData("12.125")]
    [InlineData("12.1250000")]
    public void A_quantity_keeps_up_to_three_decimals_exactly_and_drops_trailing_zeros(string text)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        var quantity = FutureSupplyQuantity.Of(value);

        Assert.Equal(value, quantity.Value);
        Assert.DoesNotContain(",", quantity.ToString(), StringComparison.Ordinal);
        Assert.False(quantity.ToString().Contains('.', StringComparison.Ordinal) && quantity.ToString().EndsWith('0'));
    }

    [Fact]
    public void A_quantity_with_a_fourth_decimal_is_refused_never_rounded_and_a_negative_one_is_refused()
    {
        var precision = Assert.Throws<FutureSupplyRuleException>(() => FutureSupplyQuantity.Of(1.0005m));
        var negative = Assert.Throws<FutureSupplyRuleException>(() => FutureSupplyQuantity.Of(-0.001m));
        var zero = Assert.Throws<FutureSupplyRuleException>(() => FutureSupplyQuantity.Positive(0m));

        Assert.Equal(
            (FutureSupplyRuleFailure.PrecisionExceeded, FutureSupplyRuleFailure.QuantityNotPositive, FutureSupplyRuleFailure.QuantityNotPositive),
            (precision.Failure, negative.Failure, zero.Failure));
    }

    [Fact]
    public void A_quantity_difference_below_zero_is_refused()
    {
        var exception = Assert.Throws<FutureSupplyRuleException>(
            () => FutureSupplyQuantity.Of(1m) - FutureSupplyQuantity.Of(1.001m));

        Assert.Equal(FutureSupplyRuleFailure.QuantityNotPositive, exception.Failure);
    }

    [Fact]
    public void Create_starts_active_at_version_one_with_the_whole_quantity_remaining()
    {
        var allocation = FutureSupplyAllocation.Create(AllocationId, Company, FutureSupplyQuantity.Positive(40.125m));

        Assert.Equal((40.125m, 40.125m, 0m, 0m, FutureSupplyAllocationStatus.Active, 1L),
            (allocation.Allocated.Value, allocation.Remaining.Value, allocation.Released.Value, allocation.Consumed.Value,
                allocation.Status, allocation.Version));
    }

    [Fact]
    public void Create_refuses_a_zero_quantity_an_empty_id_and_an_empty_company()
    {
        Assert.Equal(FutureSupplyRuleFailure.QuantityNotPositive,
            Assert.Throws<FutureSupplyRuleException>(
                () => FutureSupplyAllocation.Create(AllocationId, Company, FutureSupplyQuantity.Zero)).Failure);
        Assert.Throws<ArgumentException>(() => FutureSupplyAllocation.Create(Guid.Empty, Company, FutureSupplyQuantity.Positive(1m)));
        Assert.Throws<ArgumentException>(() => FutureSupplyAllocation.Create(AllocationId, Guid.Empty, FutureSupplyQuantity.Positive(1m)));
    }

    [Fact]
    public void A_partial_release_keeps_the_allocation_active_and_steps_the_version_by_one()
    {
        var allocation = Restore(allocated: 100m, remaining: 100m, released: 0m, FutureSupplyAllocationStatus.Active, version: 4);

        var released = allocation.Release(4, FutureSupplyQuantity.Positive(30.5m));

        Assert.Equal((30.5m, 69.5m, 30.5m, FutureSupplyAllocationStatus.Active, 5L),
            (released.Value, allocation.Remaining.Value, allocation.Released.Value, allocation.Status, allocation.Version));
    }

    [Fact]
    public void A_full_release_without_a_quantity_releases_all_that_remains_and_ends_Released()
    {
        var allocation = Restore(allocated: 100m, remaining: 60m, released: 40m, FutureSupplyAllocationStatus.Active, version: 2);

        var released = allocation.Release(2, null);

        Assert.Equal((60m, 0m, 100m, 0m, FutureSupplyAllocationStatus.Released, 3L),
            (released.Value, allocation.Remaining.Value, allocation.Released.Value, allocation.Consumed.Value, allocation.Status,
                allocation.Version));
    }

    [Fact]
    public void Releasing_the_rest_of_a_partly_consumed_allocation_ends_Consumed()
    {
        var allocation = Restore(allocated: 100m, remaining: 20m, released: 30m, FutureSupplyAllocationStatus.Active, version: 6);

        allocation.Release(6, null);

        Assert.Equal((50m, FutureSupplyAllocationStatus.Consumed), (allocation.Consumed.Value, allocation.Status));
    }

    [Fact]
    public void A_release_above_the_remaining_quantity_is_refused_and_nothing_changes()
    {
        var allocation = Restore(allocated: 100m, remaining: 10m, released: 90m, FutureSupplyAllocationStatus.Active, version: 3);

        var exception = Assert.Throws<FutureSupplyRuleException>(() => allocation.Release(3, FutureSupplyQuantity.Positive(10.001m)));

        Assert.Equal(FutureSupplyRuleFailure.ReleaseExceedsRemaining, exception.Failure);
        Assert.Equal((10m, 90m, 3L), (allocation.Remaining.Value, allocation.Released.Value, allocation.Version));
    }

    [Fact]
    public void A_stale_version_is_a_version_conflict_and_a_closed_allocation_is_a_state_conflict()
    {
        var active = Restore(allocated: 10m, remaining: 10m, released: 0m, FutureSupplyAllocationStatus.Active, version: 2);
        var released = Restore(allocated: 10m, remaining: 0m, released: 10m, FutureSupplyAllocationStatus.Released, version: 3);

        Assert.Equal(FutureSupplyRuleFailure.VersionConflict,
            Assert.Throws<FutureSupplyRuleException>(() => active.Release(1, null)).Failure);
        Assert.Equal(FutureSupplyRuleFailure.StateConflict,
            Assert.Throws<FutureSupplyRuleException>(() => released.Release(3, null)).Failure);
        Assert.Equal(2L, active.Version);
    }

    [Theory]
    [InlineData(10, 0, 0, FutureSupplyAllocationStatus.Active)]
    [InlineData(10, 0, 10, FutureSupplyAllocationStatus.Consumed)]
    [InlineData(10, 5, 5, FutureSupplyAllocationStatus.Released)]
    [InlineData(10, 8, 5, FutureSupplyAllocationStatus.Active)]
    public void A_stored_row_that_breaks_the_status_rule_is_refused(int allocated, int remaining, int released,
        FutureSupplyAllocationStatus status)
    {
        Assert.Throws<ArgumentException>(() => Restore(allocated, remaining, released, status, version: 1));
    }

    [Fact]
    public void A_reason_is_required_and_a_text_longer_than_the_column_is_refused_not_cut()
    {
        Assert.Equal("Müşteri iptali", FutureSupplyAllocation.RequireReason("Müşteri iptali"));
        Assert.Equal(FutureSupplyRuleFailure.InvalidReason,
            Assert.Throws<FutureSupplyRuleException>(() => FutureSupplyAllocation.RequireReason("   ")).Failure);
        Assert.Equal(FutureSupplyRuleFailure.InvalidReason,
            Assert.Throws<FutureSupplyRuleException>(
                () => FutureSupplyAllocation.RequireReason(new string('x', FutureSupplyAllocation.MaximumReasonLength + 1))).Failure);
    }

    [Fact]
    public void The_ledger_reserves_up_to_the_allocatable_amount_read_under_the_lock_and_writes_the_snapshot_after_the_check()
    {
        var ledger = FutureSupplyLedger.Empty(Company, PurchaseLineId);

        ledger.Reserve(FutureSupplyQuantity.Of(100m), FutureSupplyQuantity.Positive(60m));
        ledger.Reserve(FutureSupplyQuantity.Of(100m), FutureSupplyQuantity.Positive(40m));

        Assert.Equal((100m, 100m, 2L), (ledger.ActiveAllocated.Value, ledger.AllocatableSnapshot.Value, ledger.Version));
    }

    [Fact]
    public void The_ledger_refuses_a_reservation_above_the_allocatable_amount_and_keeps_its_snapshot()
    {
        var ledger = FutureSupplyLedger.Restore(Company, PurchaseLineId, FutureSupplyQuantity.Of(100m), FutureSupplyQuantity.Of(90m), 5);

        var exception = Assert.Throws<FutureSupplyRuleException>(
            () => ledger.Reserve(FutureSupplyQuantity.Of(95m), FutureSupplyQuantity.Positive(5.001m)));

        Assert.Equal(FutureSupplyRuleFailure.InsufficientFutureSupply, exception.Failure);
        Assert.Equal((90m, 100m, 5L), (ledger.ActiveAllocated.Value, ledger.AllocatableSnapshot.Value, ledger.Version));
    }

    [Fact]
    public void A_ledger_return_lowers_the_active_total_without_touching_the_snapshot()
    {
        var ledger = FutureSupplyLedger.Restore(Company, PurchaseLineId, FutureSupplyQuantity.Of(100m), FutureSupplyQuantity.Of(90m), 5);

        ledger.Return(FutureSupplyQuantity.Positive(30m));

        Assert.Equal((60m, 100m, 6L), (ledger.ActiveAllocated.Value, ledger.AllocatableSnapshot.Value, ledger.Version));
    }

    [Fact]
    public void A_ledger_return_above_the_active_total_or_on_an_unwritten_ledger_is_refused()
    {
        var stored = FutureSupplyLedger.Restore(Company, PurchaseLineId, FutureSupplyQuantity.Of(100m), FutureSupplyQuantity.Of(10m), 2);
        var empty = FutureSupplyLedger.Empty(Company, PurchaseLineId);

        Assert.Equal(FutureSupplyRuleFailure.StateConflict,
            Assert.Throws<FutureSupplyRuleException>(() => stored.Return(FutureSupplyQuantity.Positive(10.001m))).Failure);
        Assert.Equal(FutureSupplyRuleFailure.StateConflict,
            Assert.Throws<FutureSupplyRuleException>(() => empty.Return(FutureSupplyQuantity.Positive(1m))).Failure);
        Assert.Equal(10m, stored.ActiveAllocated.Value);
    }

    [Fact]
    public void A_stored_ledger_above_its_snapshot_or_without_a_version_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
            FutureSupplyLedger.Restore(Company, PurchaseLineId, FutureSupplyQuantity.Of(10m), FutureSupplyQuantity.Of(10.001m), 1));
        Assert.Throws<ArgumentException>(() =>
            FutureSupplyLedger.Restore(Company, PurchaseLineId, FutureSupplyQuantity.Of(10m), FutureSupplyQuantity.Of(1m), 0));
    }

    private static FutureSupplyAllocation Restore(decimal allocated, decimal remaining, decimal released,
        FutureSupplyAllocationStatus status, long version) =>
        FutureSupplyAllocation.Restore(AllocationId, Company, FutureSupplyQuantity.Of(allocated), FutureSupplyQuantity.Of(remaining),
            FutureSupplyQuantity.Of(released), status, version);
}
