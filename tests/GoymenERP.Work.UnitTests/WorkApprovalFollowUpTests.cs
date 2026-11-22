using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.ApprovalFollowUp;
using GoymenERP.Modules.Work.Infrastructure.ApprovalRules;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoymenERP.Work.UnitTests;

/// <summary>
/// WP2 (YOL-D17, YOL-D28, AI-D29, D10, YOL-D25): the approval follow-up cycle, its views and the business-day digest slots.
/// SENTETIK data only; no model or AI call anywhere.
/// </summary>
public sealed class WorkApprovalFollowUpTests
{
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000a2");
    private static readonly Guid Maker = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid Approver = Guid.Parse("10000000-0000-4000-8000-000000000002");
    private static readonly Guid ScopedApprover = Guid.Parse("10000000-0000-4000-8000-000000000003");
    private static readonly Guid Escalator = Guid.Parse("10000000-0000-4000-8000-000000000004");
    private static readonly DateTimeOffset Waiting = new(2026, 11, 23, 6, 0, 0, TimeSpan.Zero); // Monday 09:00 Istanbul
    private const string Kind = "logistics.dispatch-note.v1"; // hour-scale class by YOL-D18 (4 hours by default)

    [Fact]
    public async Task A_new_waiting_cycle_tells_the_eligible_approvers_now_but_not_the_maker_nor_a_scoped_holder_the_owner_refuses()
    {
        var harness = new Harness();
        harness.Time.Now = Waiting.AddMinutes(5);

        var cycle = await harness.RunAsync();

        Assert.Equal((1, 0, 1, 0, 0, 0, 1), (cycle.KindsFollowed, cycle.KindsUnavailable, cycle.Waiting, cycle.Reminders,
            cycle.Escalations, cycle.Orphaned, cycle.Notifications));
        await using var database = harness.Database();
        var step = Assert.Single(await database.ApprovalFollowUps.ToListAsync());
        Assert.Equal(((int)WorkApprovalFollowUpStepV1.Waiting, 1, Waiting.AddHours(4), "work.approval-duration-hours:0", "work.approvals.escalate"),
            (step.Step, step.RecipientCount, step.DueAtUtc!.Value, step.PolicyVersion, step.EscalationPermission));
        var notification = Assert.Single(await database.InboxNotifications.ToListAsync());
        Assert.Equal((Approver, (int)WorkInboxNotificationKindV1.ApprovalWaiting, (int)WorkInboxNotificationClassV1.Now, step.Id),
            (notification.RecipientUserId, notification.Kind, notification.Class, notification.SourceId));
        // D10: the cycle is measured by the follow-up itself, not only when someone opens the inbox.
        Assert.Single(await database.ApprovalInboxEvents.Where(row => row.EventType == (int)WorkApprovalInboxEventTypeV1.Arrived).ToListAsync());
    }

    [Fact]
    public async Task After_the_due_time_one_reminder_goes_to_the_approvers_and_one_class_length_later_the_escalation_holders_get_it_once()
    {
        var harness = new Harness();
        harness.Time.Now = Waiting.AddMinutes(5);
        await harness.RunAsync();
        harness.Time.Now = Waiting.AddHours(4).AddMinutes(1);
        var reminder = await harness.RunAsync();
        harness.Time.Now = Waiting.AddHours(8).AddMinutes(1);
        var escalation = await harness.RunAsync();
        var again = await harness.RunAsync();

        Assert.Equal((1, 0), (reminder.Reminders, reminder.Escalations));
        Assert.Equal((0, 1), (escalation.Reminders, escalation.Escalations));
        Assert.Equal((0, 0, 0, 0), (again.Waiting, again.Reminders, again.Escalations, again.Notifications));
        await using var database = harness.Database();
        Assert.Equal(new[] { 1, 2, 3 }, (await database.ApprovalFollowUps.Select(row => row.Step).ToListAsync()).Order());
        var notifications = await database.InboxNotifications.ToListAsync();
        Assert.Single(notifications, row => row.Kind == (int)WorkInboxNotificationKindV1.ApprovalReminder && row.RecipientUserId == Approver);
        Assert.Single(notifications, row => row.Kind == (int)WorkInboxNotificationKindV1.ApprovalEscalated && row.RecipientUserId == Escalator);
        Assert.DoesNotContain(notifications, row => row.RecipientUserId == Maker);
    }

