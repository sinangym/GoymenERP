using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseSupplierReturnAdjustmentMetadataTests
{
    [Fact]
    public void Null_adjustment_metadata_does_not_change_the_legacy_supplier_return_request_hash()
    {
        var request = Request(); var order = Guid.NewGuid();
        var legacy = new { request.ReturnId, request.ReturnLineId, request.GoodsAcceptanceId, request.InventoryMovementId,
            request.SourceBlock, request.QuantityKg, request.ManualUnitCostTryPerKg, request.ReplacementExpected,
            request.Reason, request.OccurredAtUtc };
        Assert.Equal(PurchasingRequestHash.Compute(PurchaseSupplierReturnCommandsV1.CreateV1, order, new { request = legacy }),
            PurchasingRequestHash.Compute(PurchaseSupplierReturnCommandsV1.CreateV1, order, new { request }));
        Assert.DoesNotContain("expectedPurchaseOrderVersion", JsonSerializer.Serialize(request, PurchasingJson.Options), StringComparison.Ordinal);
        Assert.DoesNotContain("undoQuantity", JsonSerializer.Serialize(request, PurchasingJson.Options), StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_purchase_quantity_and_version_are_a_canonical_pair_independent_of_measured_kilograms()
    {
        var request = Request() with { ExpectedPurchaseOrderVersion = "4", UndoQuantity = "2" };
        PurchaseSupplierReturnJsonV1.ValidateCreate(request);
        Assert.Equal(("2", "1.9"), (request.UndoQuantity, request.QuantityKg));
        Assert.Throws<JsonException>(() => PurchaseSupplierReturnJsonV1.ValidateCreate(request with { UndoQuantity = null }));
        Assert.Throws<JsonException>(() => PurchaseSupplierReturnJsonV1.ValidateCreate(request with { ExpectedPurchaseOrderVersion = "04" }));
        Assert.Throws<JsonException>(() => PurchaseSupplierReturnJsonV1.ValidateCreate(request with { UndoQuantity = "2.000" }));
    }

    private static CreatePurchaseSupplierReturnV1 Request() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "Free", "1.9", "12.3456", false, "SENTETIK return metadata", DateTimeOffset.UnixEpoch);

    [Fact]
    public void A_later_cost_approval_names_a_fresh_order_version_explicitly_and_preserves_legacy_hash_when_absent()
    {
        var request = new DecidePurchaseSupplierReturnV1("1", null);
        var id = Guid.NewGuid();
        var legacy = new { request.ExpectedVersion, request.Reason };
        Assert.Equal(PurchasingRequestHash.Compute(PurchaseSupplierReturnCommandsV1.ApproveCostExceptionV1, id, new { request = legacy }),
            PurchasingRequestHash.Compute(PurchaseSupplierReturnCommandsV1.ApproveCostExceptionV1, id, new { request }));
        PurchaseSupplierReturnJsonV1.ValidateDecision(request with { ExpectedPurchaseOrderVersion = "7" });
        Assert.Throws<JsonException>(() => PurchaseSupplierReturnJsonV1.ValidateDecision(request with { ExpectedPurchaseOrderVersion = "07" }));
    }
}
