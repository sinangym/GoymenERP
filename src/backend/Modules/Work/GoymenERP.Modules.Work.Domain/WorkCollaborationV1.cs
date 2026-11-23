namespace GoymenERP.Modules.Work.Domain;

/// <summary>
/// W1-C (DALGA_PLANI_v2 1.7/1.8; AI-D20, AI-D23, AI-D29, YOL-D19): record-bound notes, mentions, tasks and the two
/// notification classes. No AI is involved anywhere in this surface. The record types are the ones the slice starts with;
/// whether a record exists and whether a person may read it is answered by the record's OWNER module through
/// <see cref="IWorkRecordAccessPortV1"/>; Work never reads another module's tables.
/// </summary>
public enum WorkRecordTypeV1
{
    SalesOrderDraft = 1,
    PurchaseOrder = 2,
    StockLabel = 3,
    ManufacturingPlan = 4,
    Counterparty = 5,
    // WP9 (AI-D20/AI-D23, DALGA_PLANI_v2 1.7): the other main record kinds. A dispatch note (irsaliye) has no read of its own
    // in Logistics — it is a child of its loading instruction — so its notes are the instruction's. YOL-D42 (E-81) adds the
    // purchase goods receipt (mal kabul), addressed by its acceptance id through Purchasing's read port.
    SalesInvoice = 6,
    LoadingInstruction = 7,
    PurchaseIncomingInvoice = 8,
    ManufacturingOrder = 9,
    FinanceManualReceipt = 10,
    FinanceManualBankPayment = 11,
    SalesQuote = 12,
    PurchaseGoodsReceipt = 13,
}

public static class WorkRecordTypesV1
{
    private static readonly (WorkRecordTypeV1 Type, string Wire)[] Names =
    [
        (WorkRecordTypeV1.SalesOrderDraft, "sales-order-draft"),
        (WorkRecordTypeV1.PurchaseOrder, "purchase-order"),
        (WorkRecordTypeV1.StockLabel, "stock-label"),
        (WorkRecordTypeV1.ManufacturingPlan, "manufacturing-plan"),
        (WorkRecordTypeV1.Counterparty, "counterparty"),
        (WorkRecordTypeV1.SalesInvoice, "sales-invoice"),
        (WorkRecordTypeV1.LoadingInstruction, "loading-instruction"),
        (WorkRecordTypeV1.PurchaseIncomingInvoice, "purchase-incoming-invoice"),
        (WorkRecordTypeV1.ManufacturingOrder, "manufacturing-order"),
        (WorkRecordTypeV1.FinanceManualReceipt, "finance-manual-receipt"),
        (WorkRecordTypeV1.FinanceManualBankPayment, "finance-manual-bank-payment"),
        (WorkRecordTypeV1.SalesQuote, "sales-quote"),
        (WorkRecordTypeV1.PurchaseGoodsReceipt, "purchase-goods-receipt"),
    ];

    /// <summary>The persisted values, as the check constraints list them.</summary>
    public static IReadOnlyList<int> StoredValues { get; } = Names.Select(name => (int)name.Type).Order().ToArray();

    public static IReadOnlyList<string> WireNames { get; } = Names.Select(name => name.Wire).ToArray();

    public static bool TryParse(string? wire, out WorkRecordTypeV1 type)
    {
        foreach (var (candidate, name) in Names)
        {
            if (string.Equals(name, wire, StringComparison.Ordinal))
            {
                type = candidate;
                return true;
            }
        }

        type = default;
        return false;
    }

    public static bool IsKnown(int value) => Names.Any(name => (int)name.Type == value);

    public static string Wire(WorkRecordTypeV1 type) =>
        Names.Single(name => name.Type == type).Wire;
}

public sealed record WorkRecordRefV1(WorkRecordTypeV1 Type, Guid RecordId);

