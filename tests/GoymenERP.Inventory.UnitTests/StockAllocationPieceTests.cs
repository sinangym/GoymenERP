using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>KLN-D119 / SAT-D434 (AK-4b): the pieces a physical allocation reserves, gives up and frees with its kilograms.</summary>
public sealed class StockAllocationPieceTests
{
    private static readonly Guid Company = Guid.Parse("89100000-0000-0000-0000-000000000001");
    private static readonly Guid AllocationId = Guid.Parse("89100000-0000-0000-0000-000000000002");
    private static readonly Guid LabelId = Guid.Parse("89100000-0000-0000-0000-000000000003");

    [Fact]
    public void Create_with_pieces_reserves_them_and_create_without_pieces_holds_none()
    {
        var counted = StockAllocation.Create(AllocationId, Position(), Kilograms.Positive(123.450m), PieceCount.Of(10));
        var plain = StockAllocation.Create(AllocationId, Position(), Kilograms.Positive(123.450m));

        Assert.Equal((10, 10, true), (counted.AllocatedPieces!.Value.Value, counted.RemainingPieces!.Value.Value, counted.TracksPieces));
        Assert.Equal((null, null, false), (plain.AllocatedPieces, plain.RemainingPieces, plain.TracksPieces));
        AssertFailure(StockAllocationFailure.PiecesInvalid,
            () => StockAllocation.Create(AllocationId, Position(), Kilograms.Positive(1m), PieceCount.Zero));
    }

    [Fact]
    public void A_partial_consumption_takes_its_pieces_and_leaves_the_rest_reserved()
    {
        var allocation = Restore(remainingKg: 100m, allocatedPieces: 10, remainingPieces: 10);

        allocation.Consume(7, LabelId, Kilograms.Positive(40m), PieceCount.Of(4));

        Assert.Equal((60m, 6, 10, StockAllocationStatus.Active),
            (allocation.Remaining.Value, allocation.RemainingPieces!.Value.Value, allocation.AllocatedPieces!.Value.Value, allocation.Status));
    }

    [Fact]
    public void The_last_kilograms_take_the_last_pieces_and_close_the_allocation()
    {
        var allocation = Restore(remainingKg: 100m, allocatedPieces: 10, remainingPieces: 10);

        allocation.Consume(7, LabelId, Kilograms.Positive(100m), PieceCount.Of(10));

        Assert.Equal((0m, 0, StockAllocationStatus.Consumed),
            (allocation.Remaining.Value, allocation.RemainingPieces!.Value.Value, allocation.Status));
    }

    [Theory]
    [InlineData(40, null, StockAllocationFailure.PiecesRequired)]
    [InlineData(40, 11, StockAllocationFailure.PiecesInsufficient)]
    [InlineData(40, 0, StockAllocationFailure.PiecesInvalid)]
    // Kilograms and pieces run out together: every piece with kilograms left, or every kilogram with pieces left.
    [InlineData(40, 10, StockAllocationFailure.PiecesInvalid)]
    [InlineData(100, 9, StockAllocationFailure.PiecesInvalid)]
    public void A_consumption_whose_pieces_are_missing_too_many_or_out_of_step_changes_nothing(
        int kilograms, int? pieces, StockAllocationFailure failure)
    {
        var allocation = Restore(remainingKg: 100m, allocatedPieces: 10, remainingPieces: 10);

        AssertFailure(failure, () => allocation.Consume(7, LabelId, Kilograms.Positive(kilograms),
            pieces is { } count ? PieceCount.Of(count) : null));

        Assert.Equal((100m, 10, 7L), (allocation.Remaining.Value, allocation.RemainingPieces!.Value.Value, allocation.Version));
    }

    [Fact]
    public void An_allocation_without_pieces_is_consumed_exactly_as_before_whatever_pieces_the_shipment_names()
    {
        var silent = Restore(remainingKg: 100m);
        var named = Restore(remainingKg: 100m);

        silent.Consume(7, LabelId, Kilograms.Positive(40m));
        named.Consume(7, LabelId, Kilograms.Positive(40m), PieceCount.Of(4));

        Assert.Equal((60m, null, 8L), (silent.Remaining.Value, silent.RemainingPieces, silent.Version));
        Assert.Equal((60m, null, 8L), (named.Remaining.Value, named.RemainingPieces, named.Version));
    }

    [Fact]
    public void Release_frees_every_remaining_piece_and_keeps_the_reserved_count()
    {
        var allocation = Restore(remainingKg: 60m, allocatedPieces: 10, remainingPieces: 6);

        var released = allocation.Release(7);

        Assert.Equal((60m, 0m, 0, 10, StockAllocationStatus.Released),
            (released.Value, allocation.Remaining.Value, allocation.RemainingPieces!.Value.Value,
                allocation.AllocatedPieces!.Value.Value, allocation.Status));
    }

    [Fact]
    public void Resize_restates_the_held_pieces_and_keeps_the_consumed_ones()
    {
        // 10 reserved, 4 already shipped: 6 held. Holding 3 afterwards keeps the 4 consumed → 7 reserved in total.
        var allocation = Restore(remainingKg: 60m, allocatedPieces: 10, remainingPieces: 6);

        var difference = allocation.Resize(7, Kilograms.Positive(30m), PieceCount.Of(3));

        Assert.Equal((-30m, 30m, 70m, 3, 7, 8L),
            (difference, allocation.Remaining.Value, allocation.Allocated.Value, allocation.RemainingPieces!.Value.Value,
                allocation.AllocatedPieces!.Value.Value, allocation.Version));
    }

