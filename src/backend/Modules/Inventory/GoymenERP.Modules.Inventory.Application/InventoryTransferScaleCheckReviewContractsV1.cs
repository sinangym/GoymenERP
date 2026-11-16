namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// LOJ-D20 (SINAN-CEVAP 33.21): when the source warehouse scale setting ("kantardan geçer", KLN-D25 (7)(f)) cannot be read
/// at dispatch, the dispatch is not stopped: it is recorded with the setting stored as unreadable, which is the weighing
/// check's "kontrol edilemedi" result, and the dispatch is listed here for later review. This list only reads what the
/// dispatch stored; it changes nothing, closes nothing and chooses no tolerance. A dispatch whose setting was read (on or
/// off) is never listed, and the behavior when a read tolerance is exceeded is unchanged (the decision's limits).
/// <para>
/// The list's location and the status code name are revisable implementation details (LOJ-D20). The read is fenced like
/// the transfer read (<c>inventory.balances.read</c>, company-wide); no new permission, grant or role is introduced.
/// </para>
/// </summary>
public static class InventoryTransferScaleCheckReviewApiV1
{
    public const string Path = "/api/v1/inventory/transfers/scale-check-review";

    /// <summary>The page size when the caller names none.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The largest page a caller may ask for; a larger limit is a 400.</summary>
    public const int MaximumLimit = 200;
}

/// <summary>Stable codes of the weighing check result a dispatch stored (only the unchecked one is listed today).</summary>
public static class InventoryTransferScaleCheckStatusV1
{
    /// <summary>"Kontrol edilemedi": the source warehouse scale setting could not be read at dispatch.</summary>
    public const string NotChecked = "NotChecked";
}

/// <summary>
/// One dispatch whose weighing check could not run. <see cref="TransferVersion"/> and <see cref="TransferStatus"/> are the
/// transfer's current values (it may have moved on since the dispatch); <see cref="DispatchedAtUtc"/> is the stored dispatch
/// moment and <see cref="ScaleCheckStatus"/> is <see cref="InventoryTransferScaleCheckStatusV1.NotChecked"/>.
/// </summary>
public sealed record InventoryTransferScaleCheckReviewItemV1(
    Guid TransferId,
    string TransferVersion,
    string TransferStatus,
    Guid SourceLocationId,
    Guid DestinationLocationId,
    DateTimeOffset DispatchedAtUtc,
    string ScaleCheckStatus);

/// <summary>
/// A page of the review list, newest dispatch first (ties by transfer id). <see cref="HasMore"/>: more unchecked dispatches
/// exist than the page holds.
/// </summary>
public sealed record InventoryTransferScaleCheckReviewPageV1(
    Guid CompanyId,
    IReadOnlyList<InventoryTransferScaleCheckReviewItemV1> Items,
    bool HasMore);

/// <summary>
/// Inventory's own read of the dispatches stored with an unreadable source scale setting, under the server-resolved
/// company only (no context → <see cref="InvalidOperationException"/>, never an unscoped read). Untracked, side-effect free,
/// no authority: the query service measures it.
/// </summary>
public interface IInventoryTransferScaleCheckReviewReaderV1
{
    Task<InventoryTransferScaleCheckReviewPageV1> ListNotCheckedAsync(int limit, CancellationToken cancellationToken = default);
}

/// <summary>The fenced review query the endpoint calls (401/403/400/503 as <see cref="InventoryTransferException"/>).</summary>
public interface IInventoryTransferScaleCheckReviewQueryV1
{
    Task<InventoryTransferScaleCheckReviewPageV1> ListNotCheckedAsync(int? limit, CancellationToken cancellationToken = default);
}