/// <summary>
/// The owner's answer. <see cref="NotReadable"/> covers both "no such record in this company" and "the person may not read
/// it", so nothing about the record leaks to a caller who cannot see it. <see cref="Unavailable"/> is an owner failure and
/// always fails closed.
/// </summary>
public enum WorkRecordAccessOutcomeV1
{
    Unavailable = 0,
    Readable = 1,
    NotReadable = 2,
}

/// <summary>
/// Declared by Work, implemented by the host over each owner's own read surface. It carries no authority of its own: the
/// current actor's answer comes from the owner's authorized read (the owner's permission and company fence apply), a named
/// user's answer from the owner's read permission held company-wide by that user now (Identity measures it) after the record
/// was found readable by the actor. Reads only the server-resolved company; creates nothing.
/// </summary>
public interface IWorkRecordAccessPortV1
{
    Task<WorkRecordAccessOutcomeV1> CheckCurrentActorAsync(Guid companyId, WorkRecordRefV1 record,
        CancellationToken cancellationToken = default);

    Task<WorkRecordAccessOutcomeV1> CheckUserAsync(Guid companyId, WorkRecordRefV1 record, Guid userId,
        CancellationToken cancellationToken = default);
}

public enum WorkPersonOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    Found = 2,
}

public sealed record WorkPersonV1(Guid UserId, string DisplayName, bool IsActiveInCompany);

public sealed record WorkPeopleAnswerV1(WorkPersonOutcomeV1 Outcome, IReadOnlyList<WorkPersonV1> People, bool HasMore);

/// <summary>
/// WP9 / D7: one picker row on the wire — the person's id and display name and nothing else (no membership flag, no e-mail,
/// no role). Only active members of the server-resolved company are ever listed.
/// </summary>
public sealed record WorkPersonSummaryV1(Guid UserId, string DisplayName);

/// <summary>
/// The picker's wire answer: active people of the server-resolved company only. WP9 / D7: the picker is a tool of the two
/// commands that name a person (a mention, an assignee), so it answers only a caller who holds <c>work.tasks.assign</c> or
/// <c>work.notes.write</c> company-wide now; anyone else gets the code-less 403.
/// </summary>
public sealed record WorkPeoplePageV1(Guid CompanyId, IReadOnlyList<WorkPersonSummaryV1> People, bool HasMore);

/// <summary>Identity-backed directory for display names, the mention/assignee picker and "is this person a member here now".</summary>
public interface IWorkPersonDirectoryV1
{
    Task<WorkPeopleAnswerV1> ResolveAsync(Guid companyId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default);

    Task<WorkPeopleAnswerV1> SearchAsync(Guid companyId, string? query, CancellationToken cancellationToken = default);

    /// <summary>Active member of the company now; <see cref="WorkPersonOutcomeV1.Unavailable"/> fails closed.</summary>
    Task<WorkPersonOutcomeV1> IsActiveMemberAsync(Guid companyId, Guid userId, CancellationToken cancellationToken = default);
}

public static class WorkCollaborationLimitsV1
{
    public const int NoteBodyMaximumLength = 4000;
    public const int MentionMaximumCount = 20;
    public const int TaskTitleMaximumLength = 200;
    public const int TaskDetailsMaximumLength = 2000;
    public const int ReasonMaximumLength = 500;
    public const int PageSize = 100;
    public const int PeopleQueryMaximumLength = 100;
    public const int NoteReadsMaximumCount = 100;
}

public static class WorkCollaborationCommandsV1
{
    public const string AddNote = "work.note.add.v1";
    public const string PassivateNote = "work.note.passivate.v1";
    public const string CreateTask = "work.task.create.v1";
    public const string CompleteTask = "work.task.complete.v1";
    public const string CancelTask = "work.task.cancel.v1";
}

