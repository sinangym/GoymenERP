using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-S10 (F-010b, B-15, SINAN-CEVAP 14) at the service boundary: the plan's termin suggestion is read under the plan
/// read grant measured after the reads, for the resolved company's plan only. The calendar comes only from Organization,
/// the hourly capacity only from the plan's Settings profile; a missing source is a 200 "unavailable" with its reason,
/// never a guessed day, and nothing is written. URT-SC33: the queue of the work center is counted first (URT-D86), the
/// capacity is input kg/h (URT-D87), and the optional safety buffer is added (URT-D89, absent is zero). W5 follow-up: the
/// capacity and buffer come from the Settings work center capacity record (KIM-SC33-b); a day buffer is whole working
/// days, the work center's buffer wins over the company default and they are never added (URT-D96); the plans ahead are
/// ordered by termin, priority, creation (URT-D97) and count their remaining kg (URT-D99). The clock is 2026-09-18
/// 10:00 UTC (Friday, 13:00 in Europe/Istanbul).
/// Every shift, holiday, kg and kg/h value here is a synthetic test value.
/// </summary>
public sealed partial class ManufacturingCommandServiceTests
{
    private static readonly Guid SuggestionRollB = Guid.Parse("b2000000-0000-0000-0000-0000000000a1");
    private static readonly Guid SuggestionProfile = Guid.Parse("e2000000-0000-0000-0000-0000000000a1");
    private const string SuggestionZone = "Europe/Istanbul";

    private static (Fixture Fixture, ManufacturingCommandService Service) SuggestionCase(
        SuggestionCalendarPort? calendar, SuggestionCapacityPort? capacity, SuggestionQueuePort? queue = null,
        bool withoutQueue = false, SuggestionCompanyBufferPort? companyBuffer = null, bool withoutCompanyBuffer = false)
    {
        var fixture = new Fixture();
        var service = new ManufacturingCommandService(fixture.Store, fixture.Store, fixture.Store,
            new ManufacturingMassVerifier(fixture.Settings), fixture.Authority, fixture.CompanyContext, fixture.User,
            fixture.Correlation, new FixedTime(), workCenterCalendars: calendar,
            workCenterQueues: withoutQueue ? null : queue ?? new SuggestionQueuePort(),
            workCenterCapacities: capacity,
            companyDueDateBuffer: withoutCompanyBuffer ? null : companyBuffer ?? new SuggestionCompanyBufferPort());
        return (fixture, service);
    }

