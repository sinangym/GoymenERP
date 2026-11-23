using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

/// <summary>W1-C record notes, mentions, tasks, collaboration notifications, digests and command attempts.</summary>
public sealed partial class WorkDbContext
{
    internal const string EmptyGuid = "'00000000-0000-0000-0000-000000000000'";
    // WP9 widened 1..5 to 1..12, YOL-D42 to 1..13 (WorkRecordTypesV1.StoredValues; a unit test keeps the two equal).
    internal const string RecordTypeList = "1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13";
    internal const string RecordNoteCheck =
        "[CompanyId] <> " + EmptyGuid + " AND [Id] <> " + EmptyGuid + " AND [RecordId] <> " + EmptyGuid
        + " AND [AuthorUserId] <> " + EmptyGuid + " AND [RecordType] IN (" + RecordTypeList + ") AND LEN([Body]) > 0"
        + " AND LEN([CorrelationId]) > 0 AND ([CorrectsNoteId] IS NULL OR ([CorrectsNoteId] <> " + EmptyGuid
        + " AND [CorrectsNoteId] <> [Id]))";
    internal const string RecordNoteMentionCheck =
        "[CompanyId] <> " + EmptyGuid + " AND [NoteId] <> " + EmptyGuid + " AND [MentionedUserId] <> " + EmptyGuid;
    internal const string RecordNotePassivationCheck =
        "[CompanyId] <> " + EmptyGuid + " AND [PassivatedByUserId] <> " + EmptyGuid + " AND LEN([Reason]) > 0";
    internal const string RecordNoteReadCheck =
        "[CompanyId] <> " + EmptyGuid + " AND [NoteId] <> " + EmptyGuid + " AND [ReaderUserId] <> " + EmptyGuid;
    internal const string TaskIdentityCheck =
        "[CompanyId] <> " + EmptyGuid + " AND [Id] <> " + EmptyGuid + " AND [AssignerUserId] <> " + EmptyGuid
        + " AND [AssigneeUserId] <> " + EmptyGuid + " AND LEN([Title]) > 0 AND [Version] >= 1"
        + " AND (([RecordType] IS NULL AND [RecordId] IS NULL) OR ([RecordType] IN (" + RecordTypeList + ") AND [RecordId] IS NOT NULL"
        + " AND [RecordId] <> " + EmptyGuid + "))";
    internal const string TaskStateCheck =
        "([Status] = 1 AND [Version] = 1 AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL AND [CancelReason] IS NULL)"
        + " OR ([Status] = 2 AND [Version] = 2 AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] = [AssigneeUserId] AND [CancelReason] IS NULL)"
        + " OR ([Status] = 3 AND [Version] = 2 AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] = [AssignerUserId]"
        + " AND [CancelReason] IS NOT NULL AND LEN([CancelReason]) > 0)";
    internal const string InboxNotificationCheck =
        "[CompanyId] <> " + EmptyGuid + " AND [Id] <> " + EmptyGuid + " AND [RecipientUserId] <> " + EmptyGuid
        + " AND [SourceId] <> " + EmptyGuid + " AND LEN([CorrelationId]) > 0"
        + " AND (([Kind] = 1 AND [Class] = 1 AND [RecordType] IS NOT NULL AND [RecordId] IS NOT NULL AND [ActorUserId] IS NOT NULL)"
        + " OR ([Kind] = 2 AND [Class] = 2 AND [ActorUserId] IS NOT NULL)"
        + " OR ([Kind] = 3 AND [Class] = 1 AND [ActorUserId] IS NULL)"
        // D9 (WP2): the event-derived kinds moved from work.Notifications are "özet" (AI-D29); WP2's approval kinds are "şimdi".
        + " OR ([Kind] IN (4, 5) AND [Class] = 2 AND [ActorUserId] IS NULL AND [RecordType] IS NULL AND [RecordId] IS NULL)"
        + " OR ([Kind] IN (6, 7, 8, 9) AND [Class] = 1 AND [ActorUserId] IS NULL AND [RecordType] IS NULL AND [RecordId] IS NULL))"
        + " AND (([RecordType] IS NULL AND [RecordId] IS NULL) OR ([RecordType] IN (" + RecordTypeList + ") AND [RecordId] IS NOT NULL"
        + " AND [RecordId] <> " + EmptyGuid + "))";

