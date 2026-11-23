using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Domain.Transfers;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>KLN-D119 (AK-2b): every transfer leg moves a piece-tracking label's pieces with its kilograms.</summary>
public sealed partial class InventoryTransferTests
{
    private const string PieceLineId = "71000000-0000-0000-0000-0000000000b1";
    private const string PieceLabelId = "81000000-0000-0000-0000-0000000000b1";

    [Fact]
    public void Dispatch_and_acceptance_move_the_whole_label_pieces_to_the_destination()
    {
        var (transfer, line, ledger) = DispatchedWithPieces(10m, 4);

        var acceptance = transfer.AcceptLine(2, line.LineId, At.AddHours(2), Document("incoming"));
        ledger.Apply(acceptance.Movement(Guid.NewGuid(), Company));

        var balance = Assert.Single(ledger.Balances);
        Assert.Equal((Destination, 10m, (PieceCount?)PieceCount.Of(4)),
            (balance.Position.LocationId, balance.Quantity.Value, balance.Pieces));
        Assert.Equal(PieceCount.Of(4), acceptance.Line.ActualPieces);
    }

    [Fact]
    public void A_piece_and_kilogram_shortage_fires_the_missing_pieces_out_of_the_destination()
    {
        var (transfer, line, ledger) = DispatchedWithPieces(10m, 4);

        var receipt = transfer.ReceiveLine(2, line.LineId, At.AddHours(2), Document("incoming"), Kilograms.Positive(7.5m),
            PieceCount.Of(3));
        ledger.Apply(receipt.Acceptance.Movement(Guid.NewGuid(), Company));
        var fire = receipt.Shortage!.Movement(Guid.NewGuid(), Company);
        ledger.Apply(fire);

        Assert.Equal(((PieceCount?)PieceCount.Of(4), (PieceCount?)PieceCount.Of(3), (PieceCount?)PieceCount.Of(1),
            (PieceCount?)PieceCount.Zero), (receipt.DispatchedPieces, receipt.ReceivedPieces, receipt.ShortagePieces,
            receipt.OveragePieces));
        Assert.Equal(PieceCount.Of(1), Assert.Single(fire.Lines).Pieces);
        var balance = Assert.Single(ledger.Balances);
        Assert.Equal((7.5m, (PieceCount?)PieceCount.Of(3)), (balance.Quantity.Value, balance.Pieces));
    }

    [Fact]
    public void A_kilogram_only_shortage_on_a_piece_tracking_label_fires_zero_pieces()
    {
        var (transfer, line, ledger) = DispatchedWithPieces(10m, 4);

        var receipt = transfer.ReceiveLine(2, line.LineId, At.AddHours(2), Document("incoming"), Kilograms.Positive(9.8m),
            PieceCount.Of(4));
        ledger.Apply(receipt.Acceptance.Movement(Guid.NewGuid(), Company));
        ledger.Apply(receipt.Shortage!.Movement(Guid.NewGuid(), Company));

        Assert.Equal(PieceCount.Zero, receipt.ShortagePieces);
        Assert.Equal((9.8m, (PieceCount?)PieceCount.Of(4)), (Assert.Single(ledger.Balances).Quantity.Value,
            Assert.Single(ledger.Balances).Pieces));
    }

    [Fact]
    public void A_piece_tracking_label_is_received_only_with_its_counted_pieces()
    {
        var (transfer, line, _) = DispatchedWithPieces(10m, 4);

        var refused = Assert.Throws<InventoryRuleException>(() => transfer.ReceiveLine(2, line.LineId, At.AddHours(2),
            Document("incoming"), Kilograms.Positive(10m)));

        Assert.Equal(InventoryRuleCodes.PieceCountRequired, refused.Code);
        Assert.Equal(2, transfer.Version);
    }

    [Fact]
    public void A_kilogram_only_label_is_never_received_with_pieces()
    {
        var line = Planned(PieceLineId, PieceLabelId, 10m);
        var transfer = Transfer(line);
        transfer.Dispatch(1, At.AddHours(1), Document("outgoing"), [Actual(line, Balance(line.LabelId, 10m))]);

        var refused = Assert.Throws<InventoryRuleException>(() => transfer.ReceiveLine(2, line.LineId, At.AddHours(2),
            Document("incoming"), Kilograms.Positive(10m), PieceCount.Of(4)));

        Assert.Equal(InventoryRuleCodes.PieceCountNotTracked, refused.Code);
    }

    [Theory]
    [InlineData(10, 3)]
    [InlineData(9, 5)]
    [InlineData(11, 3)]
    public void A_piece_difference_without_a_kilogram_difference_in_the_same_direction_is_refused(int receivedKg,
        int receivedPieces)
    {
        var (transfer, line, _) = DispatchedWithPieces(10m, 4);

        var refused = Assert.Throws<InventoryRuleException>(() => transfer.ReceiveLine(2, line.LineId, At.AddHours(2),
            Document("incoming"), Kilograms.Positive(receivedKg), PieceCount.Of(receivedPieces)));

        Assert.Equal(InventoryRuleCodes.InvalidPieceCount, refused.Code);
    }