    /// <summary>A stored draft plan of 500 kg (300 + 200) on the work center.</summary>
    private static ProductionPlan SuggestionPlan(Fixture fixture)
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(Roll, new Kilograms(300m)), new PlannedInput(SuggestionRollB, new Kilograms(200m))]);
        fixture.Store.Plans[plan.Id] = plan;
        return plan;
    }

    private static PlanParameterSnapshotV1 CapturedProfile(params PlanParameterValueV1[] values) =>
        new(SuggestionProfile, "4", PlanParameterSnapshotV1.SettingsSource, ManufacturingProcess.Slitting, WorkCenter,
            new DateOnly(2026, 9, 1), null, new DateOnly(2026, 9, 18), values);

    private static OrganizationWorkCenterPlanningReferenceV2 WeekdayCalendar(Guid? workCenter = null,
        string zone = SuggestionZone, IReadOnlyList<DateOnly>? holidays = null) =>
        new(workCenter ?? WorkCenter, "Slitting", 7, "WorkCenter", zone,
        [
            .. new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
                .Select(day => new OrganizationWorkCenterShiftV2(day, new TimeOnly(8, 0), new TimeOnly(16, 0))),
        ],
            holidays ?? []);

    [Fact]
    public async Task A_draft_gets_a_suggestion_from_the_calendar_and_the_work_center_capacity_under_the_plans_read_grant()
    {
        var calendar = new SuggestionCalendarPort { Answer = WeekdayCalendar() };
        var capacity = new SuggestionCapacityPort();
        var (fixture, service) = SuggestionCase(calendar, capacity);
        var plan = SuggestionPlan(fixture);
        var effects = (fixture.Store.Writes.Count, fixture.Store.Commands.Count, fixture.Store.AuditEvents.Count);
        fixture.Authority.Permissions.Clear();

        var result = await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, false, (string?)null), (result.Status, result.IsReplay, result.Code));
        var view = result.Resource!;
        // 500 kg at 100 kg/h is five working hours: 13:00-16:00 on Friday, then 08:00-10:00 on Monday.
        Assert.Equal((2, "calendar_capacity_remaining_queue_from_now", plan.Id, plan.Version, plan.DueDateRevision, WorkCenter),
            (view.ContractVersion, view.MethodCode, view.PlanId, view.PlanVersion, view.DueDateRevision,
                view.WorkCenterId));
        Assert.Equal(("suggested", (DateOnly?)new DateOnly(2026, 9, 21), "2026-09-21T10:00", "500", (long?)300,
                (string?)null),
            (view.Status, view.SuggestedDueDate, view.CompletesAtLocal, view.RequiredKg, view.RequiredWorkingMinutes,
                view.ReasonCode));
        Assert.Equal(("100", "3", "WorkCenterCapacity"),
            (view.CapacityKgPerHour, view.CapacityProfileVersion, view.CapacitySource));
        Assert.Equal(((long?)7, "WorkCenter", SuggestionZone), (view.CalendarVersion, view.CalendarSource, view.TimeZoneId));
        Assert.Equal(((int?)0, "0", (long?)0, "0", (int?)0, (long?)300),
            (view.QueuedPlanCount, view.QueuedKg, view.QueueWorkingMinutes, view.SafetyMarginHours, view.SafetyMarginDays,
                view.TotalWorkingMinutes));
        Assert.Equal((new DateOnly(2026, 9, 18), At), (view.BusinessDate, view.CalculatedAtUtc));
        Assert.Equal([(Company, WorkCenter)], calendar.Calls);
        Assert.Equal([(Company, WorkCenter)], capacity.Calls);
        Assert.Equal([ErpPermissions.ManufacturingPlansRead], fixture.Authority.Permissions);
        Assert.Equal((ErpPermissions.ManufacturingPlansRead, Company), fixture.Authority.LastAsked);
        Assert.Equal(effects,
            (fixture.Store.Writes.Count, fixture.Store.Commands.Count, fixture.Store.AuditEvents.Count));
        // Only a proposal: the plan's termin is untouched.
        Assert.Null(fixture.Store.Plans[plan.Id].RequestedDueDate);
    }

    [Fact]
    public async Task A_holiday_of_the_calendar_moves_the_suggestion()
    {
        var calendar = new SuggestionCalendarPort { Answer = WeekdayCalendar(holidays: [new DateOnly(2026, 9, 21)]) };
        var (fixture, service) = SuggestionCase(calendar, new SuggestionCapacityPort());
        var plan = SuggestionPlan(fixture);

        var view = (await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None)).Resource!;

        Assert.Equal(("suggested", (DateOnly?)new DateOnly(2026, 9, 22), "2026-09-22T10:00"),
            (view.Status, view.SuggestedDueDate, view.CompletesAtLocal));
    }

    [Fact]
    public async Task An_approved_plan_uses_the_work_center_capacity_record_in_force()
    {
        var calendar = new SuggestionCalendarPort { Answer = WeekdayCalendar() };
        var capacity = new SuggestionCapacityPort { CapacityKgPerHour = 250m, Version = 4 };
        var (fixture, service) = SuggestionCase(calendar, capacity);
        var plan = SuggestionPlan(fixture);
        plan.WithDueDate().Approve(plan.Version, Actor, At, null, CapturedProfile());

        var view = (await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None)).Resource!;

        // 500 kg at 250 kg/h is two hours: 13:00-15:00 on Friday.
        Assert.Equal(("suggested", (DateOnly?)new DateOnly(2026, 9, 18), "2026-09-18T15:00", "250", "4",
                "WorkCenterCapacity"),
            (view.Status, view.SuggestedDueDate, view.CompletesAtLocal, view.CapacityKgPerHour,
                view.CapacityProfileVersion, view.CapacitySource));
        Assert.Equal([(Company, WorkCenter)], capacity.Calls);
    }

    [Fact]
    public async Task No_capacity_record_is_capacity_not_configured_and_reads_no_calendar()
    {
        var calendar = new SuggestionCalendarPort { Answer = WeekdayCalendar() };
        var (fixture, service) = SuggestionCase(calendar, new SuggestionCapacityPort { Missing = true });
        var plan = SuggestionPlan(fixture);

        var view = (await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None)).Resource!;

        Assert.Equal(("unavailable", (DateOnly?)null, "capacity_not_configured", (string?)null, (string?)null),
            (view.Status, view.SuggestedDueDate, view.ReasonCode, view.CapacityKgPerHour, view.CapacityProfileVersion));
        Assert.Empty(calendar.Calls);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("work-center")]
    [InlineData("zero")]
    public async Task A_capacity_answer_for_another_scope_or_without_a_positive_value_is_capacity_invalid(string fault)
    {
        var capacity = fault switch
        {
            "company" => new SuggestionCapacityPort { AnswerCompany = OtherCompany },
            "work-center" => new SuggestionCapacityPort { AnswerWorkCenter = Guid.NewGuid() },
            _ => new SuggestionCapacityPort { CapacityKgPerHour = 0m },
        };
        var (fixture, service) = SuggestionCase(new SuggestionCalendarPort { Answer = WeekdayCalendar() }, capacity);
        var plan = SuggestionPlan(fixture);

        var view = (await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None)).Resource!;

        Assert.Equal(("unavailable", "capacity_invalid", (string?)null),
            (view.Status, view.ReasonCode, view.CapacityKgPerHour));
    }

    [Theory]
    [InlineData("settings_provider_unavailable")]
    [InlineData("settings_access_denied")]
    [InlineData("settings_profile_invalid")]
    public async Task A_settings_capacity_failure_is_capacity_source_unavailable_and_reads_no_calendar(
        string settingsCode)
    {
        var calendar = new SuggestionCalendarPort { Answer = WeekdayCalendar() };
        var (fixture, service) = SuggestionCase(calendar, new SuggestionCapacityPort { Refusal = settingsCode });
        var plan = SuggestionPlan(fixture);

        var result = await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, "unavailable", "capacity_source_unavailable", (string?)null),
            (result.Status, result.Resource!.Status, result.Resource.ReasonCode, result.Resource.CapacityKgPerHour));
        Assert.Empty(calendar.Calls);
    }

    [Fact]
    public async Task A_cancelled_capacity_read_is_not_swallowed()
    {
        var (fixture, service) = SuggestionCase(new SuggestionCalendarPort { Answer = WeekdayCalendar() },
            new SuggestionCapacityPort { Fault = new OperationCanceledException() });
        var plan = SuggestionPlan(fixture);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Without_a_capacity_provider_the_source_is_unavailable_and_nothing_is_defaulted()
    {
        var (fixture, service) = SuggestionCase(new SuggestionCalendarPort { Answer = WeekdayCalendar() }, null);
        var plan = SuggestionPlan(fixture);

        var view = (await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None)).Resource!;

        Assert.Equal(("unavailable", "capacity_source_unavailable", (string?)null),
            (view.Status, view.ReasonCode, view.CapacityKgPerHour));
    }

    [Fact]
    public async Task Without_a_calendar_port_the_suggestion_is_calendar_unavailable()
    {
        var (fixture, service) = SuggestionCase(null, new SuggestionCapacityPort());
        var plan = SuggestionPlan(fixture);

        var view = (await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None)).Resource!;

        Assert.Equal(("unavailable", "calendar_unavailable", "100", (long?)null),
            (view.Status, view.ReasonCode, view.CapacityKgPerHour, view.CalendarVersion));
    }

    [Fact]
    public async Task An_organization_failure_or_unknown_work_center_never_fails_the_read()
    {
        var throwing = new SuggestionCalendarPort { Fault = new OrganizationCompanyContextMismatchException() };
        var (throwingFixture, throwingService) = SuggestionCase(throwing, new SuggestionCapacityPort());
        var unknown = new SuggestionCalendarPort { Answer = null };
        var (unknownFixture, unknownService) = SuggestionCase(unknown, new SuggestionCapacityPort());

        var failed = await throwingService.GetPlanDueDateSuggestionAsync(SuggestionPlan(throwingFixture).Id,
            CancellationToken.None);
        var missing = await unknownService.GetPlanDueDateSuggestionAsync(SuggestionPlan(unknownFixture).Id,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, "calendar_unavailable"), (failed.Status, failed.Resource!.ReasonCode));
        Assert.Equal((ManufacturingApiStatus.Ok, "work_center_not_found"),
            (missing.Status, missing.Resource!.ReasonCode));
    }

    [Theory]
    [InlineData("work-center")]
    [InlineData("zone")]
    [InlineData("blank-zone")]
    public async Task A_calendar_answer_for_another_work_center_or_with_an_unknown_zone_is_calendar_answer_invalid(
        string fault)
    {
        var answer = fault switch
        {
            "work-center" => WeekdayCalendar(workCenter: Guid.NewGuid()),
            "zone" => WeekdayCalendar(zone: "SENTETIK/Bilinmeyen"),
            _ => WeekdayCalendar(zone: " "),
        };
        var (fixture, service) = SuggestionCase(new SuggestionCalendarPort { Answer = answer },
            new SuggestionCapacityPort());
        var plan = SuggestionPlan(fixture);

        var view = (await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None)).Resource!;

        Assert.Equal(("unavailable", "calendar_answer_invalid", (long?)null),
            (view.Status, view.ReasonCode, view.CalendarVersion));
    }

    [Fact]
    public async Task A_calendar_without_working_time_says_so_with_the_required_time()
    {
        var empty = new OrganizationWorkCenterPlanningReferenceV2(WorkCenter, "Slitting", 2, "WorkCenter",
            SuggestionZone, [], []);
        var (fixture, service) = SuggestionCase(new SuggestionCalendarPort { Answer = empty },
            new SuggestionCapacityPort());
        var plan = SuggestionPlan(fixture);

        var view = (await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None)).Resource!;

        Assert.Equal(("unavailable", "no_working_time", (long?)300, (long?)2),
            (view.Status, view.ReasonCode, view.RequiredWorkingMinutes, view.CalendarVersion));
    }

    [Fact]
    public async Task A_cancelled_plan_gets_no_suggestion_and_no_source_is_asked()
    {
        var calendar = new SuggestionCalendarPort { Answer = WeekdayCalendar() };
        var capacity = new SuggestionCapacityPort();
        var (fixture, service) = SuggestionCase(calendar, capacity);
        var plan = SuggestionPlan(fixture);
        plan.Cancel(plan.Version, Actor, At, "sentetik iptal");

        var view = (await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None)).Resource!;

        Assert.Equal(("unavailable", "plan_cancelled", "500"), (view.Status, view.ReasonCode, view.RequiredKg));
        Assert.Empty(calendar.Calls);
        Assert.Empty(capacity.Calls);
    }

    [Fact]
    public async Task A_cancelled_calendar_read_is_not_swallowed()
    {
        var (fixture, service) = SuggestionCase(new SuggestionCalendarPort { Fault = new OperationCanceledException() },
            new SuggestionCapacityPort());
        var plan = SuggestionPlan(fixture);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None));
    }

    [Fact]
    public async Task The_suggestion_answers_not_found_for_an_empty_unknown_or_other_company_plan()
    {
        var calendar = new SuggestionCalendarPort { Answer = WeekdayCalendar() };
        var (fixture, service) = SuggestionCase(calendar, new SuggestionCapacityPort());
        var foreign = SuggestionPlan(fixture);
        fixture.CompanyContext.SwitchForTest(OtherCompany);
        fixture.User.Companies.Add(OtherCompany);

        Assert.Equal(ManufacturingApiStatus.NotFound,
            (await service.GetPlanDueDateSuggestionAsync(Guid.Empty, CancellationToken.None)).Status);
        Assert.Equal(ManufacturingApiStatus.NotFound,
            (await service.GetPlanDueDateSuggestionAsync(Guid.NewGuid(), CancellationToken.None)).Status);
        var other = await service.GetPlanDueDateSuggestionAsync(foreign.Id, CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.NotFound, (PlanDueDateSuggestionViewV1?)null),
            (other.Status, other.Resource));
        Assert.Empty(calendar.Calls);
    }

    [Fact]
    public async Task A_suggestion_read_meeting_concurrent_commits_is_a_retryable_503()
    {
        var (fixture, service) = SuggestionCase(new SuggestionCalendarPort { Answer = WeekdayCalendar() },
            new SuggestionCapacityPort());
        var plan = SuggestionPlan(fixture);
        fixture.Store.ReadFault = ManufacturingStoreCodes.ConcurrentChange;

        var result = await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unavailable, (string?)null, (PlanDueDateSuggestionViewV1?)null),
            (result.Status, result.Code, result.Resource));
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("actor")]
    [InlineData("company")]
    public async Task A_suggestion_read_rechecks_the_captured_context_and_the_plans_read_grant_after_the_read(
        string fault)
    {
        var (fixture, service) = SuggestionCase(new SuggestionCalendarPort { Answer = WeekdayCalendar() },
            new SuggestionCapacityPort());
        var plan = SuggestionPlan(fixture);
        fixture.Authority.Permissions.Clear();
        fixture.Store.AfterFindPlan = _ => ApplyFreshFenceFault(fixture, fault);

        var result = await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, ManufacturingApiCodes.AuthorityDenied),
            (result.Status, result.Code));
        Assert.Null(result.Resource);
        Assert.Equal([ErpPermissions.ManufacturingPlansRead], fixture.Authority.Permissions);
    }

    [Fact]
    public async Task A_suggestion_without_a_resolved_company_context_is_denied_before_any_read()
    {
        var calendar = new SuggestionCalendarPort { Answer = WeekdayCalendar() };
        var (fixture, service) = SuggestionCase(calendar, new SuggestionCapacityPort());
        var plan = SuggestionPlan(fixture);
        fixture.User.Companies.Clear();
        fixture.Authority.Permissions.Clear();

        var result = await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, (PlanDueDateSuggestionViewV1?)null),
            (result.Status, result.Resource));
        Assert.Empty(calendar.Calls);
        Assert.Empty(fixture.Authority.Permissions);
    }

    private static ProductionQueueEntryV1 Queued(DateOnly? dueDate, int minutesAfterCreation, decimal plannedKg,
        decimal processedKg = 0m, Guid? planId = null) =>
        new(planId ?? Guid.NewGuid(), dueDate, 0, At.AddMinutes(minutesAfterCreation), new Kilograms(plannedKg),
            new Kilograms(processedKg));

    [Fact]
    public async Task The_remaining_work_ordered_ahead_is_counted_before_the_plans_own_work()
    {
        var queue = new SuggestionQueuePort
        {
            Answer = planId => new ManufacturingWorkCenterQueue(
            [
                // Ahead: an earlier termin (200 planned, 50 processed → 150 left) and the same termin created earlier.
                Queued(new DateOnly(2026, 9, 25), 0, 200m, 50m),
                Queued(new DateOnly(2026, 9, 30), -10, 150m),
                // Not ahead: a later termin, the same termin created later, and no termin at all.
                Queued(new DateOnly(2026, 10, 2), -20, 900m),
                Queued(new DateOnly(2026, 9, 30), 10, 900m),
                Queued(null, -30, 900m),
            ], new ProductionQueueEntryV1(planId, new DateOnly(2026, 9, 30), 0, At, Kilograms.Zero, Kilograms.Zero)),
        };
        var (fixture, service) = SuggestionCase(new SuggestionCalendarPort { Answer = WeekdayCalendar() },
            new SuggestionCapacityPort(), queue);
        var plan = SuggestionPlan(fixture);

        var view = (await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None)).Resource!;

        // (150 + 150 remaining + 500 plan) kg at 100 kg/h is eight working hours: Friday 13:00-16:00, then Monday
        // 08:00-13:00.
        Assert.Equal(("suggested", (DateOnly?)new DateOnly(2026, 9, 21), "2026-09-21T13:00", (int?)2, "300", (long?)180,
                (long?)300, (long?)480),
            (view.Status, view.SuggestedDueDate, view.CompletesAtLocal, view.QueuedPlanCount, view.QueuedKg,
                view.QueueWorkingMinutes, view.RequiredWorkingMinutes, view.TotalWorkingMinutes));
        Assert.Equal([(WorkCenter, plan.Id)], queue.Calls);
    }

    [Fact]
    public async Task A_store_answer_for_another_target_is_queue_unavailable()
    {
        var queue = new SuggestionQueuePort
        {
            Answer = _ => new ManufacturingWorkCenterQueue([],
                new ProductionQueueEntryV1(Guid.NewGuid(), null, 0, At, Kilograms.Zero, Kilograms.Zero)),
        };
        var (fixture, service) = SuggestionCase(new SuggestionCalendarPort { Answer = WeekdayCalendar() },
            new SuggestionCapacityPort(), queue);
        var plan = SuggestionPlan(fixture);

        var view = (await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None)).Resource!;

        Assert.Equal(("unavailable", "queue_unavailable"), (view.Status, view.ReasonCode));
    }

    [Fact]
    public async Task Without_a_queue_store_or_with_an_unreadable_queue_the_suggestion_is_queue_unavailable()
    {
        var (missingFixture, missing) = SuggestionCase(new SuggestionCalendarPort { Answer = WeekdayCalendar() },
            new SuggestionCapacityPort(), withoutQueue: true);
        var missingPlan = SuggestionPlan(missingFixture);
        var (faultFixture, faulted) = SuggestionCase(new SuggestionCalendarPort { Answer = WeekdayCalendar() },
            new SuggestionCapacityPort(), new SuggestionQueuePort { FaultStoredRecord = true });
        var faultPlan = SuggestionPlan(faultFixture);

        var withoutStore = (await missing.GetPlanDueDateSuggestionAsync(missingPlan.Id, CancellationToken.None)).Resource!;
        var unreadable = (await faulted.GetPlanDueDateSuggestionAsync(faultPlan.Id, CancellationToken.None)).Resource!;

        // Never a "free line" guess: URT-D86 needs the queue.
        Assert.Equal(("unavailable", "queue_unavailable", (DateOnly?)null),
            (withoutStore.Status, withoutStore.ReasonCode, withoutStore.SuggestedDueDate));
        Assert.Equal(("unavailable", "queue_unavailable", (DateOnly?)null),
            (unreadable.Status, unreadable.ReasonCode, unreadable.SuggestedDueDate));
    }

    [Theory]
    [InlineData("Hours", "1", "2026-09-18", "2026-09-18T16:00", "1", 0, 180L)]
    [InlineData("Days", "2", "2026-09-22", "2026-09-18T15:00", "0", 2, 120L)]
    public async Task The_work_center_buffer_is_added_after_the_work_a_day_being_a_working_day(string unit,
        string value, string suggested, string completesAt, string marginHours, int marginDays, long totalMinutes)
    {
        var capacity = new SuggestionCapacityPort
        {
            CapacityKgPerHour = 250m,
            BufferValue = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
            BufferUnit = unit,
        };
        var (fixture, service) = SuggestionCase(new SuggestionCalendarPort { Answer = WeekdayCalendar() }, capacity);
        var plan = SuggestionPlan(fixture);

        var view = (await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None)).Resource!;

        // 500 kg at 250 kg/h is two hours from Friday 13:00; one buffer hour ends at 16:00; two working days after
        // Friday skip the weekend and land on Tuesday.
        Assert.Equal(("suggested", (DateOnly?)DateOnly.Parse(suggested, System.Globalization.CultureInfo.InvariantCulture),
                completesAt, marginHours, (int?)marginDays, (long?)totalMinutes),
            (view.Status, view.SuggestedDueDate, view.CompletesAtLocal, view.SafetyMarginHours, view.SafetyMarginDays,
                view.TotalWorkingMinutes));
    }

    [Theory]
    [InlineData("1", "Weeks")]
    [InlineData("-1", "Hours")]
    [InlineData("1.5", "Days")]
    [InlineData("-1", "Days")]
    [InlineData("1", null)]
    public void A_buffer_with_an_unknown_unit_a_negative_value_a_fractional_day_or_no_unit_is_invalid(string value,
        string? unit)
    {
        var number = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(((DueDateSafetyMarginV1?)null, "safety_margin_invalid"),
            ManufacturingCommandService.SafetyBufferOf(number, unit, (0m, null)));
    }

    [Fact]
    public void The_company_default_buffer_applies_only_without_a_work_center_buffer_and_is_never_added()
    {
        var companyDefault = (1m, (string?)WorkCenterSafetyBufferUnitsV1.Days);

        Assert.Equal(((DueDateSafetyMarginV1?)new DueDateSafetyMarginV1(TimeSpan.Zero, 1), (string?)null),
            ManufacturingCommandService.SafetyBufferOf(0m, null, companyDefault));
        Assert.Equal(((DueDateSafetyMarginV1?)new DueDateSafetyMarginV1(TimeSpan.FromHours(3), 0), (string?)null),
            ManufacturingCommandService.SafetyBufferOf(3m, WorkCenterSafetyBufferUnitsV1.Hours, companyDefault));
        Assert.Equal(((DueDateSafetyMarginV1?)DueDateSafetyMarginV1.None, (string?)null),
            ManufacturingCommandService.SafetyBufferOf(0m, null, (0m, null)));
    }

    [Fact]
    public async Task The_real_company_buffer_is_read_only_without_a_work_center_override_and_counts_working_days()
    {
        var buffer = new SuggestionCompanyBufferPort { Days = 2 };
        var (fixture, service) = SuggestionCase(new SuggestionCalendarPort { Answer = WeekdayCalendar() },
            new SuggestionCapacityPort { CapacityKgPerHour = 250m }, companyBuffer: buffer);
        var plan = SuggestionPlan(fixture);
        var result = await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        Assert.Equal(((int?)2, (DateOnly?)new DateOnly(2026, 9, 22)),
            (result.Resource!.SafetyMarginDays, result.Resource.SuggestedDueDate));
        Assert.Equal(Company, Assert.Single(buffer.Calls));

        var unavailableCompany = new SuggestionCompanyBufferPort { Fail = true };
        var (overrideFixture, overrideService) = SuggestionCase(new SuggestionCalendarPort { Answer = WeekdayCalendar() },
            new SuggestionCapacityPort { CapacityKgPerHour = 250m, BufferValue = 1m, BufferUnit = "Days" },
            companyBuffer: unavailableCompany);
        var own = SuggestionPlan(overrideFixture);
        var overridden = await overrideService.GetPlanDueDateSuggestionAsync(own.Id, CancellationToken.None);
        Assert.Equal(1, overridden.Resource!.SafetyMarginDays);
        Assert.Empty(unavailableCompany.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_missing_or_unreadable_company_buffer_provider_is_503_instead_of_a_zero_day_suggestion(bool missing)
    {
        var (fixture, service) = SuggestionCase(new SuggestionCalendarPort { Answer = WeekdayCalendar() },
            new SuggestionCapacityPort(), companyBuffer: new SuggestionCompanyBufferPort { Fail = true },
            withoutCompanyBuffer: missing);
        var result = await service.GetPlanDueDateSuggestionAsync(SuggestionPlan(fixture).Id, CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Unavailable, result.Status);
        Assert.Null(result.Resource);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_foreign_company_buffer_or_a_nonzero_unrecorded_default_is_unavailable(bool foreign)
    {
        var source = new SuggestionCompanyBufferPort { Days = 2, Version = 0, AnswerCompany = foreign ? OtherCompany : Company };
        var (fixture, service) = SuggestionCase(new SuggestionCalendarPort { Answer = WeekdayCalendar() },
            new SuggestionCapacityPort(), companyBuffer: source);
        var answer = await service.GetPlanDueDateSuggestionAsync(SuggestionPlan(fixture).Id, CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Unavailable, answer.Status); Assert.Null(answer.Resource);
    }

    private sealed class SuggestionCompanyBufferPort : ISettingsManufacturingDueDateBufferProviderV1
    {
        public int Days { get; init; }
        public bool Fail { get; init; }
        public long? Version { get; init; }
        public Guid? AnswerCompany { get; init; }
        public List<Guid> Calls { get; } = [];
        public Task<SettingsManufacturingDueDateBufferV1> GetDueDateBufferAsync(Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(companyId);
            if (Fail) throw new InvalidOperationException("Synthetic company buffer read failure");
            return Task.FromResult(new SettingsManufacturingDueDateBufferV1(AnswerCompany ?? companyId, Days, Version ?? (Days == 0 ? 0 : 1)));
        }
    }

    /// <summary>The work center queue store as a test double: an answer built for the asked target (empty queue by
    /// default), or a stored-record fault.</summary>
    private sealed class SuggestionQueuePort : IManufacturingWorkCenterQueueStore
    {
        public Func<Guid, ManufacturingWorkCenterQueue> Answer { get; init; } = planId =>
            new ManufacturingWorkCenterQueue([],
                new ProductionQueueEntryV1(planId, null, 0, At, Kilograms.Zero, Kilograms.Zero));

        public bool FaultStoredRecord { get; init; }

        public List<(Guid WorkCenterId, Guid TargetPlanId)> Calls { get; } = [];

        public Task<ManufacturingWorkCenterQueue> ReadWorkCenterQueueAsync(Guid workCenterId, Guid targetPlanId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((workCenterId, targetPlanId));
            if (FaultStoredRecord)
            {
                throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                    "Kuyruktaki planın planlanan girdisi yok.");
            }

            return Task.FromResult(Answer(targetPlanId));
        }
    }

    /// <summary>Organization's work-center calendar as a test double: one answer, or a fault thrown before answering.</summary>
    private sealed class SuggestionCalendarPort : IOrganizationWorkCenterPlanningReferenceV2
    {
        public OrganizationWorkCenterPlanningReferenceV2? Answer { get; init; }

        public Exception? Fault { get; init; }

        public List<(Guid CompanyId, Guid WorkCenterId)> Calls { get; } = [];

        public Task<OrganizationWorkCenterPlanningReferenceV2?> ReadAsync(Guid companyId, Guid workCenterId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((companyId, workCenterId));
            if (Fault is { } fault)
            {
                throw fault;
            }

            return Task.FromResult(Answer);
        }
    }

    /// <summary>
    /// The Settings work center capacity record in force as a test double: a synthetic version (100 kg/h, version 3, no
    /// buffer by default), no record, a refusal code thrown as Settings does, or another fault.
    /// </summary>
    private sealed class SuggestionCapacityPort : ISettingsWorkCenterCapacityProviderV1
    {
        public List<(Guid CompanyId, Guid WorkCenterId)> Calls { get; } = [];

        public decimal CapacityKgPerHour { get; init; } = 100m;

        public long Version { get; init; } = 3;

        public decimal BufferValue { get; init; }

        public string? BufferUnit { get; init; }

        public bool Missing { get; init; }

        public Guid? AnswerCompany { get; init; }

        public Guid? AnswerWorkCenter { get; init; }

        public string? Refusal { get; init; }

        public Exception? Fault { get; init; }

        public Task<SettingsWorkCenterCapacityV1?> GetWorkCenterCapacityAsync(Guid companyId, Guid workCenterId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((companyId, workCenterId));
            if (Fault is { } fault)
            {
                throw fault;
            }

            if (Refusal is { } code)
            {
                throw new SettingsPrerequisiteException(code);
            }

            return Task.FromResult(Missing
                ? null
                : new SettingsWorkCenterCapacityV1(AnswerCompany ?? companyId, AnswerWorkCenter ?? workCenterId,
                    new ProfileVersionV1(SuggestionProfile, Version), CapacityKgPerHour, BufferValue, BufferUnit));
        }
    }
}