    /// <summary>
    /// D9 (WP2): kinds 4 and 5 carry the whole evidence the retired work.Notifications rows carried (same rules: one sales line
    /// target, or — kind 5 only — one purchase line target); every other kind carries none of it. Every column is tested with
    /// IS NOT NULL so an unknown never passes the check.
    /// </summary>
    internal const string InboxNotificationEventCheck =
        "([Kind] IN (4, 5) AND [SourceId] = [Id] AND [SourceName] IS NOT NULL AND LEN([SourceName]) > 0"
        + " AND [EventId] IS NOT NULL AND [EventId] <> " + EmptyGuid
        + " AND [SourceEntityId] IS NOT NULL AND [SourceEntityId] <> " + EmptyGuid
        + " AND [SourceVersion] IS NOT NULL AND LEN([SourceVersion]) > 0 AND [ReasonCode] IS NOT NULL AND LEN([ReasonCode]) > 0"
        + " AND [ReasonText] IS NOT NULL AND LEN([ReasonText]) > 0 AND [OccurredAtUtc] IS NOT NULL"
        + " AND (([SalesOrderId] IS NOT NULL AND [SalesOrderLineId] IS NOT NULL AND [ObservedSalesOrderVersion] IS NOT NULL"
        + " AND [SalesOrderId] <> " + EmptyGuid + " AND [SalesOrderLineId] <> " + EmptyGuid + " AND [ObservedSalesOrderVersion] >= 1"
        + " AND [PurchaseOrderId] IS NULL AND [PurchaseOrderLineId] IS NULL AND [ObservedPurchaseOrderVersion] IS NULL)"
        + " OR ([Kind] = 5 AND [PurchaseOrderId] IS NOT NULL AND [PurchaseOrderLineId] IS NOT NULL AND [ObservedPurchaseOrderVersion] IS NOT NULL"
        + " AND [PurchaseOrderId] <> " + EmptyGuid + " AND [PurchaseOrderLineId] <> " + EmptyGuid + " AND [ObservedPurchaseOrderVersion] >= 1"
        + " AND [SalesOrderId] IS NULL AND [SalesOrderLineId] IS NULL AND [ObservedSalesOrderVersion] IS NULL)))"
        + " OR ([Kind] NOT IN (4, 5) AND [SourceName] IS NULL AND [EventId] IS NULL AND [SourceEntityId] IS NULL AND [SourceVersion] IS NULL"
        + " AND [ReasonCode] IS NULL AND [ReasonText] IS NULL AND [OccurredAtUtc] IS NULL AND [SalesOrderId] IS NULL"
        + " AND [SalesOrderLineId] IS NULL AND [ObservedSalesOrderVersion] IS NULL AND [PurchaseOrderId] IS NULL"
        + " AND [PurchaseOrderLineId] IS NULL AND [ObservedPurchaseOrderVersion] IS NULL)";

    internal const string EventGrainIndexName = "UX_WorkInboxNotifications_EventGrain";

    /// <summary>WP2: one follow-up step of one approval cycle (see <see cref="WorkApprovalFollowUpRow"/>).</summary>
    internal const string ApprovalFollowUpCheck =
        "[CompanyId] <> " + EmptyGuid + " AND [Id] <> " + EmptyGuid + " AND [ItemId] <> " + EmptyGuid
        + " AND LEN([Kind]) > 0 AND [Step] IN (1, 2, 3, 4) AND LEN([DocumentLabel]) > 0 AND LEN([Link]) > 0"
        + " AND LEN([ApproverPermission]) > 0 AND LEN([RuleVersion]) > 0 AND LEN([CorrelationId]) > 0"
        + " AND [RecipientCount] >= 0 AND [RecordedAtUtc] >= [CycleStartedAtUtc]"
        + " AND ([EscalationPermission] IS NULL OR LEN([EscalationPermission]) > 0)"
        + " AND ([PolicyVersion] IS NULL OR LEN([PolicyVersion]) > 0)"
        // A reminder and an escalation exist only for a computed due time and always reach at least one person; an orphaned
        // cycle has no eligible approver by definition and its row is written whoever (if anyone) it reached.
        + " AND ([Step] NOT IN (2, 3) OR ([DueAtUtc] IS NOT NULL AND [PolicyVersion] IS NOT NULL AND [RecipientCount] >= 1))"
        + " AND ([Step] <> 3 OR [EscalationPermission] IS NOT NULL)";

