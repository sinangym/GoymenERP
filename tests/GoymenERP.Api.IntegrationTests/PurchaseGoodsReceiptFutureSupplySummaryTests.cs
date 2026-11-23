using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class PurchaseGoodsReceiptFutureSupplySummaryTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Order = Guid.NewGuid();
    private static readonly Guid TechnicalLine = Guid.NewGuid();
    private static readonly Guid GeneralLine = Guid.NewGuid();
    private static readonly Guid Unit = Guid.NewGuid();
    private static readonly PurchaseReceiptSourceV1 Source = new(Company, Order, TechnicalLine, "7", Guid.NewGuid(), "v1", Unit,
        new DateOnly(2026, 11, 16)) { PurchaseOrderGeneralLineId = GeneralLine };

    [Fact]
    public void A_technical_receipt_shows_its_owner_general_line_quantity_exactly_without_a_conversion_or_new_permission()
    {
        var summary = PurchaseGoodsReceiptHost.FutureSupplySummary(Source,
            new(InventoryFutureAllocationReadOutcomeV1.Present, "2.125", Unit));
        Assert.Equal(new PurchaseReceiptFutureSupplySummaryV1(Company, Order, GeneralLine, Unit, "2.125"), summary);
    }

    [Fact]
    public void Only_the_owner_none_answer_is_displayed_as_zero_active_allocations() =>
        Assert.Equal("0", PurchaseGoodsReceiptHost.FutureSupplySummary(Source,
            new(InventoryFutureAllocationReadOutcomeV1.None, null, null)).ActiveAllocatedQuantity);

    [Theory]
    [InlineData("unreadable")]
    [InlineData("wrong-unit")]
    [InlineData("zero-present")]
    [InlineData("malformed")]
    [InlineData("none-with-quantity")]
    [InlineData("none-with-unit")]
    [InlineData("unknown-outcome")]
    [InlineData("missing")]
    public void A_bad_owner_answer_is_not_turned_into_a_zero_or_partial_success(string fault)
    {
        var answer = fault switch
        {
            "unreadable" => new InventoryFutureAllocationSumV1(InventoryFutureAllocationReadOutcomeV1.Unreadable, null, null),
            "wrong-unit" => new(InventoryFutureAllocationReadOutcomeV1.Present, "2", Guid.NewGuid()),
            "zero-present" => new(InventoryFutureAllocationReadOutcomeV1.Present, "0", Unit),
            "malformed" => new(InventoryFutureAllocationReadOutcomeV1.Present, "2.1250", Unit),
            "none-with-quantity" => new(InventoryFutureAllocationReadOutcomeV1.None, "0", null),
            "none-with-unit" => new(InventoryFutureAllocationReadOutcomeV1.None, null, Unit),
            "unknown-outcome" => new((InventoryFutureAllocationReadOutcomeV1)99, null, null),
            _ => null,
        };
        var refused = Assert.Throws<PurchasingServiceException>(() => PurchaseGoodsReceiptHost.FutureSupplySummary(Source, answer));
        Assert.Equal((503, PurchaseGoodsReceiptCodesV1.SourceInvalid), (refused.Status, refused.Code));
    }
}
