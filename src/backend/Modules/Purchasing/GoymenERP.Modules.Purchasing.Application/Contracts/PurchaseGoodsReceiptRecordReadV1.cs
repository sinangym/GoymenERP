namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// YOL-D42 (E-81, WP9): the goods receipt (mal kabul) addressed by its acceptance id alone, so another module (Work's record
/// notes and tasks) can ask "does this receipt exist in the resolved company and may the current actor read it?" without
/// knowing its order and without touching Purchasing's tables. The answer is the owner's own authorized receipt read: the
/// order read permission company-wide, the company fence and the not-found rules of <see cref="IPurchaseGoodsReceiptServiceV1"/>.
/// </summary>
public sealed record PurchaseGoodsReceiptRecordRefV1(Guid CompanyId, Guid PurchaseOrderId, Guid AcceptanceId);

/// <summary>
/// Throws <see cref="PurchasingServiceException"/> exactly as the receipt read does: 401/403 for the caller (decided BEFORE
/// anything about the receipt is disclosed), 404 <c>not_found</c> for a receipt that is not in the company, 503 when the
/// receipt read is not composed.
/// </summary>
public interface IPurchaseGoodsReceiptRecordReadPortV1
{
    Task<PurchaseGoodsReceiptRecordRefV1> ReadByAcceptanceAsync(Guid acceptanceId, CancellationToken cancellationToken = default);
}

/// <summary>Purchasing-internal: the order of a stored acceptance in the resolved company, or null.</summary>
public interface IPurchaseAcceptanceOrderLookupV1
{
    Task<Guid?> FindOrderIdAsync(Guid acceptanceId, CancellationToken cancellationToken = default);
}
