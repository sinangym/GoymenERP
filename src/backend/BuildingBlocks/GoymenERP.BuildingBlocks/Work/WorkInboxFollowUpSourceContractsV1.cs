namespace GoymenERP.BuildingBlocks.Work;

/// <summary>
/// WP2 (YOL-D17, YOL-D28, AI-D29, D10): the part of an approval source that Work's follow-up cycle (reminder, escalation,
/// "sahipsiz onay", the "şimdi" notification and the owner's exact decision time) needs while no person is asking. A source
/// that does not implement it is simply not followed up: its kind gets no reminder or escalation (fail closed) and its
/// measurement keeps the inbox-read observation of W1-A.
/// <para>
/// Both reads run under the server-resolved company only (the scheduler sets it; no user is impersonated) and return no more
/// than the inbox read returns to an approver: the owner's pending records with the same item rules
/// (<see cref="WorkInboxItemV1"/>, including the maker and the owner's further exclusions, and the scope for an owner that
/// accepts scoped approvers). Work never decides: the decision stays the owner's command on <see cref="WorkInboxItemV1.Link"/>.
/// </para>
/// </summary>
public interface IWorkInboxFollowUpSourceV1 : IWorkInboxSourceV1
{
    /// <summary>
    /// Every pending record of the kind in <paramref name="companyId"/>, oldest first, at most <paramref name="maxCount"/>
    /// (<see cref="WorkInboxSourceResultV1.HasMore"/> when the owner holds more). <see cref="NativeApprovalQueueOutcomeV1.Denied"/>
    /// when the resolved company is not <paramref name="companyId"/>; <see cref="NativeApprovalQueueOutcomeV1.Unavailable"/> on an
    /// owner failure.
    /// </summary>
    Task<WorkInboxSourceResultV1> ListPendingForFollowUpAsync(
        Guid companyId,
        int maxCount,
        CancellationToken cancellationToken);

    /// <summary>
    /// D10: the owner's own decision of one cycle (the record <paramref name="itemId"/> waiting since
    /// <paramref name="waitingSinceUtc"/>) — approval, rejection, withdrawal or cancellation — at the exact time the owner
    /// recorded it.
    /// </summary>
    Task<WorkInboxDecisionAnswerV1> ReadDecisionAsync(
        Guid companyId,
        Guid itemId,
        DateTimeOffset waitingSinceUtc,
        CancellationToken cancellationToken);
}

public enum WorkInboxDecisionOutcomeV1
{
    /// <summary>The owner could not answer now; Work asks again on its next cycle.</summary>
    Unavailable = 0,

    /// <summary>The cycle is still waiting for a decision.</summary>
    StillPending = 1,

    /// <summary>The owner decided the cycle at <see cref="WorkInboxDecisionAnswerV1.DecidedAtUtc"/>.</summary>
    Decided = 2,

    /// <summary>
    /// The cycle ended without a decision the owner records with a time (e.g. the record left the approval state through
    /// another record, or a resubmission started a new cycle); Work keeps its observed resolution only.
    /// </summary>
    NoDecisionRecorded = 3,
}

/// <summary>
/// The owner's answer. <see cref="DecidedAtUtc"/> (UTC, offset zero, never before the cycle began) and
/// <see cref="Decision"/> (<c>approved</c>, <c>rejected</c>, <c>withdrawn</c> or <c>cancelled</c>) are set only for
/// <see cref="WorkInboxDecisionOutcomeV1.Decided"/>.
/// </summary>
public sealed record WorkInboxDecisionAnswerV1(
    WorkInboxDecisionOutcomeV1 Outcome,
    DateTimeOffset? DecidedAtUtc,
    string? Decision)
{
    public static WorkInboxDecisionAnswerV1 Unavailable() => new(WorkInboxDecisionOutcomeV1.Unavailable, null, null);

    public static WorkInboxDecisionAnswerV1 StillPending() => new(WorkInboxDecisionOutcomeV1.StillPending, null, null);

    public static WorkInboxDecisionAnswerV1 NoDecisionRecorded() => new(WorkInboxDecisionOutcomeV1.NoDecisionRecorded, null, null);

    public static WorkInboxDecisionAnswerV1 Decided(DateTimeOffset decidedAtUtc, string decision) =>
        new(WorkInboxDecisionOutcomeV1.Decided, decidedAtUtc.ToUniversalTime(), decision);
}

/// <summary>The decision names a follow-up source may report (<see cref="WorkInboxDecisionAnswerV1.Decision"/>).</summary>
public static class WorkInboxDecisionsV1
{
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Withdrawn = "withdrawn";
    public const string Cancelled = "cancelled";

    public static bool IsKnown(string? decision) => decision is Approved or Rejected or Withdrawn or Cancelled;

    /// <summary>A decided answer is well formed for the cycle it answers.</summary>
    public static bool IsValid(WorkInboxDecisionAnswerV1? answer, DateTimeOffset waitingSinceUtc) =>
        answer is not null
        && (answer.Outcome == WorkInboxDecisionOutcomeV1.Decided
            ? answer.DecidedAtUtc is { } at && at.Offset == TimeSpan.Zero && at >= waitingSinceUtc && IsKnown(answer.Decision)
            : answer.DecidedAtUtc is null && answer.Decision is null && Enum.IsDefined(answer.Outcome));
}
