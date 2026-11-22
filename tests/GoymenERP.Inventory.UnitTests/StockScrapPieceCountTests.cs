using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Scraps;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>KLN-D119 (AK-2b): a piece-tracking label is scrapped with its pieces; the write-off moves both.</summary>
public sealed class StockScrapPieceCountTests
{
    private static readonly Guid ScrapId = Guid.Parse("60000000-0000-0000-0000-0000000000c1");

    [Fact]
    public void A_partial_scrap_leaves_the_remaining_pieces_on_the_label()
    {
        var ledger = Stocked(100m, 4);

        ledger.Apply(Scrap(100m, 25m, 4, 1).WriteOff(Guid.NewGuid(), At));

        Assert.Equal((75m, (PieceCount?)PieceCount.Of(3)), (ledger.QuantityAt(Position()).Value, ledger.PiecesAt(Position())));
    }

    [Fact]
    public void A_whole_scrap_takes_every_piece()
    {
        var ledger = Stocked(100m, 4);

        var writeOff = Scrap(100m, 100m, 4, 4).WriteOff(Guid.NewGuid(), At);
        ledger.Apply(writeOff);

        Assert.Equal(PieceCount.Of(4), Assert.Single(writeOff.Lines).Pieces);
        Assert.Empty(ledger.Balances);
    }

    [Fact]
    public void A_kilogram_only_fire_on_a_piece_tracking_label_scraps_zero_pieces()
    {
        var line = Assert.Single(Scrap(100m, 0.5m, 4, 0).WriteOff(Guid.NewGuid(), At).Lines);

        Assert.Equal((StockDirection.Out, 0.5m, (PieceCount?)PieceCount.Zero), (line.Direction, line.Quantity.Value, line.Pieces));
    }

    [Theory]
    [InlineData(100, 4, 3)]
    [InlineData(50, 4, 4)]
    [InlineData(50, 4, 5)]
    public void The_remaining_pieces_and_kilograms_run_out_together(int scrapKg, int bookPieces, int scrapPieces)
    {
        var refused = Assert.Throws<InventoryRuleException>(() => Scrap(100m, scrapKg, bookPieces, scrapPieces));

        Assert.Equal(InventoryRuleCodes.InvalidPieceCount, refused.Code);
    }

    [Fact]
    public void A_piece_tracking_label_needs_its_scrapped_pieces_and_a_kilogram_only_label_refuses_them()
    {
        var missing = Assert.Throws<InventoryRuleException>(() => new StockScrap(ScrapId, CompanyA, Position(),
            Kilograms.Positive(100m), Kilograms.Positive(10m), At, "Hurda", PieceCount.Of(4)));
        var extra = Assert.Throws<InventoryRuleException>(() => new StockScrap(ScrapId, CompanyA, Position(),
            Kilograms.Positive(100m), Kilograms.Positive(10m), At, "Hurda", scrapPieces: PieceCount.Of(1)));

        Assert.Equal((InventoryRuleCodes.PieceCountRequired, InventoryRuleCodes.PieceCountNotTracked), (missing.Code, extra.Code));
    }

    private static StockLedger Stocked(decimal kilograms, int pieces)
    {
        var ledger = new StockLedger();
        ledger.Apply(Movement(StockMovementKind.Receipt, new StockMovementLine(Position(), StockDirection.In,
            Kilograms.Positive(kilograms), PieceCount.Of(pieces))));
        return ledger;
    }

    private static StockScrap Scrap(decimal book, decimal scrapKg, int bookPieces, int scrapPieces) =>
        new(ScrapId, CompanyA, Position(), Kilograms.Positive(book), Kilograms.Positive(scrapKg), At, "Hurda",
            PieceCount.Of(bookPieces), PieceCount.Of(scrapPieces));
}