public static class WorkCollaborationProblemCodesV1
{
    public const string ValidationFailed = "validation_failed";
    public const string CommandMetadataInvalid = "command_metadata_invalid";
    public const string VersionConflict = "version_conflict";
    public const string SameKeyPayloadMismatch = "same_key_payload_mismatch";
    public const string ConcurrentChange = "concurrent_change";
    public const string MentionRefused = "work_mention_refused";
    public const string AssigneeRefused = "work_assignee_refused";
    public const string CorrectedNoteInvalid = "work_corrected_note_invalid";
    public const string NoteAlreadyPassive = "work_note_already_passive";
    public const string NoteNotOwn = "work_note_not_own";
    public const string NoteOwnRead = "work_note_own_read";
    public const string TaskNotOpen = "work_task_not_open";
    public const string TaskNotAssignee = "work_task_not_assignee";
    public const string TaskNotAssigner = "work_task_not_assigner";
    public const string PermissionUnregistered = "permission_unregistered";
}

public sealed class WorkCollaborationException(int status, string code, bool isReplay = false, string? originalCorrelationId = null)
    : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public bool IsReplay { get; } = isReplay;
    public string? OriginalCorrelationId { get; } = originalCorrelationId;
}

public sealed record WorkCommandMetadataV1(string IdempotencyKey, string CorrelationId, string? CausationId);

// ---- Notes -----------------------------------------------------------------------------------------------------------

public sealed record AddWorkNoteRequestV1(string Body, IReadOnlyList<Guid>? MentionedUserIds, Guid? CorrectsNoteId);

public sealed record PassivateWorkNoteRequestV1(string Reason);

/// <summary>
/// WP9 (DALGA_PLANI_v2 1.7 "okundu bilgisi"): <see cref="ReadByMeAtUtc"/> is when the caller marked the note read (null when
/// not yet); <see cref="Reads"/> lists who has read it and when, oldest first — the author is never listed (writing a note is
/// not reading it). Anyone who can read the record sees the same list: a read receipt says nothing the record's notes do not.
/// </summary>
public sealed record WorkNoteViewV1(Guid Id, Guid CompanyId, string RecordType, Guid RecordId, Guid AuthorUserId,
    string AuthorDisplayName, string Body, Guid? CorrectsNoteId, IReadOnlyList<Guid> MentionedUserIds,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? PassivatedAtUtc, string? PassivationReason,
    DateTimeOffset? ReadByMeAtUtc, IReadOnlyList<WorkNoteReadV1> Reads);

public sealed record WorkNoteReadV1(Guid UserId, string DisplayName, DateTimeOffset ReadAtUtc);

public sealed record WorkNoteReadResponseV1(Guid NoteId, DateTimeOffset ReadAtUtc);

public sealed record WorkNotePageV1(Guid CompanyId, string RecordType, Guid RecordId, IReadOnlyList<WorkNoteViewV1> Items, bool HasMore);

public sealed record WorkNoteResponseV1(WorkNoteViewV1 Note, bool IsReplay, string OriginalCorrelationId);

// ---- Tasks -----------------------------------------------------------------------------------------------------------

public enum WorkTaskStatusV1
{
    Open = 1,
    Completed = 2,
    Cancelled = 3,
}

public sealed record CreateWorkTaskRequestV1(string Title, string? Details, Guid AssigneeUserId, DateTimeOffset DueAtUtc,
    string? RecordType, Guid? RecordId);

public sealed record CompleteWorkTaskRequestV1(string ExpectedVersion);

public sealed record CancelWorkTaskRequestV1(string ExpectedVersion, string Reason);

public sealed record WorkTaskViewV1(Guid Id, Guid CompanyId, string? RecordType, Guid? RecordId, string Title, string? Details,
    Guid AssignerUserId, string AssignerDisplayName, Guid AssigneeUserId, string AssigneeDisplayName, DateTimeOffset DueAtUtc,
    string Status, string Version, DateTimeOffset CreatedAtUtc, DateTimeOffset? ClosedAtUtc, Guid? ClosedByUserId,
    string? CancelReason);