    internal const string InboxDigestCheck =
        "[CompanyId] <> " + EmptyGuid + " AND [Id] <> " + EmptyGuid + " AND [RecipientUserId] <> " + EmptyGuid
        + " AND [ReleasedAtUtc] >= [SlotUtc] AND LEN([ScheduleVersion]) > 0";
    internal const string CollaborationAttemptCheck =
        "[CompanyId] <> " + EmptyGuid + " AND [ActorUserId] <> " + EmptyGuid + " AND [TargetId] <> " + EmptyGuid
        + " AND LEN([Command]) > 0 AND LEN([CorrelationId]) > 0"
        + " AND DATALENGTH([IdempotencyKeySha256]) = 64 AND [IdempotencyKeySha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'"
        + " AND DATALENGTH([RequestSha256]) = 64 AND [RequestSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'"
        + " AND (([Status] IN (200, 201) AND [RefusalCode] IS NULL) OR ([Status] = 409 AND [RefusalCode] IS NOT NULL AND LEN([RefusalCode]) > 0))";

    internal DbSet<WorkRecordNoteRow> RecordNotes => Set<WorkRecordNoteRow>();
    internal DbSet<WorkRecordNoteMentionRow> RecordNoteMentions => Set<WorkRecordNoteMentionRow>();
    internal DbSet<WorkRecordNotePassivationRow> RecordNotePassivations => Set<WorkRecordNotePassivationRow>();
    internal DbSet<WorkRecordNoteReadRow> RecordNoteReads => Set<WorkRecordNoteReadRow>();
    internal DbSet<WorkTaskRow> Tasks => Set<WorkTaskRow>();
    internal DbSet<WorkInboxNotificationRow> InboxNotifications => Set<WorkInboxNotificationRow>();
    internal DbSet<WorkInboxNotificationReadRow> InboxNotificationReads => Set<WorkInboxNotificationReadRow>();
    internal DbSet<WorkInboxDigestRow> InboxDigests => Set<WorkInboxDigestRow>();
    internal DbSet<WorkInboxDigestItemRow> InboxDigestItems => Set<WorkInboxDigestItemRow>();
    internal DbSet<WorkCollaborationAttemptRow> CollaborationAttempts => Set<WorkCollaborationAttemptRow>();
    internal DbSet<WorkApprovalFollowUpRow> ApprovalFollowUps => Set<WorkApprovalFollowUpRow>();

