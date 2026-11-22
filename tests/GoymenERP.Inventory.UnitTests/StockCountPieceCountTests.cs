using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Counts;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>KLN-D119 (AK-2): a piece-tracking label is counted in pieces and kilograms; its adjustment moves both.</summary>
public sealed class StockCountPieceCountTests
{
    private static readonly Guid CountId = Guid.Parse("60000000-0000-0000-0000-0000000000a1");

    [Fact]
    public void A_shortage_of_pieces_and_kilograms_posts_one_out_line_with_the_piece_difference()
    {
        var count = Count(100m, 75m, 4, 3);
        var ledger = new StockLedger();
        ledger.Apply(Movement(StockMovementKind.Receipt, new StockMovementLine(Position(), StockDirection.In,
            Kilograms.Positive(100m), PieceCount.Of(4))));

        var adjustment = count.Adjustment(Guid.NewGuid(), At);
        ledger.Apply(adjustment);

        var line = Assert.Single(adjustment.Lines);
        Assert.Equal((StockDirection.Out, 25m, (PieceCount?)PieceCount.Of(1)), (line.Direction, line.Quantity.Value, line.Pieces));
        Assert.Equal(-1, count.PieceDifference);
        Assert.Equal((75m, (PieceCount?)PieceCount.Of(3)), (ledger.QuantityAt(Position()).Value, ledger.PiecesAt(Position())));
    }

    [Fact]
    public void A_kilogram_only_difference_on_a_piece_tracking_label_moves_zero_pieces()
    {
        var line = Assert.Single(Count(100m, 100.5m, 4, 4).Adjustment(Guid.NewGuid(), At).Lines);

        Assert.Equal((StockDirection.In, 0.5m, (PieceCount?)PieceCount.Zero), (line.Direction, line.Quantity.Value, line.Pieces));
    }

    [Fact]
    public void A_kg_only_label_is_counted_without_pieces_exactly_as_before()
    {
        var count = Count(100m, 90m, null, null);

        Assert.Null(count.PieceDifference);
        Assert.Null(Assert.Single(count.Adjustment(Guid.NewGuid(), At).Lines).Pieces);
    }

    [Theory]
    [InlineData(4, null, "inventory_piece_count_required")]
    [InlineData(null, 4, "inventory_piece_count_not_tracked")]
    public void A_piece_tracking_label_needs_counted_pieces_and_a_kg_only_label_refuses_them(int? book, int? counted, string code)
    {
        Assert.Equal(code, Assert.Throws<InventoryRuleException>(() => Count(100m, 90m, book, counted)).Code);
    }

    [Theory]
    [InlineData("100", "100", 4, 3)] // a piece difference without a kg difference
    [InlineData("100", "110", 4, 3)] // a piece shortage with a kg surplus
    [InlineData("100", "0", 4, 1)]   // kg gone but a piece counted
    [InlineData("100", "50", 4, 0)]  // no piece but kg counted
    public void A_piece_count_that_disagrees_with_its_kilograms_is_refused(string book, string counted, int bookPieces, int countedPieces)
    {
        var error = Assert.Throws<InventoryRuleException>(() => Count(decimal.Parse(book), decimal.Parse(counted), bookPieces, countedPieces));

        Assert.Equal(InventoryRuleCodes.InvalidPieceCount, error.Code);
    }

    private static StockCount Count(decimal book, decimal counted, int? bookPieces, int? countedPieces) =>
        new(CountId, CompanyA, Position(), Kilograms.Of(book), Kilograms.Of(counted), At, "SENTETIK sayım",
            bookPieces is { } b ? PieceCount.Of(b) : null, countedPieces is { } c ? PieceCount.Of(c) : null);
}
