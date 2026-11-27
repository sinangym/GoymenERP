using System.Globalization;
using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>Internal source variants, not an added public command body. Ordered selection retains its
/// existing non-null PO identity; an unordered source can never be represented by an empty/fake PO.</summary>
public abstract record PurchaseInvoiceAcceptanceSelectionV1(Guid InvoiceLineId, Guid AcceptanceId, string Quantity);
public sealed record PurchaseInvoiceOrderedAcceptanceSelectionV1(Guid InvoiceLineId, Guid PurchaseOrderId,
    Guid AcceptanceId, string Quantity) : PurchaseInvoiceAcceptanceSelectionV1(InvoiceLineId, AcceptanceId, Quantity);
public sealed record PurchaseInvoiceUnorderedAcceptanceSelectionV1(Guid InvoiceLineId, Guid AcceptanceId,
    Guid AcceptanceLineId, string ExpectedAcceptanceVersion, string Quantity)
    : PurchaseInvoiceAcceptanceSelectionV1(InvoiceLineId, AcceptanceId, Quantity);

public static class PurchaseInvoiceAcceptanceSelectionContractV1
{
    public static bool IsValid(PurchaseInvoiceAcceptanceSelectionV1? selection) => selection is not null
        && selection.InvoiceLineId != Guid.Empty && selection.AcceptanceId != Guid.Empty
        && TryQuantity(selection.Quantity, false, out _)
        && (selection switch
        {
            PurchaseInvoiceOrderedAcceptanceSelectionV1 ordered => ordered.PurchaseOrderId != Guid.Empty,
            PurchaseInvoiceUnorderedAcceptanceSelectionV1 unordered => unordered.AcceptanceLineId != Guid.Empty
                && TryVersion(unordered.ExpectedAcceptanceVersion, out _),
            _ => false,
        });

    internal static bool TryVersion(string? text, out long value) => long.TryParse(text, NumberStyles.None,
        CultureInfo.InvariantCulture, out value) && value > 0 && text == value.ToString(CultureInfo.InvariantCulture);

    // The existing invoice's decimal(24,6) storage bound; no product-family scale or physical unit is guessed.
    internal static bool TryQuantity(string? text, bool allowZero, out decimal value) => decimal.TryParse(text,
        NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) && (allowZero ? value >= 0 : value > 0)
        && value <= PurchaseIncomingInvoice.MaximumStoredQuantity
        && decimal.Round(value, PurchaseIncomingInvoice.MaximumStoredScale) == value
        && text == PurchaseAcceptanceParticipantContractV1.FormatQuantity(value);
}
