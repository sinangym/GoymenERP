using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Modules.Purchasing.Domain.Services;

/// <summary>Private snapshot locator only, never authority or a substitute for the real parent command receipt.</summary>
public sealed record PurchaseServiceSupplierRevisionOrigin(Guid ParentOrderId, long ParentRevisionVersion, string ParentCommandKeyHash);

public static partial class PurchaseServiceOrder
{
    public static PurchaseServiceOrderState PrepareSupplierRevision(PurchaseServiceOrderState value, long expected,
        Guid party, Guid account, string partyVersion, string accountVersion, string actor, string reason, DateTimeOffset at,
        PurchaseOrderInvoiceHistory invoiceHistory)
    {
        Validate(value); RequireVersion(value, expected); RequireActor(actor, at);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > PurchaseOrder.SupplierRevisionReasonMaximumLength)
            throw new ArgumentException("Cari revizyonu gerekçesi geçersiz.");
        if (party == Guid.Empty || account == Guid.Empty || party == value.SupplierPartyId) throw new ArgumentException("Yeni cari gerekli.");
        if (invoiceHistory == PurchaseOrderInvoiceHistory.Unavailable)
            throw new PurchasingRuleException(PurchaseOrder.SupplierRevisionInvoiceUnavailable, "Fatura geçmişi doğrulanamadı.");
        if (invoiceHistory != PurchaseOrderInvoiceHistory.None || value.Acceptances.Count != 0 || value.Lines.Any(x => x.AcceptedQuantity != 0)
            || value.Status is not (PurchaseServiceOrderStatus.Approved or PurchaseServiceOrderStatus.Draft))
            throw new PurchasingRuleException(PurchaseOrder.SupplierRevisionBlocked, "Kabul/fatura varsa cari değiştirilemez.");
        var result = value with { Version = checked(value.Version + 1), Status = PurchaseServiceOrderStatus.Draft,
            SupplierPartyId = party, CompanyAccountId = account, PartyVersion = partyVersion, AccountVersion = accountVersion,
            PreparedBy = actor, ApprovedBy = null, UpdatedAtUtc = at, SupplierRevisionOrigin = null,
            Lines = Array.AsReadOnly(value.Lines.Select(x => x with { UnitPrice = null }).ToArray()),
            Materials = Array.AsReadOnly(value.Materials.ToArray()), ExpectedOutputs = Array.AsReadOnly(value.ExpectedOutputs.ToArray()),
            Acceptances = Array.AsReadOnly(value.Acceptances.ToArray()) };
        Validate(result); return result;
    }
    // The actual store must first verify the real linked parent receipt and current parent Draft. This pure leaf is no admission.
    public static PurchaseServiceOrderState SetSupplierRevisionPrices(PurchaseServiceOrderState value, long expected,
        IReadOnlyDictionary<Guid, decimal> prices, string actor, string reason, DateTimeOffset at)
    {
        Validate(value); RequireVersion(value, expected); RequireActor(actor, at); RequireReason(reason);
        if (value.Status != PurchaseServiceOrderStatus.Draft || value.SupplierRevisionOrigin is null)
            throw new PurchaseServiceOrderException(StateConflict);
        ArgumentNullException.ThrowIfNull(prices);
        if (prices.Count == 0 || prices.Any(x => value.Lines.All(line => line.LineId != x.Key))) throw new ArgumentException("Fiyat satırı geçersiz.");
        if (prices.All(x => value.Lines.Single(line => line.LineId == x.Key).UnitPrice == x.Value)) throw new PurchaseServiceOrderException(StateConflict);
        var result = value with { Version = checked(value.Version + 1), UpdatedAtUtc = at,
            Lines = value.Lines.Select(line => prices.TryGetValue(line.LineId, out var price) ? line with { UnitPrice = price } : line).ToArray() };
        Validate(result); return result;
    }
}
