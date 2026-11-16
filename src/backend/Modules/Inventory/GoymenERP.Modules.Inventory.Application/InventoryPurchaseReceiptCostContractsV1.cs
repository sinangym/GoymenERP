namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Only server-verified receipt identities. Measured kg is not used to invent a purchase conversion coefficient.</summary>
public sealed record InventoryPurchaseReceiptCostSelectionV1(Guid CompanyId, Guid AcceptanceId,
    Guid PurchaseOrderId, Guid PurchaseOrderLineId, Guid ProductId, string ProductVersion, DateTimeOffset OccurredAtUtc);

/// <summary>
/// Actual Purchasing price + Product coefficient + recorded invoice/entry-day FX proof. TL and USD are independent;
/// an unavailable USD display value never removes a readable TL value. EvidenceJson contains source identities/versions,
/// not an idempotency key or user-selected valuation. Unknown fields are null, never provisional zeroes.
/// </summary>
public sealed record InventoryPurchaseReceiptCostV1(Guid CompanyId, Guid AcceptanceId,
    Guid PurchaseOrderId, Guid PurchaseOrderLineId, Guid? OriginalCurrencyId, decimal? OriginalUnitCostPerKg,
    decimal? TlUnitCostPerKg, decimal? UsdUnitCostPerKg, InventoryIssueCostCurrencyV1 Currency,
    string SourceVersion, string EvidenceJson, string? TlUnavailableReason, string? UsdUnavailableReason,
    InventoryExactCostRatioV1? OriginalUnitCostRatio = null, InventoryExactCostRatioV1? TlUnitCostRatio = null,
    InventoryExactCostRatioV1? UsdUnitCostRatio = null);

public interface IInventoryPurchaseReceiptCostSourceV1
{
    Task<InventoryPurchaseReceiptCostV1> ReadAsync(InventoryPurchaseReceiptCostSelectionV1 selection,
        CancellationToken cancellationToken = default);
}
