namespace GoymenERP.Modules.Purchasing.Domain.GoodsReceipts;

/// <summary>The life cycle of a bulk goods receipt (KLN-D43, KLN-D45).</summary>
public enum PurchaseGoodsReceiptBatchStatus
{
    /// <summary>The kg difference is outside the tolerance (or no tolerance is set): no stock yet, an approver decides.</summary>
    PendingApproval = 1,

    /// <summary>Every label was received into stock and accepted on the order line.</summary>
    Posted = 2,

    /// <summary>The approver refused the difference; no stock was received and the label codes are released.</summary>
    Rejected = 3,
}

/// <summary>The stable refusal codes of the bulk goods receipt decision rules.</summary>
public static class PurchaseGoodsReceiptBatchRuleCodes
{
    /// <summary>Only a batch that waits for approval is approved or rejected.</summary>
    public const string NotPending = "purchasing_goods_receipt_batch_not_pending";

    /// <summary>KLN-D45 (hazırlayan ≠ onaylayan): the preparer cannot approve or reject the same batch.</summary>
    public const string SameActor = "purchasing_goods_receipt_batch_same_actor";

    /// <summary>Fail-closed: the preparer is not recorded, so maker ≠ checker cannot be measured.</summary>
    public const string MakerUnknown = "purchasing_goods_receipt_batch_maker_unknown";

    /// <summary>A rejection needs a reason.</summary>
    public const string RejectionReasonRequired = "purchasing_goods_receipt_batch_rejection_reason_required";

    /// <summary>A rejection reason longer than the stored bound is refused, never cut.</summary>
    public const string RejectionReasonTooLong = "purchasing_goods_receipt_batch_rejection_reason_too_long";
}

/// <summary>
/// ALC-S16 (F-027): the decision state of one bulk goods receipt. Stock and order acceptance are written by the
/// receipt participants, never here; this type only holds the approval rule (KLN-D45: preparer ≠ approver).
/// </summary>
public sealed class PurchaseGoodsReceiptBatch
{
    /// <summary>The stored bound of a rejection reason.</summary>
    public const int MaximumReasonLength = 500;

    public PurchaseGoodsReceiptBatch(Guid id, PurchaseGoodsReceiptBatchStatus status, long version, string? preparedBy)
    {
        Id = Identity.Require(id, nameof(id));
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        Status = status;
        Version = version;
        PreparedBy = preparedBy;
    }

    public Guid Id { get; }

    public PurchaseGoodsReceiptBatchStatus Status { get; private set; }

    public long Version { get; private set; }

    public string? PreparedBy { get; }

    public string? DecidedBy { get; private set; }

    public string? Reason { get; private set; }

    /// <summary>Approves the pending difference; the caller then posts the labels in the same transaction.</summary>
    public void Approve(long expectedVersion, string actorId) => Decide(expectedVersion, actorId, PurchaseGoodsReceiptBatchStatus.Posted, reason: null);

    /// <summary>Rejects the pending difference with a reason; no stock is received.</summary>
    public void Reject(long expectedVersion, string actorId, string? reason)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new PurchasingRuleException(PurchaseGoodsReceiptBatchRuleCodes.RejectionReasonRequired, "Red gerekçesi zorunludur.");
        }

        if (trimmed.Length > MaximumReasonLength)
        {
            throw new PurchasingRuleException(PurchaseGoodsReceiptBatchRuleCodes.RejectionReasonTooLong, "Red gerekçesi çok uzun.");
        }

        Decide(expectedVersion, actorId, PurchaseGoodsReceiptBatchStatus.Rejected, trimmed);
    }

    private void Decide(long expectedVersion, string actorId, PurchaseGoodsReceiptBatchStatus target, string? reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        if (expectedVersion != Version)
        {
            throw new PurchasingVersionConflictException(expectedVersion, Version);
        }

        if (Status != PurchaseGoodsReceiptBatchStatus.PendingApproval)
        {
            throw new PurchasingRuleException(PurchaseGoodsReceiptBatchRuleCodes.NotPending, "Toplu mal kabul onay beklemiyor.");
        }

        if (string.IsNullOrWhiteSpace(PreparedBy))
        {
            throw new PurchasingRuleException(PurchaseGoodsReceiptBatchRuleCodes.MakerUnknown, "Toplu mal kabulü hazırlayan bilinmiyor.");
        }

        if (string.Equals(PreparedBy, actorId, StringComparison.Ordinal))
        {
            throw new PurchasingRuleException(PurchaseGoodsReceiptBatchRuleCodes.SameActor, "Hazırlayan kendi toplu mal kabulünü onaylayamaz veya reddedemez.");
        }

        Status = target;
        DecidedBy = actorId;
        Reason = reason;
        Version++;
    }
}