    private void ConfigureCollaboration(ModelBuilder model)
    {
        var note = model.Entity<WorkRecordNoteRow>();
        note.ToTable("RecordNotes", Schema, table => table.HasCheckConstraint("CK_WorkRecordNotes_Identity", RecordNoteCheck));
        note.HasKey(row => new { row.CompanyId, row.Id });
        note.Property(row => row.Body).HasMaxLength(4000).IsRequired();
        note.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        note.Property(row => row.CausationId).HasMaxLength(128);
        note.HasIndex(row => new { row.CompanyId, row.RecordType, row.RecordId, row.CreatedAtUtc, row.Id });
        note.HasOne<WorkRecordNoteRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.CorrectsNoteId })
            .OnDelete(DeleteBehavior.Restrict);
        note.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        note.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var mention = model.Entity<WorkRecordNoteMentionRow>();
        mention.ToTable("RecordNoteMentions", Schema, table => table.HasCheckConstraint("CK_WorkRecordNoteMentions_Identity", RecordNoteMentionCheck));
        mention.HasKey(row => new { row.CompanyId, row.NoteId, row.MentionedUserId });
        mention.HasOne<WorkRecordNoteRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.NoteId }).OnDelete(DeleteBehavior.Restrict);
        mention.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var passive = model.Entity<WorkRecordNotePassivationRow>();
        passive.ToTable("RecordNotePassivations", Schema, table => table.HasCheckConstraint("CK_WorkRecordNotePassivations_Identity", RecordNotePassivationCheck));
        passive.HasKey(row => new { row.CompanyId, row.NoteId });
        passive.Property(row => row.Reason).HasMaxLength(500).IsRequired();
        passive.HasOne<WorkRecordNoteRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.NoteId }).OnDelete(DeleteBehavior.Restrict);
        passive.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var noteRead = model.Entity<WorkRecordNoteReadRow>();
        noteRead.ToTable("RecordNoteReads", Schema, table => table.HasCheckConstraint("CK_WorkRecordNoteReads_Identity", RecordNoteReadCheck));
        noteRead.HasKey(row => new { row.CompanyId, row.NoteId, row.ReaderUserId });
        noteRead.HasOne<WorkRecordNoteRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.NoteId }).OnDelete(DeleteBehavior.Restrict);
        noteRead.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var task = model.Entity<WorkTaskRow>();
        task.ToTable("Tasks", Schema, table =>
        {
            table.HasCheckConstraint("CK_WorkTasks_Identity", TaskIdentityCheck);
            table.HasCheckConstraint("CK_WorkTasks_State", TaskStateCheck);
        });
        task.HasKey(row => new { row.CompanyId, row.Id });
        task.Property(row => row.Title).HasMaxLength(200).IsRequired();
        task.Property(row => row.Details).HasMaxLength(2000);
        task.Property(row => row.CancelReason).HasMaxLength(500);
        task.Property(row => row.Version).IsConcurrencyToken();
        task.Property(row => row.RowVersion).IsRowVersion();
        task.HasIndex(row => new { row.CompanyId, row.AssigneeUserId, row.Status, row.DueAtUtc, row.Id });
        task.HasIndex(row => new { row.CompanyId, row.RecordType, row.RecordId, row.CreatedAtUtc, row.Id });
        task.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        task.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var inbox = model.Entity<WorkInboxNotificationRow>();
        inbox.ToTable("InboxNotifications", Schema, table =>
        {
            table.HasCheckConstraint("CK_WorkInboxNotifications_Identity", InboxNotificationCheck);
            table.HasCheckConstraint("CK_WorkInboxNotifications_Event", InboxNotificationEventCheck);
        });
        inbox.HasKey(row => new { row.CompanyId, row.Id });
        inbox.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        inbox.Property(row => row.CausationId).HasMaxLength(128);
        inbox.Property(row => row.SourceName).HasMaxLength(128).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        inbox.Property(row => row.SourceVersion).HasMaxLength(128).IsUnicode(false);
        inbox.Property(row => row.ReasonCode).HasMaxLength(64).IsUnicode(false);
        inbox.Property(row => row.ReasonText).HasMaxLength(4000);
        // D9: the event grain of the retired work.Notifications unique index, kept for the moved kinds.
        inbox.HasIndex(row => new { row.CompanyId, row.SourceName, row.EventId, row.RecipientUserId, row.SalesOrderId,
                row.SalesOrderLineId, row.PurchaseOrderId, row.PurchaseOrderLineId })
            .IsUnique().HasFilter("[EventId] IS NOT NULL").HasDatabaseName(EventGrainIndexName);
        inbox.HasOne<WorkNotificationInboxRow>().WithMany()
            .HasForeignKey(row => new { row.CompanyId, row.SourceName, row.EventId }).OnDelete(DeleteBehavior.Restrict);
        inbox.HasIndex(row => new { row.CompanyId, row.RecipientUserId, row.Kind, row.SourceId }).IsUnique()
            .HasDatabaseName("UX_WorkInboxNotifications_RecipientEvent");
        inbox.HasIndex(row => new { row.CompanyId, row.RecipientUserId, row.RecordedAtUtc, row.Id });
        inbox.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        inbox.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var read = model.Entity<WorkInboxNotificationReadRow>();
        read.ToTable("InboxNotificationReads", Schema);
        read.HasKey(row => new { row.CompanyId, row.NotificationId });
        read.HasOne<WorkInboxNotificationRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.NotificationId }).OnDelete(DeleteBehavior.Restrict);
        read.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var digest = model.Entity<WorkInboxDigestRow>();
        digest.ToTable("InboxDigests", Schema, table => table.HasCheckConstraint("CK_WorkInboxDigests_Identity", InboxDigestCheck));
        digest.HasKey(row => new { row.CompanyId, row.Id });
        digest.Property(row => row.ScheduleVersion).HasMaxLength(128).IsUnicode(false).IsRequired();
        digest.HasIndex(row => new { row.CompanyId, row.RecipientUserId, row.SlotUtc }).IsUnique()
            .HasDatabaseName("UX_WorkInboxDigests_RecipientSlot");
        digest.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        digest.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var item = model.Entity<WorkInboxDigestItemRow>();
        item.ToTable("InboxDigestItems", Schema);
        item.HasKey(row => new { row.CompanyId, row.NotificationId });
        item.HasIndex(row => new { row.CompanyId, row.DigestId });
        item.HasOne<WorkInboxNotificationRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.NotificationId }).OnDelete(DeleteBehavior.Restrict);
        item.HasOne<WorkInboxDigestRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.DigestId }).OnDelete(DeleteBehavior.Restrict);
        item.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var followUp = model.Entity<WorkApprovalFollowUpRow>();
        followUp.ToTable("ApprovalFollowUps", Schema, table => table.HasCheckConstraint("CK_WorkApprovalFollowUps_Identity", ApprovalFollowUpCheck));
        followUp.HasKey(row => new { row.CompanyId, row.Id });
        followUp.Property(row => row.Kind).HasMaxLength(160).IsUnicode(false).UseCollation("Latin1_General_100_BIN2").IsRequired();
        followUp.Property(row => row.DocumentLabel).HasMaxLength(200).IsRequired();
        followUp.Property(row => row.Link).HasMaxLength(512).IsUnicode(false).IsRequired();
        followUp.Property(row => row.ApproverPermission).HasMaxLength(128).IsUnicode(false).UseCollation("Latin1_General_100_BIN2").IsRequired();
        followUp.Property(row => row.EscalationPermission).HasMaxLength(128).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        followUp.Property(row => row.RuleVersion).HasMaxLength(256).IsUnicode(false).IsRequired();
        followUp.Property(row => row.PolicyVersion).HasMaxLength(128).IsUnicode(false);
        followUp.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        // One row per cycle and step: a racing second cycle run cannot remind or escalate twice.
        followUp.HasIndex(row => new { row.CompanyId, row.Kind, row.ItemId, row.CycleStartedAtUtc, row.Step }).IsUnique()
            .HasDatabaseName("UX_WorkApprovalFollowUps_CycleStep");
        followUp.HasIndex(row => new { row.CompanyId, row.Step, row.RecordedAtUtc, row.Id });
        followUp.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        followUp.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);

        var attempt = model.Entity<WorkCollaborationAttemptRow>();
        attempt.ToTable("CollaborationAttempts", Schema, table => table.HasCheckConstraint("CK_WorkCollaborationAttempts_Identity", CollaborationAttemptCheck));
        attempt.HasKey(row => new { row.CompanyId, row.ActorUserId, row.IdempotencyKeySha256 });
        attempt.Property(row => row.IdempotencyKeySha256).HasColumnType("char(64)").IsUnicode(false).IsFixedLength();
        attempt.Property(row => row.RequestSha256).HasColumnType("char(64)").IsUnicode(false).IsFixedLength();
        attempt.Property(row => row.Command).HasMaxLength(80).IsUnicode(false).UseCollation(OutboxCollation).IsRequired();
        attempt.Property(row => row.RefusalCode).HasMaxLength(64).IsUnicode(false);
        attempt.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        attempt.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
    }

    private bool noteDisposalWindowOpen;

    /// <summary>
    /// YOL-D26 disposal window: while open, the note family (the note, its mentions, passivation and read receipts, and the
    /// mention notifications it caused with their reads and digest links) may be deleted; nothing else may. Only
    /// <c>WorkNoteDisposalV1</c> opens it, and only after the configuration gate and the owner's disposal confirmation.
    /// </summary>
    internal IDisposable OpenNoteDisposalWindow()
    {
        if (noteDisposalWindowOpen) throw new InvalidOperationException("The note disposal window is already open.");
        noteDisposalWindowOpen = true;
        return new DisposalWindow(this);
    }

    private static bool IsNoteDisposalRow(object entity) => entity is WorkRecordNoteRow or WorkRecordNoteMentionRow
        or WorkRecordNotePassivationRow or WorkRecordNoteReadRow or WorkInboxNotificationReadRow or WorkInboxDigestItemRow
        || entity is WorkInboxNotificationRow { Kind: 1 };

    private sealed class DisposalWindow(WorkDbContext owner) : IDisposable
    {
        public void Dispose() => owner.noteDisposalWindowOpen = false;
    }

    private static bool IsAppendOnlyCollaborationRow(object entity) => entity is WorkRecordNoteRow or WorkRecordNoteMentionRow
        or WorkRecordNotePassivationRow or WorkRecordNoteReadRow or WorkInboxNotificationRow or WorkInboxNotificationReadRow or WorkInboxDigestRow
        or WorkInboxDigestItemRow or WorkCollaborationAttemptRow or WorkApprovalFollowUpRow;
}