    [Fact]
    public async Task While_no_one_holds_the_escalation_permission_only_the_reminder_goes_out()
    {
        var harness = new Harness();
        harness.Holders.Escalation = [];
        harness.Time.Now = Waiting.AddHours(9);

        var cycle = await harness.RunAsync();

        Assert.Equal((1, 1, 0), (cycle.Waiting, cycle.Reminders, cycle.Escalations));
        await using var database = harness.Database();
        Assert.DoesNotContain(await database.ApprovalFollowUps.ToListAsync(), row => row.Step == (int)WorkApprovalFollowUpStepV1.Escalated);
    }

    [Fact]
    public async Task Without_an_eligible_approver_the_cycle_is_orphaned_and_the_escalation_holders_are_told()
    {
        var harness = new Harness();
        harness.Holders.Approvers = [Holder(Maker), Holder(ScopedApprover, companyWide: false)];
        harness.Time.Now = Waiting.AddHours(9);

        var cycle = await harness.RunAsync();

        Assert.Equal((0, 1, 0, 0), (cycle.Waiting, cycle.Orphaned, cycle.Reminders, cycle.Escalations));
        await using var database = harness.Database();
        var orphaned = Assert.Single(await database.ApprovalFollowUps.ToListAsync());
        Assert.Equal(((int)WorkApprovalFollowUpStepV1.Orphaned, 1), (orphaned.Step, orphaned.RecipientCount));
        var notification = Assert.Single(await database.InboxNotifications.ToListAsync());
        Assert.Equal((Escalator, (int)WorkInboxNotificationKindV1.ApprovalOrphaned), (notification.RecipientUserId, notification.Kind));
    }

    [Fact]
    public async Task An_unavailable_holder_directory_or_timing_takes_no_step_and_no_guess()
    {
        var harness = new Harness();
        harness.Holders.Available = false;
        harness.Time.Now = Waiting.AddHours(9);
        var unavailable = await harness.RunAsync();
        Assert.Equal((0, 1), (unavailable.KindsFollowed, unavailable.KindsUnavailable));

        harness.Holders.Available = true;
        harness.Timing.Answer = null;
        var noTiming = await harness.RunAsync();
        Assert.Equal((1, 0, 0), (noTiming.Waiting, noTiming.Reminders, noTiming.Escalations));
        await using var database = harness.Database();
        var waiting = Assert.Single(await database.ApprovalFollowUps.ToListAsync());
        Assert.Null(waiting.DueAtUtc);
    }

    [Fact]
    public async Task The_owners_decision_time_is_recorded_exactly_when_the_cycle_leaves_the_pending_list()
    {
        var harness = new Harness();
        harness.Time.Now = Waiting.AddMinutes(5);
        await harness.RunAsync();
        var decidedAt = Waiting.AddMinutes(37);
        harness.Source.Pending = [];
        harness.Source.Decision = WorkInboxDecisionAnswerV1.Decided(decidedAt, WorkInboxDecisionsV1.Approved);
        harness.Time.Now = Waiting.AddHours(2);

        var cycle = await harness.RunAsync();

        Assert.Equal(1, cycle.DecisionsRecorded);
        await using var database = harness.Database();
        var decided = Assert.Single(await database.ApprovalInboxEvents.Where(row => row.EventType == (int)WorkApprovalInboxEventTypeV1.Decided).ToListAsync());
        Assert.Equal((decidedAt, Waiting, harness.ItemId), (decided.OccurredAtUtc, decided.CycleStartedAtUtc, decided.ItemId));
        // The decided cycle is closed: the observation does not add a second (observed) resolution.
        Assert.Empty(await database.ApprovalInboxEvents.Where(row => row.EventType == (int)WorkApprovalInboxEventTypeV1.ResolvedObserved).ToListAsync());
    }

    [Fact]
    public async Task The_follow_up_views_need_the_escalation_permission_and_drop_a_decided_cycle()
    {
        var harness = new Harness();
        harness.Time.Now = Waiting.AddHours(9);
        await harness.RunAsync();

        var denied = await Assert.ThrowsAsync<WorkApprovalFollowUpException>(() => harness.Reader(null).ListAsync(WorkApprovalFollowUpViewsV1.Escalated));
        Assert.Equal(403, denied.Status);
        var invalid = await Assert.ThrowsAsync<WorkApprovalFollowUpException>(() =>
            harness.Reader(WorkPermissionManifestV1.ApprovalsEscalate).ListAsync("everything"));
        Assert.Equal(400, invalid.Status);
        var page = await harness.Reader(WorkPermissionManifestV1.ApprovalsEscalate).ListAsync(WorkApprovalFollowUpViewsV1.Escalated);
        var item = Assert.Single(page.Items);
        Assert.Equal((Kind, harness.ItemId, "escalated", Waiting), (item.Kind, item.ItemId, item.Step, item.WaitingSinceUtc));
        Assert.Empty((await harness.Reader(WorkPermissionManifestV1.ApprovalsEscalate).ListAsync(WorkApprovalFollowUpViewsV1.Orphaned)).Items);

        harness.Source.Pending = [];
        harness.Source.Decision = WorkInboxDecisionAnswerV1.Decided(Waiting.AddHours(9), WorkInboxDecisionsV1.Rejected);
        harness.Time.Now = Waiting.AddHours(10);
        await harness.RunAsync();
        Assert.Empty((await harness.Reader(WorkPermissionManifestV1.ApprovalsEscalate).ListAsync(WorkApprovalFollowUpViewsV1.Escalated)).Items);
    }

