using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>KLN-D119 (AK-2): a piece-tracking label carries pieces next to its kilograms in every movement.</summary>
public sealed partial class StockLedgerTests
{
    [Fact]
    public void A_piece_receipt_then_issues_keep_pieces_and_kilograms_together_and_the_last_piece_empties_the_label()
    {
        var ledger = new StockLedger();
        ledger.Apply(Movement(StockMovementKind.Receipt, InPieces(Position(), 314m, 10)));

        ledger.Apply(Movement(StockMovementKind.Issue, OutPieces(Position(), 94.2m, 3)));

        Assert.Equal((219.8m, (PieceCount?)PieceCount.Of(7)), (ledger.QuantityAt(Position()).Value, ledger.PiecesAt(Position())));
        Assert.True(ledger.TracksPieces(Label1));
        Assert.Equal(PieceCount.Of(7), Assert.Single(ledger.Balances).Pieces);

        ledger.Apply(Movement(StockMovementKind.Issue, OutPieces(Position(), 219.8m, 7)));

        Assert.Equal((0m, (PieceCount?)PieceCount.Zero), (ledger.QuantityAt(Position()).Value, ledger.PiecesAt(Position())));
        Assert.False(ledger.TracksPieces(Label1));
    }

    [Fact]
    public void A_movement_that_does_not_know_the_count_of_a_piece_tracking_label_is_refused_whole()
    {
        var ledger = new StockLedger();
        ledger.Apply(Movement(StockMovementKind.Receipt, InPieces(Position(), 100m, 4)));

        var error = Assert.Throws<InventoryRuleException>(() => ledger.Apply(Issue(Position(), 25m)));

        Assert.Equal(InventoryRuleCodes.PieceCountRequired, error.Code);
        Assert.Equal((100m, (PieceCount?)PieceCount.Of(4)), (ledger.QuantityAt(Position()).Value, ledger.PiecesAt(Position())));
        Assert.Single(ledger.AppliedMovements);
    }

