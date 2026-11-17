using GoymenERP.BuildingBlocks.Messaging;

namespace GoymenERP.Modules.Inventory.Application;

public interface IInventoryPurchaseInvoiceCostStoreV1
{
    Task ApplyAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken = default);
}

/// <summary>The owner store repeats validation and deduplicates its committed effects before Work acknowledges delivery.</summary>
public sealed class InventoryPurchaseInvoiceCostInboxConsumerV1(IInventoryPurchaseInvoiceCostStoreV1 store) : IWorkInboxConsumerV1
{
    public Task HandleAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken) => store.ApplyAsync(envelope, cancellationToken);
}

public static class InventoryPurchaseInvoiceCostDifferenceV1
{
    public const string EventType = "inventory.purchase-invoice-cost-difference.proposed.v1";
    public const string AggregateType = "Inventory.PurchaseInvoiceCost";
}

/// <summary>Immutable valuation only. No physical movement, historical issue stamp or journal is rewritten.</summary>
public sealed record InventoryPurchaseInvoiceCostReceiptResultV1(Guid InvoiceLineId, int InvoiceLineNo, Guid AcceptanceId,
    Guid MovementId, int MovementLineNumber, Guid LabelId, Guid ProductId, Guid LocationId,
    decimal MatchedQuantity, decimal AcceptedQuantity, InventoryExactCostRatioV1 InvoicedKg,
    InventoryExactCostRatioV1 InvoiceTlValue, InventoryExactCostRatioV1 PreviouslyRecognizedTlValue,
    InventoryExactCostRatioV1 SourceDifference, long ReceiptCostStampVersion);

/// <summary>All source differences for one original receipt position are applied together; invoice line ordering
/// cannot introduce intermediate-average rounding or allocate a capped residual to an arbitrary line.</summary>
public sealed record InventoryPurchaseInvoiceCostPositionResultV1(Guid ProductId, Guid LocationId,
    InventoryExactCostRatioV1 SourceDifference, InventoryExactCostRatioV1 AbsorbedDifference,
    InventoryExactCostRatioV1 UnabsorbedDifference, InventoryExactCostRatioV1 RecordedPoolValueEffect,
    InventoryExactCostRatioV1 AverageRoundingDifference, long PoolVersion);

/// <summary>The exact residual belongs to Accounting's separate SMM suggestion. It is not rounded by an event adapter.</summary>
public sealed record InventoryPurchaseInvoiceCostApplicationResultV1(Guid CompanyId, Guid InvoiceId, long InvoiceVersion,
    Guid SourceEventId, Guid AdmissionId, DateOnly InvoiceDate, InventoryPurchaseInvoiceOfficialDocumentV1 OfficialDocument,
    string SourceSha256, IReadOnlyList<InventoryPurchaseInvoiceCostReceiptResultV1> Receipts,
    IReadOnlyList<InventoryPurchaseInvoiceCostPositionResultV1> Positions, Guid? CostDifferenceEventId,
    InventoryExactCostRatioV1 UnabsorbedTlDifference);
