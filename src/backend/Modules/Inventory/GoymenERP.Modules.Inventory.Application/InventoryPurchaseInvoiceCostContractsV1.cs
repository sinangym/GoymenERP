using GoymenERP.BuildingBlocks.Messaging;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Purchasing's immutable invoice identity. A document proof is not a new invoice or a current-file lookup.</summary>
public sealed record InventoryPurchaseInvoiceOfficialDocumentV1(Guid DocumentId, long DocumentVersion, string Sha256, Guid Ettn);

/// <summary>Both quantities are frozen by the actual Purchasing owner in the same order unit. The Inventory owner
/// verifies the referenced receipt and its physical kg; no current Product conversion or inferred label is used.</summary>
public sealed record InventoryPurchaseInvoiceReceiptCostShareV1(Guid AcceptanceId, Guid OrderId, Guid OrderLineId,
    long OrderVersion, decimal MatchedQuantity, decimal AcceptedQuantity,
    IReadOnlyList<InventoryPurchaseInvoiceReceiptAdjustmentV1>? ReceiptAdjustments = null);

public sealed record InventoryPurchaseInvoiceReceiptAdjustmentV1(Guid AdjustmentId, decimal UndoQuantity, bool IsSupplierReturn);

public sealed record InventoryPurchaseInvoiceGoodsCostSourceV1(Guid ProductId, Guid UnitId, decimal InvoiceQuantity,
    IReadOnlyList<InventoryPurchaseInvoiceReceiptCostShareV1> Receipts);

/// <summary>Only official net cost enters Inventory. VAT/withholding, service and expense amounts are not stock cost.</summary>
public sealed record InventoryPurchaseInvoiceCostLineV1(Guid LineId, int LineNo, string? ItemType, string? LinePurpose,
    decimal? Net, decimal? FunctionalNet, InventoryPurchaseInvoiceGoodsCostSourceV1? GoodsCostSource = null);

public sealed record InventoryPurchaseInvoiceCostSourceV1(Guid CompanyId, Guid InvoiceId, long Version,
    Guid AdmissionId, Guid CurrencyId, decimal? Net, decimal? FunctionalNet, DateOnly InvoiceDate,
    string? FinalizedBy, IReadOnlyList<InventoryPurchaseInvoiceCostLineV1>? Lines,
    InventoryPurchaseInvoiceOfficialDocumentV1? OfficialDocument = null);

public static class InventoryPurchaseInvoiceCostInboxV1
{
    public const string SourceName = "purchasing";
    public const string EventType = "purchasing.invoice.finalized.v1";
    public const string ConsumerName = "inventory.purchase-invoice-cost";
    public static IReadOnlyList<WorkInboxSubscriptionV1> Subscriptions { get; } = [new(SourceName, EventType, 1)];
}

/// <summary>One exact official invoice-line share. This proves neither initial cost recognition nor where a later
/// cost difference is absorbed: those require the actual Inventory receipt/stamp/pool under the owner transaction.</summary>
public sealed record InventoryPurchaseInvoiceCostReceiptV1(Guid InvoiceLineId, int InvoiceLineNo,
    Guid ProductId, Guid UnitId, Guid AcceptanceId, Guid OrderId, Guid OrderLineId, long OrderVersion,
    decimal MatchedQuantity, decimal AcceptedQuantity, InventoryExactCostRatioV1 TlNetShare,
    IReadOnlyList<InventoryPurchaseInvoiceReceiptAdjustmentV1>? ReceiptAdjustments = null);

public sealed record InventoryPurchaseInvoiceCostProjectionV1(Guid CompanyId, Guid InvoiceId, long InvoiceVersion,
    Guid AdmissionId, DateOnly InvoiceDate, string FinalizedBy, InventoryPurchaseInvoiceOfficialDocumentV1 OfficialDocument,
    IReadOnlyList<InventoryPurchaseInvoiceCostReceiptV1> Receipts);
