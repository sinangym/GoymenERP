namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

/// <summary>W1-C: a note on a record. Append-only: never edited (a correction is a new note) and never deleted (YOL-D19).</summary>
internal sealed class WorkRecordNoteRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public int RecordType { get; set; }
    public Guid RecordId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string Body { get; set; } = "";
    public Guid? CorrectsNoteId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CorrelationId { get; set; } = "";
    public string? CausationId { get; set; }
}

internal sealed class WorkRecordNoteMentionRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid NoteId { get; set; }
    public Guid MentionedUserId { get; set; }
}

/// <summary>A note leaves the default view by this append-only row; the note itself stays (YOL-D19).</summary>
internal sealed class WorkRecordNotePassivationRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid NoteId { get; set; }
    public Guid PassivatedByUserId { get; set; }
    public DateTimeOffset PassivatedAtUtc { get; set; }
    public string Reason { get; set; } = "";
}

/// <summary>
/// WP9 read receipt: one row per (note, reader), written the first time the reader marks the note read; never changed. The
/// author does not get one (writing is not reading). Kept and disposed of with the note (YOL-D26).
/// </summary>
internal sealed class WorkRecordNoteReadRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid NoteId { get; set; }
    public Guid ReaderUserId { get; set; }
    public DateTimeOffset ReadAtUtc { get; set; }
}

internal sealed class WorkTaskRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public int? RecordType { get; set; }
    public Guid? RecordId { get; set; }
    public string Title { get; set; } = "";
    public string? Details { get; set; }
    public Guid AssignerUserId { get; set; }
    public Guid AssigneeUserId { get; set; }
    public DateTimeOffset DueAtUtc { get; set; }
    public int Status { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public string? CancelReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// AI-D29 personal collaboration notification. One row per (recipient, kind, source): the unique key is the decision's
/// "aynı kayıt, alıcı ve olay için ikinci bildirim üretilmez" (the source id — a note or a task — fixes the record).
/// </summary>
internal sealed class WorkInboxNotificationRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public Guid RecipientUserId { get; set; }
    public int Kind { get; set; }
    public int Class { get; set; }
    public Guid SourceId { get; set; }
    public int? RecordType { get; set; }
    public Guid? RecordId { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string CorrelationId { get; set; } = "";
    public string? CausationId { get; set; }

    // D9 (WP2): the event evidence of kinds 4 (allocation released) and 5 (purchase supply revived), moved here from the
    // retired work.Notifications table; null on every other kind. For these kinds SourceId is the row's own Id and the
    // event grain (source, event, recipient, target lines) is unique.
    public string? SourceName { get; set; }
    public Guid? EventId { get; set; }
    public Guid? SourceEntityId { get; set; }
    public string? SourceVersion { get; set; }
    public string? ReasonCode { get; set; }
    public string? ReasonText { get; set; }
    public DateTimeOffset? OccurredAtUtc { get; set; }
    public Guid? SalesOrderId { get; set; }
    public Guid? SalesOrderLineId { get; set; }
    public long? ObservedSalesOrderVersion { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public Guid? PurchaseOrderLineId { get; set; }
    public long? ObservedPurchaseOrderVersion { get; set; }
    public int? CostKind { get; set; }
}

internal sealed class WorkInboxNotificationReadRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid NotificationId { get; set; }
    public DateTimeOffset ReadAtUtc { get; set; }
}

/// <summary>One released digest for one recipient at one schedule slot.</summary>
internal sealed class WorkInboxDigestRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public Guid RecipientUserId { get; set; }
    public DateTimeOffset SlotUtc { get; set; }
    public DateTimeOffset ReleasedAtUtc { get; set; }
    public string ScheduleVersion { get; set; } = "";
}

/// <summary>A digest-class notification becomes visible only through exactly one of these rows.</summary>
internal sealed class WorkInboxDigestItemRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid NotificationId { get; set; }
    public Guid DigestId { get; set; }
}

/// <summary>
/// WP2 (YOL-D17, YOL-D28, AI-D29): one step Work took for one pending approval cycle (kind, owner record, the time it began
/// waiting): 1 = the approvers were told it waits ("şimdi"), 2 = the one reminder after the duration ran out, 3 = escalated
/// to the holders of the rule's escalation permission, 4 = no eligible approver ("sahipsiz onay"). Append-only; one row per
/// cycle and step. The owner's label and link are the ones the owner gave at that step (the follow-up view shows them to a
/// holder who may not read the owner's queue); the decision is never taken here.
/// </summary>
internal sealed class WorkApprovalFollowUpRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public string Kind { get; set; } = "";
    public Guid ItemId { get; set; }
    public DateTimeOffset CycleStartedAtUtc { get; set; }
    public int Step { get; set; }
    public string DocumentLabel { get; set; } = "";
    public string Link { get; set; } = "";
    public string ApproverPermission { get; set; } = "";
    public string? EscalationPermission { get; set; }
    public string RuleVersion { get; set; } = "";
    public string? PolicyVersion { get; set; }
    public DateTimeOffset? DueAtUtc { get; set; }
    public int RecipientCount { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string CorrelationId { get; set; } = "";
}

/// <summary>Idempotency record of one collaboration command (success or recorded terminal refusal).</summary>
internal sealed class WorkCollaborationAttemptRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid ActorUserId { get; set; }
    public string IdempotencyKeySha256 { get; set; } = "";
    public string Command { get; set; } = "";
    public string RequestSha256 { get; set; } = "";
    public Guid TargetId { get; set; }
    public int Status { get; set; }
    public string? RefusalCode { get; set; }
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}