    [Fact]
    public void A_label_stocked_without_pieces_stays_without_pieces_and_is_never_backfilled()
    {
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));

        Assert.Equal(InventoryRuleCodes.PieceCountNotTracked, Assert.Throws<InventoryRuleException>(
            () => ledger.Apply(Movement(StockMovementKind.Issue, OutPieces(Position(), 10m, 1)))).Code);
        ledger.Apply(Issue(Position(), 10m));

        Assert.Null(ledger.PiecesAt(Position()));
        Assert.False(ledger.TracksPieces(Label1));
        Assert.Null(Assert.Single(ledger.Balances).Pieces);
    }

    [Fact]
    public void Pieces_never_go_below_zero_and_never_leave_kilograms_without_pieces_or_pieces_without_kilograms()
    {
        var ledger = new StockLedger();
        ledger.Apply(Movement(StockMovementKind.Receipt, InPieces(Position(), 100m, 4)));

        Assert.Equal(InventoryRuleCodes.NegativePieceCount, Assert.Throws<InventoryRuleException>(
            () => ledger.Apply(Movement(StockMovementKind.Issue, OutPieces(Position(), 10m, 5)))).Code);
        Assert.Equal(InventoryRuleCodes.InvalidPieceCount, Assert.Throws<InventoryRuleException>(
            () => ledger.Apply(Movement(StockMovementKind.Issue, OutPieces(Position(), 10m, 4)))).Code);
        Assert.Equal(InventoryRuleCodes.InvalidPieceCount, Assert.Throws<InventoryRuleException>(
            () => ledger.Apply(Movement(StockMovementKind.Issue, OutPieces(Position(), 100m, 3)))).Code);
        Assert.Equal(InventoryRuleCodes.InvalidPieceCount, Assert.Throws<InventoryRuleException>(
            () => new StockLedger().Apply(Movement(StockMovementKind.Receipt, InPieces(Position(), 10m, 0)))).Code);
        Assert.Equal((100m, (PieceCount?)PieceCount.Of(4)), (ledger.QuantityAt(Position()).Value, ledger.PiecesAt(Position())));
    }

    [Fact]
    public void A_count_adjustment_of_kilograms_only_carries_zero_pieces_on_a_piece_tracking_label()
    {
        var ledger = new StockLedger();
        ledger.Apply(Movement(StockMovementKind.Receipt, InPieces(Position(), 100m, 4)));

        ledger.Apply(Movement(StockMovementKind.CountAdjustment, InPieces(Position(), 0.5m, 0)));
        ledger.Apply(Movement(StockMovementKind.CountAdjustment, OutPieces(Position(), 25.5m, 1)));

        Assert.Equal((75m, (PieceCount?)PieceCount.Of(3)), (ledger.QuantityAt(Position()).Value, ledger.PiecesAt(Position())));
    }

    [Fact]
    public void Blocking_part_of_a_piece_tracking_label_moves_its_pieces_and_the_pair_must_agree()
    {
        var ledger = new StockLedger();
        var blocked = Position(block: BlockState.Blocked);
        ledger.Apply(Movement(StockMovementKind.Receipt, InPieces(Position(), 100m, 4)));

        ledger.Apply(Movement(StockMovementKind.Block, OutPieces(Position(), 25m, 1), InPieces(blocked, 25m, 1)));

        Assert.Equal((PieceCount?)PieceCount.Of(3), ledger.PiecesAt(Position()));
        Assert.Equal((PieceCount?)PieceCount.Of(1), ledger.PiecesAt(blocked));
        Assert.Equal(InventoryRuleCodes.InvalidMovement, Assert.Throws<InventoryRuleException>(() =>
            Movement(StockMovementKind.Block, OutPieces(Position(), 25m, 1), InPieces(blocked, 25m, 2))).Code);
        Assert.Equal(InventoryRuleCodes.InvalidMovement, Assert.Throws<InventoryRuleException>(() =>
            Movement(StockMovementKind.Block, OutPieces(Position(), 25m, 1), In(blocked, 25m))).Code);
    }

    [Fact]
    public void An_emptied_piece_tracking_label_may_arrive_again_without_pieces_and_drops_its_stale_count()
    {
        var ledger = new StockLedger();
        ledger.Apply(Movement(StockMovementKind.Receipt, InPieces(Position(), 10m, 1)));
        ledger.Apply(Movement(StockMovementKind.Issue, OutPieces(Position(), 10m, 1)));

        ledger.Apply(Receipt(Position(), 20m));

        Assert.Null(ledger.PiecesAt(Position()));
        ledger.Apply(Issue(Position(), 5m));
        Assert.Equal(15m, ledger.QuantityAt(Position()).Value);
    }

    [Fact]
    public void Stored_balances_must_agree_on_pieces_for_one_label_and_keep_pieces_and_kilograms_together()
    {
        var blocked = Position(block: BlockState.Blocked);

        Assert.Equal(InventoryRuleCodes.InvalidPieceCount, Assert.Throws<InventoryRuleException>(() => StockLedger.FromStoredBalances(
            [new StockBalance(Position(), Kilograms.Of(10m), PieceCount.Of(1)), new StockBalance(blocked, Kilograms.Of(5m))])).Code);
        Assert.Equal(InventoryRuleCodes.InvalidPieceCount, Assert.Throws<InventoryRuleException>(() => StockLedger.FromStoredBalances(
            [new StockBalance(Position(), Kilograms.Of(10m), PieceCount.Zero)])).Code);
        var ledger = StockLedger.FromStoredBalances(
            [new StockBalance(Position(), Kilograms.Of(10m), PieceCount.Of(2)), new StockBalance(blocked, Kilograms.Zero, PieceCount.Zero)]);
        Assert.True(ledger.TracksPieces(Label1));
        Assert.Equal(InventoryRuleCodes.PieceCountRequired,
            Assert.Throws<InventoryRuleException>(() => ledger.Apply(Issue(Position(), 1m))).Code);
    }

    [Fact]
    public void Lines_of_one_label_in_one_movement_carry_pieces_together_or_not_at_all()
    {
        var ledger = new StockLedger();
        var blocked = Position(block: BlockState.Blocked);

        Assert.Equal(InventoryRuleCodes.InvalidPieceCount, Assert.Throws<InventoryRuleException>(() =>
            ledger.Apply(Movement(StockMovementKind.Receipt, InPieces(Position(), 10m, 1), In(blocked, 5m)))).Code);
        Assert.Empty(ledger.Balances);
    }

    [Fact]
    public void A_piece_count_is_a_whole_non_negative_number()
    {
        Assert.Equal(InventoryRuleCodes.InvalidPieceCount, Assert.Throws<InventoryRuleException>(() => PieceCount.Of(-1)).Code);
        Assert.Equal(InventoryRuleCodes.InvalidPieceCount, Assert.Throws<InventoryRuleException>(() => PieceCount.Of(1.5m)).Code);
        Assert.Equal(InventoryRuleCodes.InvalidPieceCount, Assert.Throws<InventoryRuleException>(() => PieceCount.Positive(0)).Code);
        Assert.Equal(3, PieceCount.Of(3m).Value);
        Assert.Equal("5", (PieceCount.Of(2) + PieceCount.Of(3)).ToString());
    }

    private static StockMovementLine InPieces(StockPosition position, decimal kilograms, int pieces) =>
        new(position, StockDirection.In, Kilograms.Positive(kilograms), PieceCount.Of(pieces));

    private static StockMovementLine OutPieces(StockPosition position, decimal kilograms, int pieces) =>
        new(position, StockDirection.Out, Kilograms.Positive(kilograms), PieceCount.Of(pieces));
}
