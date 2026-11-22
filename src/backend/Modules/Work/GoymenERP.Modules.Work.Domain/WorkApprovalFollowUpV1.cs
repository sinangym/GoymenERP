namespace GoymenERP.Modules.Work.Domain;

/// <summary>
/// WP2 (YOL-D17, YOL-D28, AI-D29): the steps Work takes for one pending approval cycle. There is no automatic approval or
/// rejection: Work only tells people.
/// </summary>
public enum WorkApprovalFollowUpStepV1
{
    /// <summary>The eligible approvers were told the approval waits ("şimdi", AI-D29).</summary>
    Waiting = 1,

    /// <summary>The one reminder after the duration ran out (YOL-D17), to the eligible approvers.</summary>
    Reminder = 2,

    /// <summary>Escalated to the holders of the rule's escalation permission (YOL-D28; the "onay yükseltme" permission by default).</summary>
    Escalated = 3,

    /// <summary>No eligible approver: the cycle is a "sahipsiz onay" (YOL-D17, ROL-D3).</summary>
    Orphaned = 4,
}

public static class WorkApprovalFollowUpStepsV1
{
    public static string Wire(int step) => step switch
    {
        (int)WorkApprovalFollowUpStepV1.Waiting => "waiting",
        (int)WorkApprovalFollowUpStepV1.Reminder => "reminder",
        (int)WorkApprovalFollowUpStepV1.Escalated => "escalated",
        (int)WorkApprovalFollowUpStepV1.Orphaned => "orphaned",
        _ => throw new InvalidOperationException("Unknown approval follow-up step."),
    };

    public static WorkInboxNotificationKindV1 NotificationKind(WorkApprovalFollowUpStepV1 step) => step switch
    {
        WorkApprovalFollowUpStepV1.Waiting => WorkInboxNotificationKindV1.ApprovalWaiting,
        WorkApprovalFollowUpStepV1.Reminder => WorkInboxNotificationKindV1.ApprovalReminder,
        WorkApprovalFollowUpStepV1.Escalated => WorkInboxNotificationKindV1.ApprovalEscalated,
        WorkApprovalFollowUpStepV1.Orphaned => WorkInboxNotificationKindV1.ApprovalOrphaned,
        _ => throw new InvalidOperationException("Unknown approval follow-up step."),
    };
}

/// <summary>The follow-up views (<c>GET /api/v1/work/approval-follow-ups?view=…</c>).</summary>
public static class WorkApprovalFollowUpViewsV1
{
    /// <summary>Escalated cycles still open, for a holder of their escalation permission.</summary>
    public const string Escalated = "escalated";

    /// <summary>Cycles still open without an eligible approver ("sahipsiz onay").</summary>
    public const string Orphaned = "orphaned";
}

/// <summary>
/// One open follow-up item: the owner's kind, record, label and screen, since when it waits, the step, the approver and
/// escalation permissions it was followed up under, the due time Work computed and when Work took the step. The decision is
/// given on <see cref="Link"/> with the owner's command and the owner's approver permission (the follow-up decides nothing).
/// </summary>
public sealed record WorkApprovalFollowUpItemV1(
    string Kind,
    Guid ItemId,
    string DocumentLabel,
    string Link,
    DateTimeOffset WaitingSinceUtc,
    string Step,
    string ApproverPermission,
    string? EscalationPermission,
    string RuleVersion,
    DateTimeOffset? DueAtUtc,
    DateTimeOffset RecordedAtUtc,
    int RecipientCount);

public sealed record WorkApprovalFollowUpPageV1(Guid CompanyId, string View, IReadOnlyList<WorkApprovalFollowUpItemV1> Items, bool HasMore);

/// <summary>
/// WP2: the follow-up views. Both need <c>work.approvals.escalate</c> company-wide now (YOL-D28); an escalated item whose rule
/// named another escalation permission is shown only to a holder of that permission too. Only Work's own rows are read;
/// an item leaves the view when the owner's decision (D10) or the observed resolution closes its cycle.
/// </summary>
public interface IWorkApprovalFollowUpReadV1
{
    Task<WorkApprovalFollowUpPageV1> ListAsync(string? view, CancellationToken cancellationToken = default);
}

public sealed class WorkApprovalFollowUpException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

/// <summary>The counts of one follow-up cycle of one company (system work; scheduler only).</summary>
public sealed record WorkApprovalFollowUpCycleV1(
    int KindsFollowed,
    int KindsUnavailable,
    int Waiting,
    int Reminders,
    int Escalations,
    int Orphaned,
    int Notifications,
    int DecisionsRecorded);

/// <summary>Scheduler work, run per company only when <c>Work:CollaborationScheduler:ApprovalFollowUpEnabled</c> is true.</summary>
public interface IWorkApprovalFollowUpMaintenanceV1
{
    Task<WorkApprovalFollowUpCycleV1> RunCycleAsync(CancellationToken cancellationToken = default);
}
