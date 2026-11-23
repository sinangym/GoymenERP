using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Work.UnitTests;

public sealed class WorkCollaborationDomainTests
{
    private static readonly Guid Assigner = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid Assignee = Guid.Parse("10000000-0000-4000-8000-000000000002");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Only_the_assignee_completes_an_open_task_and_the_version_moves_by_exactly_one()
    {
        var task = WorkTaskV1.Restore(Assigner, Assignee, WorkTaskStatusV1.Open, 1);
        Assert.Equal(WorkCollaborationProblemCodesV1.TaskNotAssignee,
            Assert.Throws<WorkTaskRuleException>(() => task.Complete(1, Assigner, Now)).Code);
        task.Complete(1, Assignee, Now);
        Assert.Equal((WorkTaskStatusV1.Completed, 2L, (Guid?)Assignee, (DateTimeOffset?)Now),
            (task.Status, task.Version, task.ClosedByUserId, task.ClosedAtUtc));
        Assert.Equal(WorkCollaborationProblemCodesV1.TaskNotOpen,
            Assert.Throws<WorkTaskRuleException>(() => task.Complete(2, Assignee, Now)).Code);
    }

    [Fact]
    public void Only_the_assigner_cancels_an_open_task_with_a_reason_and_a_stale_version_conflicts_first()
    {
        var task = WorkTaskV1.Restore(Assigner, Assignee, WorkTaskStatusV1.Open, 1);
        Assert.Equal(WorkCollaborationProblemCodesV1.VersionConflict,
            Assert.Throws<WorkTaskRuleException>(() => task.Cancel(7, Assigner, "r", Now)).Code);
        Assert.Equal(WorkCollaborationProblemCodesV1.TaskNotAssigner,
            Assert.Throws<WorkTaskRuleException>(() => task.Cancel(1, Assignee, "r", Now)).Code);
        task.Cancel(1, Assigner, "artik gerekmiyor", Now);
        Assert.Equal((WorkTaskStatusV1.Cancelled, 2L, "artik gerekmiyor"), (task.Status, task.Version, task.CancelReason));
    }

    [Fact]
    public void Restore_refuses_an_invalid_stored_task()
    {
        Assert.Throws<InvalidOperationException>(() => WorkTaskV1.Restore(Guid.Empty, Assignee, WorkTaskStatusV1.Open, 1));
        Assert.Throws<InvalidOperationException>(() => WorkTaskV1.Restore(Assigner, Assignee, WorkTaskStatusV1.Open, 0));
        Assert.Throws<InvalidOperationException>(() => WorkTaskV1.Restore(Assigner, Assignee, (WorkTaskStatusV1)9, 1));
    }

    [Fact]
    public void Record_type_wire_names_round_trip_and_unknown_names_refuse()
    {
        // WP9 appended seven kinds after the first five (values 6..12) and YOL-D42 the goods receipt (13); existing values never move.
        Assert.Equal(new[] { "sales-order-draft", "purchase-order", "stock-label", "manufacturing-plan", "counterparty",
                "sales-invoice", "loading-instruction", "purchase-incoming-invoice", "manufacturing-order", "finance-manual-receipt",
                "finance-manual-bank-payment", "sales-quote", "purchase-goods-receipt" },
            WorkRecordTypesV1.WireNames);
        Assert.Equal(Enumerable.Range(1, 13), WorkRecordTypesV1.StoredValues);
        foreach (var name in WorkRecordTypesV1.WireNames)
        {
            Assert.True(WorkRecordTypesV1.TryParse(name, out var type));
            Assert.Equal(name, WorkRecordTypesV1.Wire(type));
        }

        Assert.False(WorkRecordTypesV1.TryParse("Sales-Order-Draft", out _));
        Assert.False(WorkRecordTypesV1.TryParse(null, out _));
        Assert.False(WorkRecordTypesV1.IsKnown(0));
        Assert.False(WorkRecordTypesV1.IsKnown(14));
    }

    [Fact]
    public void AI_D29_default_classes_put_mentions_and_overdue_work_in_now_and_task_assignment_in_digest()
    {
        Assert.Equal(WorkInboxNotificationClassV1.Now, WorkInboxNotificationClassesV1.Of(WorkInboxNotificationKindV1.Mention));
        Assert.Equal(WorkInboxNotificationClassV1.Now, WorkInboxNotificationClassesV1.Of(WorkInboxNotificationKindV1.TaskOverdue));
        Assert.Equal(WorkInboxNotificationClassV1.Digest, WorkInboxNotificationClassesV1.Of(WorkInboxNotificationKindV1.TaskAssigned));
    }

    [Fact]
    public void The_latest_digest_slot_is_today_or_yesterday_in_the_schedule_zone_and_none_without_a_schedule()
    {
        var schedule = new WorkDigestScheduleV1(true, Guid.NewGuid(), [new TimeOnly(9, 0), new TimeOnly(13, 0), new TimeOnly(17, 0)],
            "Etc/GMT-3", "1");
        // 10:30 UTC is 13:30 at UTC+3: the 13:00 slot (10:00 UTC) is the latest.
        Assert.Equal(new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero),
            WorkDigestSlotsV1.LatestSlotAtOrBefore(schedule, new DateTimeOffset(2026, 11, 22, 10, 30, 0, TimeSpan.Zero)));
        // 05:00 UTC is 08:00 local: yesterday's 17:00 local (14:00 UTC) is the latest.
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 14, 0, 0, TimeSpan.Zero),
            WorkDigestSlotsV1.LatestSlotAtOrBefore(schedule, new DateTimeOffset(2026, 11, 22, 5, 0, 0, TimeSpan.Zero)));
        Assert.Null(WorkDigestSlotsV1.LatestSlotAtOrBefore(WorkDigestScheduleV1.Unavailable(Guid.NewGuid()), Now));
        Assert.Null(WorkDigestSlotsV1.LatestSlotAtOrBefore(schedule with { TimeZoneId = "Not/AZone" }, Now));
    }

    [Fact]
    public void The_permission_manifest_spells_exactly_the_five_Work_names()
    {
        // WP-C6 adds work.assistant-memory.oversee (AI-D30) next to the three W1-C names; WP2 adds work.approvals.escalate (YOL-D28).
        Assert.Equal(new[] { "work.approvals.escalate", "work.assistant-memory.oversee", "work.notes.write", "work.tasks.assign", "work.tasks.complete" },
            WorkPermissionManifestV1.Names.Order(StringComparer.Ordinal));
    }
}
