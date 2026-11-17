namespace GoymenERP.Modules.Purchasing.Application.Contracts;

public sealed record PurchaseIncomingInvoicePageV1(int PageIndex, bool HasNext, IReadOnlyList<PurchaseIncomingInvoiceViewV1> Items);
public sealed record PurchaseInvoiceReceiptCandidateV1(Guid PurchaseOrderId, string? PurchaseOrderNumber, Guid PurchaseOrderLineId,
    int OrderLineNo, Guid AcceptanceId, DateTimeOffset AcceptedAtUtc, Guid ProductId, Guid UnitId, string AvailableQuantity);
public sealed record PurchaseInvoiceReceiptCandidatePageV1(Guid InvoiceId, Guid InvoiceLineId, string InvoiceVersion,
    int PageIndex, bool HasNext, IReadOnlyList<PurchaseInvoiceReceiptCandidateV1> Items);

/// <summary>API-50/51: company-scoped navigation and advisory receipt choices. Match commands re-read under owner locks.</summary>
public interface IPurchaseIncomingInvoiceWorkspaceV1
{
    Task<PurchaseIncomingInvoicePageV1> ListAsync(string? officialNumber, int pageIndex, CancellationToken cancellationToken = default);
    Task<PurchaseInvoiceReceiptCandidatePageV1> CandidatesAsync(Guid invoiceId, Guid invoiceLineId, int pageIndex,
        CancellationToken cancellationToken = default);
}
