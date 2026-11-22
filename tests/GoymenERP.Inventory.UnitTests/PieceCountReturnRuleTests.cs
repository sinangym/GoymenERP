using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Quantities;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>KLN-D119 (AK-2b): a return of a traced exit (customer return, production input return) and its pieces.</summary>
public sealed class PieceCountReturnRuleTests
{
    [Fact]
    public void A_partial_return_brings_back_some_pieces_while_kilograms_and_pieces_stay_outstanding()
    {
        var pieces = Returned(100m, 4, 0m, 0, 50m, 2);

        Assert.Equal(PieceCount.Of(2), pieces);
    }

    [Fact]
    public void The_last_kilograms_bring_back_the_last_pieces()
    {
        Assert.Equal(PieceCount.Of(2), Returned(100m, 4, 50m, 2, 50m, 2));
    }

    [Fact]
    public void A_kilogram_only_exit_returns_without_pieces()
    {
        Assert.Null(PieceCountReturnRule.Returned(Kilograms.Positive(100m), null, Kilograms.Zero, PieceCount.Zero,
            Kilograms.Positive(10m), null));
    }

    [Theory]
    [InlineData(50, 5, "inventory_negative_piece_count")]
    [InlineData(100, 3, "inventory_invalid_piece_count")]
    [InlineData(50, 4, "inventory_invalid_piece_count")]
    public void Pieces_never_exceed_the_exit_and_run_out_with_the_kilograms(int returnedKg, int returnedPieces, string code)
    {
        var refused = Assert.Throws<InventoryRuleException>(() => Returned(100m, 4, 0m, 0, returnedKg, returnedPieces));

        Assert.Equal(code, refused.Code);
    }

    [Fact]
    public void A_piece_exit_needs_returned_pieces_and_a_kilogram_only_exit_refuses_them()
    {
        var missing = Assert.Throws<InventoryRuleException>(() => PieceCountReturnRule.Returned(Kilograms.Positive(100m),
            PieceCount.Of(4), Kilograms.Zero, PieceCount.Zero, Kilograms.Positive(10m), null));
        var extra = Assert.Throws<InventoryRuleException>(() => PieceCountReturnRule.Returned(Kilograms.Positive(100m),
            null, Kilograms.Zero, PieceCount.Zero, Kilograms.Positive(10m), 1));

        Assert.Equal((InventoryRuleCodes.PieceCountRequired, InventoryRuleCodes.PieceCountNotTracked), (missing.Code, extra.Code));
    }

    private static PieceCount? Returned(decimal exitedKg, int exitedPieces, decimal alreadyKg, int alreadyPieces,
        decimal returnedKg, int requested) =>
        PieceCountReturnRule.Returned(Kilograms.Positive(exitedKg), PieceCount.Of(exitedPieces), Kilograms.Of(alreadyKg),
            PieceCount.Of(alreadyPieces), Kilograms.Positive(returnedKg), requested);
}