public sealed record WorkTaskPageV1(Guid CompanyId, IReadOnlyList<WorkTaskViewV1> Items, bool HasMore);

public sealed record WorkTaskResponseV1(WorkTaskViewV1 Task, bool IsReplay, string OriginalCorrelationId);

/// <summary>
/// One level, three states (W1-C): open → completed (assignee) or open → cancelled (assigner). Each step moves the version
/// by exactly one; a closed task never changes again.
/// </summary>
public sealed class WorkTaskV1
{
    private WorkTaskV1(Guid assignerUserId, Guid assigneeUserId, WorkTaskStatusV1 status, long version)
    {
        AssignerUserId = assignerUserId;
        AssigneeUserId = assigneeUserId;
        Status = status;
        Version = version;
    }

    public Guid AssignerUserId { get; }
    public Guid AssigneeUserId { get; }
    public WorkTaskStatusV1 Status { get; private set; }
    public long Version { get; private set; }
    public Guid? ClosedByUserId { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public string? CancelReason { get; private set; }

    public static WorkTaskV1 Restore(Guid assignerUserId, Guid assigneeUserId, WorkTaskStatusV1 status, long version)
    {
        if (assignerUserId == Guid.Empty || assigneeUserId == Guid.Empty || version < 1 || !Enum.IsDefined(status))
        {
            throw new InvalidOperationException("Stored work task is invalid.");
        }

        return new WorkTaskV1(assignerUserId, assigneeUserId, status, version);
    }

    public void Complete(long expectedVersion, Guid actor, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        if (Status != WorkTaskStatusV1.Open) throw new WorkTaskRuleException(WorkCollaborationProblemCodesV1.TaskNotOpen);
        if (actor != AssigneeUserId) throw new WorkTaskRuleException(WorkCollaborationProblemCodesV1.TaskNotAssignee);
        Status = WorkTaskStatusV1.Completed;
        Close(actor, now);
    }

    public void Cancel(long expectedVersion, Guid actor, string reason, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        if (Status != WorkTaskStatusV1.Open) throw new WorkTaskRuleException(WorkCollaborationProblemCodesV1.TaskNotOpen);
        if (actor != AssignerUserId) throw new WorkTaskRuleException(WorkCollaborationProblemCodesV1.TaskNotAssigner);
        Status = WorkTaskStatusV1.Cancelled;
        CancelReason = reason;
        Close(actor, now);
    }

    private void RequireVersion(long expectedVersion)
    {
        if (expectedVersion != Version) throw new WorkTaskRuleException(WorkCollaborationProblemCodesV1.VersionConflict);
    }

    private void Close(Guid actor, DateTimeOffset now)
    {
        ClosedByUserId = actor;
        ClosedAtUtc = now;
        Version = checked(Version + 1);
    }

    public static string Wire(WorkTaskStatusV1 status) => status switch
    {
        WorkTaskStatusV1.Open => "open",
        WorkTaskStatusV1.Completed => "completed",
        WorkTaskStatusV1.Cancelled => "cancelled",
        _ => throw new InvalidOperationException("Unknown work task status."),
    };
}

public sealed class WorkTaskRuleException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

// ---- Collaboration notifications (AI-D29) ------------------------------------------------------------------------------

public enum WorkInboxNotificationKindV1
{
    Mention = 1,
    TaskAssigned = 2,
    TaskOverdue = 3,
}

public enum WorkInboxNotificationClassV1
{
    Now = 1,
    Digest = 2,
}

/// <summary>
/// AI-D29's default class assignment: "şimdi" = mention and overdue work (and, when they are connected, waiting approvals and
/// security events); everything else is "özet". The decision makes the assignment a Settings parameter; until a Settings
/// owner key exists this catalogue is the decided default and nothing else.
/// </summary>
public static class WorkInboxNotificationClassesV1
{
    public static WorkInboxNotificationClassV1 Of(WorkInboxNotificationKindV1 kind) => kind switch
    {
        WorkInboxNotificationKindV1.Mention => WorkInboxNotificationClassV1.Now,
        WorkInboxNotificationKindV1.TaskOverdue => WorkInboxNotificationClassV1.Now,
        WorkInboxNotificationKindV1.TaskAssigned => WorkInboxNotificationClassV1.Digest,
        _ => throw new InvalidOperationException("Unknown work inbox notification kind."),
    };

