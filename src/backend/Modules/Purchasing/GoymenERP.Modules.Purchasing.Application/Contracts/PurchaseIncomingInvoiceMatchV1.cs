using GoymenERP.Modules.Purchasing.Domain.Invoices;
using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>The caller selects receipt shares, never an authoritative price, rate, supplier or tolerance.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PurchaseInvoiceReceiptSelectionV1(Guid InvoiceLineId, Guid PurchaseOrderId, Guid AcceptanceId, string Quantity);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PurchaseInvoiceAdditionalCostSelectionV1(Guid InvoiceLineId, Guid PurchaseOrderId, Guid CostId, string NetAmount);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PurchaseIncomingInvoiceMatchRequestV1(string ExpectedVersion, IReadOnlyList<PurchaseInvoiceReceiptSelectionV1> Receipts,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PurchaseInvoiceAdditionalCostSelectionV1>? AdditionalCosts = null);
public sealed record PurchaseIncomingInvoiceMatchSourceV1(IReadOnlyList<PurchaseInvoiceReceiptMatch> Receipts, PurchaseInvoiceVariancePolicy Policy,
    IReadOnlyList<PurchaseInvoiceAdditionalCostMatch>? AdditionalCosts = null);

/// <summary>ALS-D8/D14/D15: invoked inside the Purchasing invoice store's transaction with every selected order line locked.
/// Actual Purchasing receipts and prices plus versioned Settings facts are read; no client-supplied expected amounts.</summary>
public interface IPurchaseIncomingInvoiceMatchSourceV1
{
    /// <summary>Internal extension boundary; no existing command/wire/hash changes. Only wholly ordered,
    /// valid selections can reach the old PO price/receipt matcher. Unordered sources require a real
    /// persistent acceptance AND commercial bridge; even valid operational facts cannot be downgraded to a PO.</summary>
    Task<PurchaseIncomingInvoiceMatchSourceV1> ReadAcceptanceVariantsAsync(PurchaseIncomingInvoice invoice,
        IReadOnlyList<PurchaseInvoiceAcceptanceSelectionV1> selected, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (selected is null || selected.Count == 0 || selected.Any(item =>
                item is not PurchaseInvoiceOrderedAcceptanceSelectionV1 || !PurchaseInvoiceAcceptanceSelectionContractV1.IsValid(item))
            || selected.Select(item => item.AcceptanceId).Distinct().Count() != selected.Count)
            throw new PurchasingServiceException(503, "service_unavailable");
        return ReadWithAdditionalCostsAsync(invoice, selected.Cast<PurchaseInvoiceOrderedAcceptanceSelectionV1>()
            .Select(item => new PurchaseInvoiceReceiptSelectionV1(item.InvoiceLineId, item.PurchaseOrderId, item.AcceptanceId, item.Quantity))
            .ToArray(), [], cancellationToken);
    }

    Task<PurchaseIncomingInvoiceMatchSourceV1> ReadAsync(PurchaseIncomingInvoice invoice,
        IReadOnlyList<PurchaseInvoiceReceiptSelectionV1> selected, CancellationToken cancellationToken = default);
    Task<PurchaseIncomingInvoiceMatchSourceV1> ReadWithAdditionalCostsAsync(PurchaseIncomingInvoice invoice,
        IReadOnlyList<PurchaseInvoiceReceiptSelectionV1> selected, IReadOnlyList<PurchaseInvoiceAdditionalCostSelectionV1> expenses,
        CancellationToken cancellationToken = default) => expenses.Count == 0 ? ReadAsync(invoice, selected, cancellationToken)
        : throw new PurchasingServiceException(503, "service_unavailable");
}
