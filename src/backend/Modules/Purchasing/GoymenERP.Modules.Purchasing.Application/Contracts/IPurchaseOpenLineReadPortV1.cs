namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// AT-025 ("Satış ve satın alma belgeleri operasyon listelerinde karışmaz"): the Purchasing-owned read port behind the
/// PURCHASE section of the operations work list (Logistics L-09, "Beklenen Alış / Kapı Kabulü"). It answers the context
/// company's APPROVED purchase order lines that still wait for goods receipt — remaining = committed − accepted above
/// zero, the Purchasing owner's own remaining (the <see cref="IPurchaseSupplyReferencePortV1"/> rule, never recomputed by
/// the consumer) — as a LINE page, newest order first, then by line number.
/// <para>
/// A Draft order's lines are not listed (Draft orders reserve nothing, ASSUMPTION-PUR4-2); a fully accepted line is not
/// listed. Another company's orders are invisible under the tenant filter. The cursor is opaque and bound to the company
/// that read it: a malformed or foreign cursor is <see cref="ArgumentException"/> (the consumer's 400); the last page
/// answers a null cursor. The port carries NO authority (the LOG-4 D6 shape): the consumer measures its own name before
/// and after asking. Reading here creates nothing. PRICE-FREE by construction: Purchasing holds no price (T28), and no
/// price, discount, currency or payment member exists on <see cref="PurchaseOpenLineReferenceV1"/>.
/// </para>
/// </summary>
public interface IPurchaseOpenLineReadPortV1
{
    Task<PurchaseOpenLinePageV1> ListOpenApprovedLinesAsync(int pageSize, string? cursor, CancellationToken cancellationToken = default);
}

public sealed record PurchaseOpenLinePageV1(IReadOnlyList<PurchaseOpenLineReferenceV1> Items, string? NextCursor);

/// <summary>
/// One approved purchase order line waiting for goods receipt, as stored: the company, the order (id + business date) and
/// the line (id, number, kind), the supplier (party + company account), the product and its answered version, the Settings
/// unit, and the exact quantity texts (Ö2) — committed (ordered − technical slices), accepted, remaining. Identities and
/// text only — no price, no stock.
/// </summary>
public sealed record PurchaseOpenLineReferenceV1(
    Guid CompanyId,
    Guid OrderId,
    DateOnly OrderEffectiveOn,
    Guid LineId,
    int LineNo,
    string Kind,
    Guid SupplierPartyId,
    Guid CompanyAccountId,
    Guid ProductId,
    string ProductVersion,
    Guid UnitId,
    string CommittedText,
    string AcceptedText,
    string RemainingText);
