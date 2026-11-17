namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>Read actual Purchasing facts through the host's borrowed transaction after acceptance staging.
/// Inventory owns unit conversion/valuation; a price-pending or invoice-missing answer remains explicit.</summary>
public interface IPurchaseReceiptCostProofV1 : IAsyncDisposable
{
    Task<PurchaseReceiptCostProofV1?> ReadAsync(Guid purchaseOrderId, Guid purchaseOrderLineId, Guid acceptanceId,
        CancellationToken cancellationToken = default);
}

public sealed record PurchaseReceiptCostProofV1(Guid CompanyId, Guid PurchaseOrderId, Guid PurchaseOrderLineId,
    Guid AcceptanceId, Guid ProductId, string ProductVersion, string AcceptedQuantity,
    string AcceptanceOrderVersion, DateTimeOffset ReceivedAtUtc, PurchaseOrderLineCostAnswerV1 Price,
    PurchaseInvoiceCostRateAnswerV1 InvoiceRate, PurchaseOrderExchangeRateAnswerV1? ApprovedOrderRate = null);
