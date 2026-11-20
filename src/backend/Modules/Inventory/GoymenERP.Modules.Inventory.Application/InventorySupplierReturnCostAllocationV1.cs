namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Owner-measured purchase units; kilograms are independently owned by Inventory.</summary>
public sealed record InventorySupplierReturnPurchaseSourceV1(decimal AcceptedQuantity, string SourceVersion,
    IReadOnlyList<InventoryPurchaseInvoiceReceiptAdjustmentV1> ReceiptAdjustments);

/// <summary>Whole-source selection copies both exact owner quantities; partial selection supplies both explicitly.</summary>
public sealed record InventorySupplierReturnSourceSelectionV1(bool TakeAll, string? QuantityKg, string? UndoQuantity);
public sealed record InventorySupplierReturnInvoicedSourceSelectionV1(Guid SourceEventId, bool TakeAll,
    string? QuantityKg, string? UndoQuantity);
public sealed record InventorySupplierReturnCostAllocationV1(string ExpectedSourceVersion,
    InventorySupplierReturnSourceSelectionV1 Pending,
    IReadOnlyList<InventorySupplierReturnInvoicedSourceSelectionV1> InvoicedSources);

public sealed record InventorySupplierReturnInvoicedCostSourceV1(Guid SourceEventId, Guid InvoiceId, Guid InvoiceLineId,
    InventoryExactCostRatioV1 AvailableQuantityKg, InventoryExactCostRatioV1 AvailableUndoQuantity, bool WholePairAvailable = false);
public sealed record InventorySupplierReturnCostSourcesV1(Guid CompanyId, Guid GoodsAcceptanceId, Guid LabelId,
    string SourceVersion, InventoryExactCostRatioV1 PendingQuantityKg, InventoryExactCostRatioV1 PendingUndoQuantity,
    IReadOnlyList<InventorySupplierReturnInvoicedCostSourceV1> InvoicedSources, bool PendingWholePairAvailable = false);

public interface IInventorySupplierReturnCostSourceReadPortV1
{
    Task<InventorySupplierReturnCostSourcesV1> GetAsync(Guid companyId, Guid acceptanceId,
        InventorySupplierReturnPurchaseSourceV1 purchaseSource, CancellationToken cancellationToken = default);
}

/// <summary>Resolved immutable evidence retained by the return movement; no amount or price is disclosed by preview.</summary>
public sealed record InventorySupplierReturnResolvedSourceV1(Guid? SourceEventId,
    InventoryExactCostRatioV1 QuantityKg, InventoryExactCostRatioV1 UndoQuantity);
public sealed record InventorySupplierReturnAllocationProofV1(string SourceVersion, Guid AcceptanceId,
    long LabelVersionBefore, long LabelVersionAfter, InventorySupplierReturnPurchaseSourceV1 PurchaseSource,
    InventorySupplierReturnCostSourcesV1 Before, IReadOnlyList<InventorySupplierReturnResolvedSourceV1> Selected);
