using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AK-3 / AK-8b (KLN-D119): the undo quantity of a piece purchase line is its piece count for EVERY receipt adjustment — a
/// supplier return, a receipt correction and an undo — so Inventory takes the pieces out of the piece-tracking label with the
/// kilograms. A kilogram line sends none, exactly as before; a fraction of a piece is refused, never converted.
/// </summary>
public sealed class PurchaseReceiptReversalPieceCountTests
{
    [Theory]
    [InlineData("3", false, 3)]
    [InlineData("12", false, 12)]
    [InlineData("3", true, 3)]
    public void A_partial_or_full_undo_of_a_piece_line_carries_its_whole_pieces_to_Inventory(string undoQuantity, bool supplierReturn, int pieces)
    {
        Assert.Equal(pieces, PurchaseReceiptReversalHost.PieceCountOf(unitIsKilogram: false, undoQuantity, supplierReturn));
    }

    [Theory]
    [InlineData("3")]
    [InlineData("2.5")]
    [InlineData(null)]
    public void A_kilogram_line_sends_no_piece_count(string? undoQuantity)
    {
        Assert.Null(PurchaseReceiptReversalHost.PieceCountOf(unitIsKilogram: true, undoQuantity, isSupplierReturn: false));
        Assert.Null(PurchaseReceiptReversalHost.PieceCountOf(unitIsKilogram: true, undoQuantity, isSupplierReturn: true));
    }

    [Theory]
    [InlineData("2.5")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("")]
    [InlineData(null)]
    public void A_fraction_a_zero_or_a_missing_piece_quantity_is_refused_422_with_the_flows_own_code(string? undoQuantity)
    {
        var undo = Assert.Throws<PurchasingServiceException>(() =>
            PurchaseReceiptReversalHost.PieceCountOf(unitIsKilogram: false, undoQuantity, isSupplierReturn: false));
        var supplierReturn = Assert.Throws<PurchasingServiceException>(() =>
            PurchaseReceiptReversalHost.PieceCountOf(unitIsKilogram: false, undoQuantity, isSupplierReturn: true));

        Assert.Equal((422, PurchaseReceiptAdjustmentCodesV1.PieceCountRequired), (undo.Status, undo.Code));
        Assert.Equal((422, PurchaseSupplierReturnCodesV1.PieceCountRequired), (supplierReturn.Status, supplierReturn.Code));
        Assert.Equal("undoQuantity", Assert.Single(undo.Errors).Field);
    }
}
