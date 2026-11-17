using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryGoodsReceiptReversalContractTests
{
    [Theory]
    [InlineData(InventoryStoreCodes.ConcurrentChange, 409, InventoryGoodsReceiptReversalCodesV1.ConcurrentChange)]
    [InlineData(InventoryStoreCodes.MovementIdConflict, 409, InventoryGoodsReceiptReversalCodesV1.IdempotencyConflict)]
    [InlineData(InventoryStoreCodes.DuplicateSource, 409, InventoryGoodsReceiptReversalCodesV1.IdempotencyConflict)]
    [InlineData("settings_provider_unavailable", 503, InventoryGoodsReceiptReversalCodesV1.QuantityProfileUnavailable)]
    [InlineData("unrecognized_owner_fault", 503, InventoryGoodsReceiptReversalCodesV1.Unavailable)]
    public void Store_failures_keep_the_closed_status_code_contract_instead_of_escaping_as_server_errors(string source, int status, string code)
    {
        var refused = InventoryGoodsReceiptReversalParticipant.StoreRefusal(source);
        Assert.Equal((status, code), (refused.Status, refused.Code));
        Assert.True(InventoryGoodsReceiptReversalCodesV1.IsDefined(status, code));
    }

    [Fact]
    public void An_unknown_or_mismatched_status_code_pair_cannot_be_constructed_as_a_typed_reversal_refusal()
    {
        Assert.Throws<ArgumentException>(() => new InventoryGoodsReceiptReversalException(200, InventoryGoodsReceiptReversalCodesV1.Unavailable));
        Assert.Throws<ArgumentException>(() => new InventoryGoodsReceiptReversalException(409, "unknown"));
    }
}
