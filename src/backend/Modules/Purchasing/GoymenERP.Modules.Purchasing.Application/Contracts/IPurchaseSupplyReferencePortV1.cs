namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// PUR-4a: the Purchasing-owned read port the chain's consumers — Inventory allocation (INV-AL-1, the
/// "gelecek arz" source share of Sinan #66 5738113623 §1) — ask before they create their own records; the
/// mirror of Sales's <c>ISalesOrderLineReferencePortV1</c>. It answers an APPROVED purchase order line's
/// remaining commitment (committed − accepted) as EXPECTED SUPPLY, never as stock: an approved order books
/// nothing in Inventory, and the physical receipt is Inventory's own event (T09, PUR-4b). A Draft order's
/// line is <see cref="PurchaseSupplyReferenceOutcomeV1.NotApproved"/> (Draft orders reserve nothing,
/// ASSUMPTION-PUR4-2); an unknown or another company's order or line is
/// <see cref="PurchaseSupplyReferenceOutcomeV1.NotFound"/> (never 403). Reading here creates nothing.
/// </summary>
public interface IPurchaseSupplyReferencePortV1
{
    Task<PurchaseSupplyReferenceAnswerV1> GetApprovedLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default);
}

public enum PurchaseSupplyReferenceOutcomeV1
{
    /// <summary>The order is approved; <see cref="PurchaseSupplyReferenceAnswerV1.Line"/> carries the line and its remaining commitment.</summary>
    Approved = 1,

    /// <summary>The order exists in the context company but is not approved; <see cref="PurchaseSupplyReferenceAnswerV1.Status"/> says its state. Fail-closed: no supply.</summary>
    NotApproved = 2,

    /// <summary>No such order or line in the context company.</summary>
    NotFound = 3,
}

public sealed record PurchaseSupplyReferenceAnswerV1(PurchaseSupplyReferenceOutcomeV1 Outcome, PurchaseSupplyLineReferenceV1? Line, string? Status);

/// <summary>
/// The approved line's facts as stored: the company, the order and the line, the supplier (party + company
/// account), the product and its version as the Product owner answered it, the Settings unit, the line's
/// kind, and the exact quantity texts (Ö2): committed (ordered − technical slices), accepted (shares) and
/// remaining = committed − accepted (the expected supply). Identities and text only — no price, no stock.
/// </summary>
public sealed record PurchaseSupplyLineReferenceV1(
    Guid CompanyId,
    Guid OrderId,
    Guid LineId,
    int LineNo,
    Guid SupplierPartyId,
    Guid CompanyAccountId,
    Guid? SourceRequestId,
    Guid? SourceRequestLineId,
    Guid ProductId,
    string ProductVersion,
    Guid UnitId,
    string Kind,
    string CommittedText,
    string AcceptedText,
    string RemainingText,
    long Version);