    [Fact]
    public void Business_day_digest_slots_skip_weekends_and_recorded_holidays()
    {
        var holiday = new DateOnly(2026, 11, 23); // SENTETIK: a Monday listed as a holiday
        var schedule = new WorkDigestScheduleV1(true, Company, [new TimeOnly(9, 0), new TimeOnly(13, 0), new TimeOnly(17, 0)],
            "Europe/Istanbul", "work.digest-time-morning:0", BusinessDaysOnly: true, Holidays: new HashSet<DateOnly> { holiday });
        // Tuesday 08:00 Istanbul: Monday is a holiday and the weekend is skipped, so Friday 17:00 (14:00 UTC) is the latest.
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 14, 0, 0, TimeSpan.Zero),
            WorkDigestSlotsV1.LatestSlotAtOrBefore(schedule, new DateTimeOffset(2026, 11, 24, 5, 0, 0, TimeSpan.Zero)));
        // Tuesday 09:30 Istanbul: the 09:00 slot of the day.
        Assert.Equal(new DateTimeOffset(2026, 11, 24, 6, 0, 0, TimeSpan.Zero),
            WorkDigestSlotsV1.LatestSlotAtOrBefore(schedule, new DateTimeOffset(2026, 11, 24, 6, 30, 0, TimeSpan.Zero)));
        Assert.False(WorkDigestSlotsV1.IsBusinessDay(new DateOnly(2026, 11, 22), null));
        Assert.True(WorkDigestSlotsV1.IsBusinessDay(new DateOnly(2026, 11, 23), null));
    }

    [Fact]
    public void Due_times_follow_the_class_length_holidays_and_the_escalation_assumption()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        var timing = new WorkApprovalTimingV1(Company, 4, "work.approval-duration-hours:0", 2, "work.approval-duration-business-days:0",
            new Dictionary<int, IReadOnlySet<DateOnly>> { [2026] = new HashSet<DateOnly> { new(2026, 11, 24) } });
        var hours = WorkApprovalDueCalculatorV1.Due(WorkApprovalDurationClassV1.Hours, timing, Waiting, zone)!.Value;
        Assert.Equal((Waiting.AddHours(4), 4), (hours.Due, hours.Length));
        // Monday 09:00 + 2 business days, Tuesday is a holiday → Thursday 09:00 Istanbul (06:00 UTC).
        var days = WorkApprovalDueCalculatorV1.Due(WorkApprovalDurationClassV1.BusinessDays, timing, Waiting, zone)!.Value;
        Assert.Equal(new DateTimeOffset(2026, 11, 26, 6, 0, 0, TimeSpan.Zero), days.Due);
        Assert.Equal(Waiting.AddHours(8), WorkApprovalDueCalculatorV1.EscalationDue(WorkApprovalDurationClassV1.Hours, 4, hours.Due, zone, timing.HolidayYears));
        Assert.Null(WorkApprovalDueCalculatorV1.Due(WorkApprovalDurationClassV1.BusinessDays, timing, Waiting, null));
        Assert.Null(WorkApprovalDueCalculatorV1.Due(WorkApprovalDurationClassV1.Hours, timing with { Hours = null }, Waiting, zone));
    }

    private static WorkPermissionHolderV1 Holder(Guid user, bool companyWide = true) =>
        new(user, companyWide, companyWide ? new HashSet<Guid>() : new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid>(), new HashSet<Guid>());

    private sealed class Harness
    {
        private readonly string name = Guid.NewGuid().ToString("N");

        public Harness()
        {
            Source = new FakeSource(ItemId);
        }

        public Guid ItemId { get; } = Guid.NewGuid();
        public FakeSource Source { get; }
        public FakeHolders Holders { get; } = new();
        public FakeTiming Timing { get; } = new();
        public MutableTime Time { get; } = new();

        public WorkDbContext Database()
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new WorkDbContext(new DbContextOptionsBuilder<WorkDbContext>().UseInMemoryDatabase(name).Options, company);
        }

        public async Task<WorkApprovalFollowUpCycleV1> RunAsync()
        {
            var company = new CompanyContext();
            company.Set(Company);
            await using var database = new WorkDbContext(new DbContextOptionsBuilder<WorkDbContext>().UseInMemoryDatabase(name).Options, company);
            await using var measurementsDb = new WorkDbContext(new DbContextOptionsBuilder<WorkDbContext>().UseInMemoryDatabase(name).Options, company);
            await using var rulesDb = new WorkDbContext(new DbContextOptionsBuilder<WorkDbContext>().UseInMemoryDatabase(name).Options, company);
            var maintenance = new WorkApprovalFollowUpMaintenanceV1(database, company, [Source], new WorkApprovalRuleReaderV1(rulesDb),
                new WorkApprovalInboxMeasurementStoreV1(measurementsDb, company, Time), Timing, Holders,
                new SystemWorkBusinessTimeZoneSourceV1(), Time, NullLogger<WorkApprovalFollowUpMaintenanceV1>.Instance);
            return await maintenance.RunCycleAsync();
        }

        public WorkApprovalFollowUpReadV1 Reader(string? granted)
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new WorkApprovalFollowUpReadV1(
                new WorkDbContext(new DbContextOptionsBuilder<WorkDbContext>().UseInMemoryDatabase(name).Options, company),
                company, new FakeUser(Escalator), new FakeAuthority(granted));
        }
    }

    private sealed class FakeSource(Guid itemId) : IWorkInboxFollowUpSourceV1
    {
        public string Module => "logistics";
        public string Kind => WorkApprovalFollowUpTests.Kind;
        public string ApproverPermission => "logistics.dispatch-notes.approve";
        public IReadOnlyList<Guid> Pending { get; set; } = [itemId];
        public WorkInboxDecisionAnswerV1 Decision { get; set; } = WorkInboxDecisionAnswerV1.StillPending();

        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The follow-up never asks as a person.");

        public Task<WorkInboxSourceResultV1> ListPendingForFollowUpAsync(Guid companyId, int maxCount, CancellationToken cancellationToken) =>
            Task.FromResult(WorkInboxSourceResultV1.Found(companyId, Pending.Select(id => new WorkInboxItemV1("logistics", Kind, id,
                "SENTETIK irsaliye", Waiting, "/lojistik/yukleme-talimatlari/" + id.ToString("D"), Maker.ToString("D"),
                [new WorkApprovalRuleResultV1("logistics.dispatch-note-draft", null, null, null, null)], [])).ToArray(), false));

        public Task<WorkInboxDecisionAnswerV1> ReadDecisionAsync(Guid companyId, Guid itemId, DateTimeOffset waitingSinceUtc,
            CancellationToken cancellationToken) => Task.FromResult(Decision);
    }

    private sealed class FakeHolders : IWorkPermissionHolderDirectoryV1
    {
        public bool Available { get; set; } = true;
        public IReadOnlyList<WorkPermissionHolderV1> Approvers { get; set; } =
            [Holder(Maker), Holder(Approver), Holder(ScopedApprover, companyWide: false)];
        public IReadOnlyList<WorkPermissionHolderV1> Escalation { get; set; } = [Holder(Escalator), Holder(Maker)];

        public Task<WorkPermissionHoldersV1> ListHoldersAsync(Guid companyId, string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(!Available || companyId != Company
                ? WorkPermissionHoldersV1.Unavailable
                : new WorkPermissionHoldersV1(true, permission == WorkPermissionManifestV1.ApprovalsEscalate ? Escalation : Approvers));
    }

    private sealed class FakeTiming : IWorkApprovalTimingReaderV1
    {
        public WorkApprovalTimingV1? Answer { get; set; } = new(Company, 4, "work.approval-duration-hours:0", 2,
            "work.approval-duration-business-days:0", new Dictionary<int, IReadOnlySet<DateOnly>>());

        public Task<WorkApprovalTimingV1?> ReadAsync(Guid companyId, int fromYear, int toYear, CancellationToken cancellationToken = default) =>
            Task.FromResult(Answer);
    }

    private sealed class FakeAuthority(string? granted) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(permission == granted && companyId == Company
                ? new PermissionScope(true, true, new HashSet<string>(["SENTETIK_ROLE"]), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
    }

    private sealed class FakeUser(Guid actor) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]);
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }

    public sealed class MutableTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
