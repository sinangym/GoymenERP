namespace GoymenERP.Modules.Purchasing.Domain.Returns;

/// <summary>The life cycle of one supplier return line (B-19; SAT-D392, SAT-D393).</summary>
public enum PurchaseSupplierReturnStatus
{
    /// <summary>SAT-D393: the invoice unit cost could not be read; a manual cost waits for its exception approval. No stock left yet.</summary>
    PendingCostException = 1,

    /// <summary>The goods left stock (Inventory's supplier-return exit is staged) at the recorded unit cost.</summary>
    Recorded = 2,

    /// <summary>The cost exception was refused; no stock left and the return is closed.</summary>
    Rejected = 3,
}

/// <summary>Where the unit cost a return leaves stock at came from (SAT-D306, SAT-D392, SAT-D393).</summary>
public enum PurchaseSupplierReturnCostBasis
{
    /// <summary>SAT-D392: the unit cost of the invoice that bills the related order's receipt.</summary>
    InvoiceUnitCost = 1,

    /// <summary>SAT-D393: the invoice cost could not be read; a manual cost entered with the return, valid only after its exception approval.</summary>
    ManualCostWithException = 2,
}

/// <summary>The stable refusal codes of the supplier return cost-exception rules.</summary>
public static class PurchaseSupplierReturnRuleCodes
{
    /// <summary>Only a return that waits for its cost exception is approved or rejected.</summary>
    public const string NotPending = "purchasing_supplier_return_not_pending";

    /// <summary>
    /// The person who entered the manual cost decides the exception. SAT-D396 leaves same-person approval undecided
    /// (OPEN, NEEDS-SINAN), so the default is fail-closed: the decider must be someone else.
    /// </summary>
    public const string SameActor = "purchasing_supplier_return_same_actor";

    /// <summary>Fail-closed: the person who entered the return is not recorded, so a distinct decider cannot be measured.</summary>
    public const string MakerUnknown = "purchasing_supplier_return_maker_unknown";

    /// <summary>A rejection needs a reason.</summary>
    public const string RejectionReasonRequired = "purchasing_supplier_return_rejection_reason_required";

    /// <summary>A rejection reason longer than the stored bound is refused, never cut.</summary>
    public const string RejectionReasonTooLong = "purchasing_supplier_return_rejection_reason_too_long";
}

/// <summary>
/// ALC-S5 (B-19; SAT-D306, SAT-D392, SAT-D393, SAT-D396): the cost-exception decision state of one supplier return line.
/// The stock exit itself is written by Inventory's supplier-return exit participant, never here; this type only holds the
/// exception approval rule. Whether the decider may be the person who entered the return is a policy input
/// (<paramref name="allowSameActorDecision"/> in <see cref="Approve"/>/<see cref="Reject"/>) that callers keep switched off
/// until it is decided.
/// </summary>
public sealed class PurchaseSupplierReturn
{
    /// <summary>The stored bound of a return reason and of a rejection reason.</summary>
    public const int MaximumReasonLength = 500;

    public PurchaseSupplierReturn(Guid id, PurchaseSupplierReturnStatus status, long version, string? requestedBy)
    {
        Id = Identity.Require(id, nameof(id));
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        Status = status;
        Version = version;
        RequestedBy = requestedBy;
    }

    public Guid Id { get; }

    public PurchaseSupplierReturnStatus Status { get; private set; }

    public long Version { get; private set; }

    public string? RequestedBy { get; }

    public string? DecidedBy { get; private set; }

    public string? Reason { get; private set; }

    /// <summary>SAT-D393/SAT-D396: approves the manual cost; the caller then stages the stock exit in the same transaction.</summary>
    public void Approve(long expectedVersion, string actorId, bool allowSameActorDecision = false) =>
        Decide(expectedVersion, actorId, PurchaseSupplierReturnStatus.Recorded, reason: null, allowSameActorDecision);

    /// <summary>Refuses the manual cost with a reason; no stock leaves.</summary>
    public void Reject(long expectedVersion, string actorId, string? reason, bool allowSameActorDecision = false)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new PurchasingRuleException(PurchaseSupplierReturnRuleCodes.RejectionReasonRequired, "Red gerekçesi zorunludur.");
        }

        if (trimmed.Length > MaximumReasonLength)
        {
            throw new PurchasingRuleException(PurchaseSupplierReturnRuleCodes.RejectionReasonTooLong, "Red gerekçesi çok uzun.");
        }

        Decide(expectedVersion, actorId, PurchaseSupplierReturnStatus.Rejected, trimmed, allowSameActorDecision);
    }

    private void Decide(long expectedVersion, string actorId, PurchaseSupplierReturnStatus target, string? reason, bool allowSameActorDecision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        if (expectedVersion != Version)
        {
            throw new PurchasingVersionConflictException(expectedVersion, Version);
        }

        if (Status != PurchaseSupplierReturnStatus.PendingCostException)
        {
            throw new PurchasingRuleException(PurchaseSupplierReturnRuleCodes.NotPending, "Tedarikçi iadesi maliyet istisnası onayı beklemiyor.");
        }

        if (!allowSameActorDecision)
        {
            if (string.IsNullOrWhiteSpace(RequestedBy))
            {
                throw new PurchasingRuleException(PurchaseSupplierReturnRuleCodes.MakerUnknown, "Tedarikçi iadesini giren kişi bilinmiyor.");
            }

            if (string.Equals(RequestedBy, actorId, StringComparison.Ordinal))
            {
                throw new PurchasingRuleException(PurchaseSupplierReturnRuleCodes.SameActor,
                    "İadeyi giren kişi kendi maliyet istisnasını onaylayamaz veya reddedemez.");
            }
        }

        Status = target;
        DecidedBy = actorId;
        Reason = reason;
        Version++;
    }
}