    public static string Wire(WorkInboxNotificationKindV1 kind) => kind switch
    {
        WorkInboxNotificationKindV1.Mention => "mention",
        WorkInboxNotificationKindV1.TaskAssigned => "task-assigned",
        WorkInboxNotificationKindV1.TaskOverdue => "task-overdue",
        _ => throw new InvalidOperationException("Unknown work inbox notification kind."),
    };

    public static string Wire(WorkInboxNotificationClassV1 value) => value switch
    {
        WorkInboxNotificationClassV1.Now => "now",
        WorkInboxNotificationClassV1.Digest => "digest",
        _ => throw new InvalidOperationException("Unknown work inbox notification class."),
    };
}

public sealed record WorkInboxNotificationViewV1(Guid Id, Guid CompanyId, string Kind, string Class, Guid SourceId,
    string? RecordType, Guid? RecordId, Guid? ActorUserId, DateTimeOffset RecordedAtUtc, DateTimeOffset VisibleAtUtc,
    DateTimeOffset? ReadAtUtc);

public sealed record WorkInboxNotificationPageV1(Guid CompanyId, IReadOnlyList<WorkInboxNotificationViewV1> Items,
    bool HasMore, int UnreadNowCount, int UnreadDigestCount);

public sealed record WorkInboxNotificationReadResponseV1(Guid Id, DateTimeOffset ReadAtUtc);

/// <summary>
/// The company's digest times (AI-D29: "günde yaklaşık üç kez", the hours are a Settings parameter). <see cref="Unavailable"/>
/// (no owner key yet, or an owner failure) means no digest is released: the digest class stays held, it is never shown early.
/// </summary>
public sealed record WorkDigestScheduleV1(bool Available, Guid CompanyId, IReadOnlyList<TimeOnly> LocalTimes,
    string TimeZoneId, string Version)
{
    public static WorkDigestScheduleV1 Unavailable(Guid companyId) => new(false, companyId, [], "", "");
}

public interface IWorkDigestScheduleReaderV1
{
    Task<WorkDigestScheduleV1> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Pure slot arithmetic: the latest digest slot at or before <paramref name="nowUtc"/> (today's or yesterday's local times
/// in the schedule's zone), or null when the schedule is unavailable or empty.
/// </summary>
public static class WorkDigestSlotsV1
{
    public static DateTimeOffset? LatestSlotAtOrBefore(WorkDigestScheduleV1 schedule, DateTimeOffset nowUtc)
    {
        if (!schedule.Available || schedule.LocalTimes.Count == 0) return null;
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId); }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException) { return null; }
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, zone);
        DateTimeOffset? best = null;
        foreach (var dayOffset in new[] { 0, -1 })
        {
            var day = DateOnly.FromDateTime(localNow.DateTime).AddDays(dayOffset);
            foreach (var time in schedule.LocalTimes)
            {
                var local = day.ToDateTime(time, DateTimeKind.Unspecified);
                if (zone.IsInvalidTime(local)) continue;
                var slot = new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
                if (slot <= nowUtc && (best is null || slot > best)) best = slot;
            }
        }

        return best;
    }
}