    [Fact]
    public void Zero_received_pieces_are_refused()
    {
        var (transfer, line, _) = DispatchedWithPieces(10m, 4);

        var refused = Assert.Throws<InventoryRuleException>(() => transfer.ReceiveLine(2, line.LineId, At.AddHours(2),
            Document("incoming"), Kilograms.Positive(9m), PieceCount.Zero));

        Assert.Equal(InventoryRuleCodes.InvalidPieceCount, refused.Code);
    }

    [Fact]
    public void An_approved_excess_of_a_piece_tracking_label_enters_with_its_pieces_and_requires_them()
    {
        var (transfer, line, ledger) = DispatchedWithPieces(10m, 4);
        var receipt = transfer.ReceiveLine(2, line.LineId, At.AddHours(2), Document("incoming"), Kilograms.Positive(12.5m),
            PieceCount.Of(5));
        ledger.Apply(receipt.Acceptance.Movement(Guid.NewGuid(), Company));

        var missing = Assert.Throws<InventoryRuleException>(() =>
            transfer.ReceiptOverage(line.LineId, receipt.OverageQuantity, At.AddHours(3)));
        var overage = transfer.ReceiptOverage(line.LineId, receipt.OverageQuantity, At.AddHours(3), receipt.OveragePieces);
        ledger.Apply(overage.Movement(Guid.NewGuid(), Company));

        Assert.Equal(InventoryRuleCodes.PieceCountRequired, missing.Code);
        Assert.Equal((PieceCount?)PieceCount.Of(1), receipt.OveragePieces);
        Assert.Equal((12.5m, (PieceCount?)PieceCount.Of(5)), (Assert.Single(ledger.Balances).Quantity.Value,
            Assert.Single(ledger.Balances).Pieces));
    }

    [Fact]
    public void An_excess_of_a_kilogram_only_label_is_never_approved_with_pieces()
    {
        var line = Planned(PieceLineId, PieceLabelId, 10m);
        var transfer = Transfer(line);
        transfer.Dispatch(1, At.AddHours(1), Document("outgoing"), [Actual(line, Balance(line.LabelId, 10m))]);
        var receipt = transfer.ReceiveLine(2, line.LineId, At.AddHours(2), Document("incoming"), Kilograms.Positive(11m));

        var refused = Assert.Throws<InventoryRuleException>(() =>
            transfer.ReceiptOverage(line.LineId, receipt.OverageQuantity, At.AddHours(3), PieceCount.Of(1)));

        Assert.Equal(InventoryRuleCodes.PieceCountNotTracked, refused.Code);
        Assert.Null(receipt.DispatchedPieces);
    }

    [Fact]
    public void A_cancelled_dispatch_puts_the_pieces_back_at_the_source()
    {
        var (transfer, _, ledger) = DispatchedWithPieces(10m, 4);

        ledger.Apply(transfer.CancelDispatch(2, At.AddHours(2)).Movement(Guid.NewGuid(), Company));

        var balance = Assert.Single(ledger.Balances);
        Assert.Equal((Source, 10m, (PieceCount?)PieceCount.Of(4)), (balance.Position.LocationId, balance.Quantity.Value,
            balance.Pieces));
    }

    [Fact]
    public void A_transit_loss_records_the_pieces_in_and_out_without_changing_any_balance()
    {
        var (transfer, line, ledger) = DispatchedWithPieces(10m, 4);

        var loss = transfer.WriteOffLine(2, line.LineId, At.AddHours(2)).Movement(Guid.NewGuid(), Company);
        ledger.Apply(loss);

        Assert.All(loss.Lines, movementLine => Assert.Equal(PieceCount.Of(4), movementLine.Pieces));
        Assert.Empty(ledger.Balances);
    }

    private static (InventoryTransfer Transfer, InventoryTransferPlanLine Line, StockLedger Ledger) DispatchedWithPieces(
        decimal kilograms, int pieces)
    {
        var line = Planned(PieceLineId, PieceLabelId, kilograms);
        var source = Balance(line.LabelId, kilograms) with { Pieces = PieceCount.Of(pieces) };
        var ledger = StockLedger.FromStoredBalances([source]);
        var transfer = Transfer(line);
        var dispatch = transfer.Dispatch(1, At.AddHours(1), Document("outgoing"), [Actual(line, source)]);
        ledger.Apply(dispatch.Movement(Guid.NewGuid(), Company));
        Assert.Equal(PieceCount.Of(pieces), Assert.Single(dispatch.Lines).ActualPieces);
        return (transfer, line, ledger);
    }
}