    [Fact]
    public void Resize_of_only_the_pieces_is_a_change_and_the_same_kilograms_and_pieces_is_none()
    {
        var allocation = Restore(remainingKg: 60m, allocatedPieces: 10, remainingPieces: 6);

        AssertFailure(StockAllocationFailure.Unchanged, () => allocation.Resize(7, Kilograms.Positive(60m), PieceCount.Of(6)));
        allocation.Resize(7, Kilograms.Positive(60m), PieceCount.Of(5));

        Assert.Equal((60m, 5, 9), (allocation.Remaining.Value, allocation.RemainingPieces!.Value.Value, allocation.AllocatedPieces!.Value.Value));
    }

    [Fact]
    public void Resize_requires_pieces_on_a_counted_allocation_and_refuses_them_on_a_plain_one()
    {
        var counted = Restore(remainingKg: 60m, allocatedPieces: 10, remainingPieces: 6);
        var plain = Restore(remainingKg: 60m);

        AssertFailure(StockAllocationFailure.PiecesRequired, () => counted.Resize(7, Kilograms.Positive(30m)));
        AssertFailure(StockAllocationFailure.PiecesInvalid, () => counted.Resize(7, Kilograms.Positive(30m), PieceCount.Zero));
        AssertFailure(StockAllocationFailure.PiecesNotTracked, () => plain.Resize(7, Kilograms.Positive(30m), PieceCount.Of(3)));
        Assert.Equal((60m, 7L, 60m, 7L), (counted.Remaining.Value, counted.Version, plain.Remaining.Value, plain.Version));
    }

    [Theory]
    // position pieces, active pieces, every active holds pieces, unreserved kg, requested kg, requested pieces
    [InlineData(10, 0, true, 100, 40, 4, 4)]
    [InlineData(10, 6, true, 40, 40, 4, 4)]
    // A piece-less active allocation hides how many pieces its kilograms take: only the bound is checked.
    [InlineData(10, 0, false, 60, 60, 4, 4)]
    public void A_reservation_of_a_counted_position_takes_the_requested_pieces(
        int positionPieces, int activePieces, bool everyActiveHolds, int unreservedKg, int kilograms, int requested, int expected)
    {
        var pieces = StockAllocation.ReservablePieces(Kilograms.Positive(kilograms), requested, Kilograms.Of(unreservedKg),
            PieceCount.Of(positionPieces), PieceCount.Of(activePieces), everyActiveHolds);

        Assert.Equal(expected, pieces!.Value.Value);
    }

    [Theory]
    [InlineData(10, 0, 100, 40, null, StockAllocationFailure.PiecesRequired)]
    [InlineData(10, 0, 100, 40, 0, StockAllocationFailure.PiecesInvalid)]
    [InlineData(10, 6, 40, 40, 5, StockAllocationFailure.PiecesInsufficient)]
    // The unreserved kilograms and pieces run out together.
    [InlineData(10, 0, 100, 40, 10, StockAllocationFailure.PiecesInvalid)]
    [InlineData(10, 0, 100, 100, 9, StockAllocationFailure.PiecesInvalid)]
    public void A_reservation_whose_pieces_are_missing_too_many_or_out_of_step_is_refused(
        int positionPieces, int activePieces, int unreservedKg, int kilograms, int? requested, StockAllocationFailure failure) =>
        AssertFailure(failure, () => StockAllocation.ReservablePieces(Kilograms.Positive(kilograms), requested,
            Kilograms.Of(unreservedKg), PieceCount.Of(positionPieces), PieceCount.Of(activePieces), true));

    [Fact]
    public void A_kg_only_position_reserves_no_pieces_and_refuses_a_piece_count()
    {
        Assert.Null(StockAllocation.ReservablePieces(Kilograms.Positive(40m), null, Kilograms.Of(100m), null, PieceCount.Zero, true));
        AssertFailure(StockAllocationFailure.PiecesNotTracked, () => StockAllocation.ReservablePieces(
            Kilograms.Positive(40m), 4, Kilograms.Of(100m), null, PieceCount.Zero, true));
    }

    [Theory]
    [InlineData(10, null)]
    [InlineData(null, 10)]
    [InlineData(0, 0)]
    [InlineData(5, 6)]
    public void A_stored_allocation_with_half_or_impossible_pieces_is_not_restored(int? allocated, int? remaining) =>
        Assert.Throws<ArgumentException>(() => StockAllocation.Restore(AllocationId, Position(), Kilograms.Positive(100m),
            Kilograms.Of(100m), 7, StockAllocationStatus.Active,
            allocated is { } a ? PieceCount.Of(a) : null, remaining is { } r ? PieceCount.Of(r) : null));

    private static StockAllocation Restore(decimal remainingKg, int? allocatedPieces = null, int? remainingPieces = null) =>
        StockAllocation.Restore(AllocationId, Position(), Kilograms.Positive(100m), Kilograms.Of(remainingKg), 7,
            StockAllocationStatus.Active,
            allocatedPieces is { } allocated ? PieceCount.Of(allocated) : null,
            remainingPieces is { } remaining ? PieceCount.Of(remaining) : null);

    private static StockPosition Position()
    {
        var label = new MaterialLabel(LabelId, Company, "SYN-ALLOCATION-PIECES", Guid.NewGuid(), Guid.NewGuid());
        return new StockPosition(Company, new StockOwner(StockPartyKind.Company, Company),
            new StockCustodian(StockPartyKind.Company, Company), Guid.NewGuid(), label, BlockState.Free);
    }

    private static void AssertFailure(StockAllocationFailure failure, Action action) =>
        Assert.Equal(failure, Assert.Throws<StockAllocationException>(action).Failure);
}