public interface IWorkCollaborationServiceV1
{
    Task<WorkNotePageV1> ListNotesAsync(string recordType, Guid recordId, bool includePassive, CancellationToken cancellationToken = default);
    Task<WorkNoteResponseV1> AddNoteAsync(string recordType, Guid recordId, AddWorkNoteRequestV1 request, WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<WorkNoteResponseV1> PassivateNoteAsync(Guid noteId, PassivateWorkNoteRequestV1 request, WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<WorkNoteReadResponseV1> MarkNoteReadAsync(Guid noteId, CancellationToken cancellationToken = default);
    Task<WorkTaskPageV1> ListMyTasksAsync(bool includeClosed, CancellationToken cancellationToken = default);
    Task<WorkTaskPageV1> ListRecordTasksAsync(string recordType, Guid recordId, CancellationToken cancellationToken = default);
    Task<WorkTaskResponseV1> CreateTaskAsync(CreateWorkTaskRequestV1 request, WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<WorkTaskResponseV1> CompleteTaskAsync(Guid taskId, CompleteWorkTaskRequestV1 request, WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<WorkTaskResponseV1> CancelTaskAsync(Guid taskId, CancelWorkTaskRequestV1 request, WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<WorkInboxNotificationPageV1> ListMyInboxAsync(string? notificationClass, bool unreadOnly, CancellationToken cancellationToken = default);
    Task<WorkInboxNotificationReadResponseV1> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default);
    Task<WorkPeoplePageV1> SearchPeopleAsync(string? query, CancellationToken cancellationToken = default);
}

/// <summary>Scheduler work, run per company by the digest hosted service (off by default).</summary>
public interface IWorkCollaborationMaintenanceV1
{
    Task<WorkCollaborationCycleV1> RunCycleAsync(CancellationToken cancellationToken = default);
}

public sealed record WorkCollaborationCycleV1(int OverdueNotifications, int DigestsReleased, int DigestItems, bool ScheduleAvailable);

// ---- Note retention (YOL-D19, YOL-D26) -----------------------------------------------------------------------------------

/// <summary>
/// YOL-D26: a record-bound note (and its mentions, passivation, read receipts and the mention notifications it caused) is
/// kept as long as the record it is bound to is kept, and is disposed of together with that record. Until the company's
/// record disposal policy is in force the disposal path is CLOSED: today a note is never deleted, only passivated. The path
/// exists in code so the day the policy is decided it is a configuration change, not a new write surface.
/// </summary>
public static class WorkNoteRetentionV1
{
    /// <summary>Configuration key; absent or anything but <c>true</c> keeps the path closed (fail-closed default).</summary>
    public const string DisposalEnabledKey = "Work:NoteDisposal:Enabled";
    public const string DisposalClosed = "work_note_disposal_closed";
    public const string RecordDisposalNotConfirmed = "work_record_disposal_not_confirmed";
}

/// <summary>
/// The record owner's statement that a record has been disposed of under the record disposal policy. No owner implements it
/// yet (the policy is not in force), so the host composes a port that answers <see cref="WorkRecordDisposalOutcomeV1.Unavailable"/>
/// for every record: even with the configuration key on, nothing is disposed of without the owner's word.
/// </summary>
public interface IWorkRecordDisposalPortV1
{
    Task<WorkRecordDisposalOutcomeV1> CheckDisposedAsync(Guid companyId, WorkRecordRefV1 record, CancellationToken cancellationToken = default);
}

public enum WorkRecordDisposalOutcomeV1
{
    Unavailable = 0,
    NotDisposed = 1,
    Disposed = 2,
}

public sealed record WorkNoteDisposalResultV1(Guid CompanyId, string RecordType, Guid RecordId, int NotesDisposed, int NotificationsDisposed);

/// <summary>System work, not a user command: run by the future record disposal job for one record of the resolved company.</summary>
public interface IWorkNoteDisposalV1
{
    Task<WorkNoteDisposalResultV1> DisposeRecordNotesAsync(WorkRecordRefV1 record, string correlationId, CancellationToken cancellationToken = default);
}

public sealed class WorkNoteDisposalException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
