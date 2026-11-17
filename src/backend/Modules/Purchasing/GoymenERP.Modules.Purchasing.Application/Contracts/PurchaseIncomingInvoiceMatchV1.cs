using GoymenERP.Modules.Purchasing.Domain.Invoices;
using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>The caller selects receipt shares, never an authoritative price, rate, supplier or tolerance.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PurchaseInvoiceReceiptSelectionV1(Guid InvoiceLineId, Guid PurchaseOrderId, Guid AcceptanceId, string Quantity);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PurchaseIncomingInvoiceMatchRequestV1(string ExpectedVersion, IReadOnlyList<PurchaseInvoiceReceiptSelectionV1> Receipts);
public sealed record PurchaseIncomingInvoiceMatchSourceV1(IReadOnlyList<PurchaseInvoiceReceiptMatch> Receipts, PurchaseInvoiceVariancePolicy Policy);

/// <summary>ALS-D8/D14/D15: invoked inside the Purchasing invoice store's transaction with every selected order line locked.
/// Actual Purchasing receipts and prices plus versioned Settings facts are read; no client-supplied expected amounts.</summary>
public interface IPurchaseIncomingInvoiceMatchSourceV1
{
    Task<PurchaseIncomingInvoiceMatchSourceV1> ReadAsync(PurchaseIncomingInvoice invoice,
        IReadOnlyList<PurchaseInvoiceReceiptSelectionV1> selected, CancellationToken cancellationToken = default);
}
