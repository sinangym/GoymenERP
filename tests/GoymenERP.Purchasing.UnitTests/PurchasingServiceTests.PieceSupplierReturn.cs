using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// AK-3 (KLN-D119): a supplier return of a piece purchase line names its returned pieces as a whole undo quantity (purchase unit,
/// ALS-D91/D92) beside the label kilograms; a legacy kg-only return or a fraction of a piece is refused before anything is written,
/// never derived from kilograms. Split/merged-label and quantity returns are AK-8 (SAT-D433, SAT-D440–D445). SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class PurchasingServiceTests
{
    private static async Task<(World World, Guid Order, ReturnStoreFake Returns)> PieceReturnWorldAsync()
    {
        var world = new World
        {
            TheoreticalKilograms = new FakeTheoreticalKilograms
            {
                Answer = PurchaseTheoreticalKilogramsPerUnitAnswerV1.Found(12.345m, "product.kg-per-unit.v1/sentetik"),
            },
        };
        var order = await world.Service().CreateOrderAsync(OrderOf(null, null, "100", unitId: PieceUnitId), Metadata("piece-return-order"));
        await world.AsApprover(() => world.Service().ApproveOrderAsync(order.Detail.Order.Id, new("2"), Metadata("piece-return-approve")));
        return (world, order.Detail.Order.Id, new ReturnStoreFake(order.Detail.Lines[0].LineId));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("3", "2.5")]
    public async Task A_piece_line_return_without_a_whole_piece_undo_quantity_is_refused_before_any_write(string? version, string? undo)
    {
        var (world, order, returns) = await PieceReturnWorldAsync();
        var host = new ReturnAdjustmentHost();
        var request = ReturnRequest() with { ExpectedPurchaseOrderVersion = version, UndoQuantity = undo, ReplacementExpected = false };

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service(reversals: host, returns: returns).CreateSupplierReturnAsync(order, request, Metadata("piece-return-refused")));

        Assert.Equal((422, PurchaseSupplierReturnCodesV1.PieceCountRequired), (refusal.Status, refusal.Code));
        Assert.Equal((0, 0), (host.Calls, returns.NewWrites));
    }

    [Fact]
    public async Task A_piece_line_return_with_whole_pieces_is_recorded_with_its_pieces_and_kilograms()
    {
        // Golden A10: a 20-piece label weighed 249,870 kg leaves the piece line.
        var (world, order, returns) = await PieceReturnWorldAsync();
        var host = new ReturnAdjustmentHost();
        var request = ReturnRequest() with { QuantityKg = "249.87", UndoQuantity = "20" };

        var answer = await world.Service(reversals: host, returns: returns).CreateSupplierReturnAsync(order, request, Metadata("piece-return"));

        Assert.Equal(("20", "249.87"), (answer.Return.UndoQuantity, answer.Return.QuantityKg));
        Assert.Equal(1, returns.NewWrites);
    }
}
