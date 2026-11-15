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
/// never a guessed day, and nothing is written. The clock is 2026-09-18 10:00 UTC (Friday, 13:00 in Europe/Istanbul).
/// Every shift, holiday, kg and kg/h value here is a synthetic test value.
/// </summary>
public sealed partial class ManufacturingCommandServiceTests
{
    private static readonly Guid SuggestionRollB = Guid.Parse("b2000000-0000-0000-0000-0000000000a1");
    private static readonly Guid SuggestionProfile = Guid.Parse("e2000000-0000-0000-0000-0000000000a1");
    private const string SuggestionZone = "Europe/Istanbul";

    private static (Fixture Fixture, ManufacturingCommandService Service) SuggestionCase(
        SuggestionCalendarPort? calendar, SuggestionCapacityPort? capacity)
    {
        var fixture = new Fixture();
        var service = new ManufacturingCommandService(fixture.Store, fixture.Store, fixture.Store,
            new ManufacturingMassVerifier(fixture.Settings), fixture.Authority, fixture.CompanyContext, fixture.User,
            fixture.Correlation, new FixedTime(), manufacturingParameters: capacity, workCenterCalendars: calendar);
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
    public async Task A_draft_gets_a_suggestion_from_the_calendar_and_todays_capacity_profile_under_the_plans_read_grant()
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
        Assert.Equal((1, "calendar_capacity_from_now", plan.Id, plan.Version, plan.DueDateRevision, WorkCenter),
            (view.ContractVersion, view.MethodCode, view.PlanId, view.PlanVersion, view.DueDateRevision,
                view.WorkCenterId));
        Assert.Equal(("suggested", (DateOnly?)new DateOnly(2026, 9, 21), "2026-09-21T10:00", "500", (long?)300,
                (string?)null),
            (view.Status, view.SuggestedDueDate, view.CompletesAtLocal, view.RequiredKg, view.RequiredWorkingMinutes,
                view.ReasonCode));
        Assert.Equal(("100", "3", "Settings"), (view.CapacityKgPerHour, view.CapacityProfileVersion, view.CapacitySource));
        Assert.Equal(((long?)7, "WorkCenter", SuggestionZone), (view.CalendarVersion, view.CalendarSource, view.TimeZoneId));
        Assert.Equal((new DateOnly(2026, 9, 18), At), (view.BusinessDate, view.CalculatedAtUtc));
        Assert.Equal([(Company, WorkCenter)], calendar.Calls);
        Assert.Equal(("Slitting", (Guid?)WorkCenter), (capacity.Process, capacity.WorkCenter));
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
    public async Task An_approved_plan_uses_its_captured_profile_and_not_todays_settings()
    {
        var calendar = new SuggestionCalendarPort { Answer = WeekdayCalendar() };
        var capacity = new SuggestionCapacityPort { Refusal = ManufacturingProfileProblemCodesV1.ProviderUnavailable };
        var (fixture, service) = SuggestionCase(calendar, capacity);
        var plan = SuggestionPlan(fixture);
        plan.WithDueDate().Approve(plan.Version, Actor, At, null, CapturedProfile(
            new PlanParameterValueV1(ManufacturingDueDateSuggestionV1.CapacityParameterKey, "kg/h", 250m, null)));

        var view = (await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None)).Resource!;

        // 500 kg at 250 kg/h is two hours: 13:00-15:00 on Friday.
        Assert.Equal(("suggested", (DateOnly?)new DateOnly(2026, 9, 18), "2026-09-18T15:00", "250", "4", "Settings"),
            (view.Status, view.SuggestedDueDate, view.CompletesAtLocal, view.CapacityKgPerHour,
                view.CapacityProfileVersion, view.CapacitySource));
        Assert.Null(capacity.Process);
    }

    [Fact]
    public async Task An_approved_plan_without_a_capacity_value_is_capacity_not_configured()
    {
        var (fixture, service) = SuggestionCase(new SuggestionCalendarPort { Answer = WeekdayCalendar() },
            new SuggestionCapacityPort());
        var plan = SuggestionPlan(fixture);
        plan.WithDueDate().Approve(plan.Version, Actor, At, null,
            CapturedProfile(new PlanParameterValueV1("lineSpeedMetresPerMinute", "m/min", 20m, null)));

        var view = (await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None)).Resource!;

        Assert.Equal(("unavailable", (DateOnly?)null, "capacity_not_configured", (string?)null, "4"),
            (view.Status, view.SuggestedDueDate, view.ReasonCode, view.CapacityKgPerHour, view.CapacityProfileVersion));
    }

    [Theory]
    [InlineData("kg/min", "100", null)]
    [InlineData("kg/h", null, "fast")]
    [InlineData("kg/h", "0.000", null)]
    public void A_capacity_value_with_another_unit_a_choice_or_no_positive_number_is_capacity_invalid(string unit,
        string? number, string? choice)
    {
        var profile = CapturedProfile(new PlanParameterValueV1(ManufacturingDueDateSuggestionV1.CapacityParameterKey,
            unit, number is null ? null : decimal.Parse(number, System.Globalization.CultureInfo.InvariantCulture),
            choice));

        Assert.Equal(((decimal?)null, "capacity_invalid"), ManufacturingCommandService.CapacityOf(profile));
        Assert.Equal(((decimal?)null, "capacity_not_configured"), ManufacturingCommandService.CapacityOf(null));
    }

    [Theory]
    [InlineData("settings_provider_unavailable", "capacity_source_unavailable")]
    [InlineData("settings_access_denied", "capacity_source_unavailable")]
    [InlineData("settings_manufacturing_parameter_profile_not_found", "capacity_not_configured")]
    public async Task A_drafts_settings_answer_without_a_profile_says_why_and_reads_no_calendar(string settingsCode,
        string reason)
    {
        var calendar = new SuggestionCalendarPort { Answer = WeekdayCalendar() };
        var (fixture, service) = SuggestionCase(calendar, new SuggestionCapacityPort { Refusal = settingsCode });
        var plan = SuggestionPlan(fixture);

        var result = await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, "unavailable", reason, (string?)null),
            (result.Status, result.Resource!.Status, result.Resource.ReasonCode, result.Resource.CapacityKgPerHour));
        Assert.Empty(calendar.Calls);
    }

    [Fact]
    public async Task Without_a_parameter_port_the_capacity_is_not_configured_and_nothing_is_defaulted()
    {
        var (fixture, service) = SuggestionCase(new SuggestionCalendarPort { Answer = WeekdayCalendar() }, null);
        var plan = SuggestionPlan(fixture);

        var view = (await service.GetPlanDueDateSuggestionAsync(plan.Id, CancellationToken.None)).Resource!;

        Assert.Equal(("unavailable", "capacity_not_configured"), (view.Status, view.ReasonCode));
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
        Assert.Null(capacity.Process);
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
    /// The Settings parameter profile in force today as a test double: a published version carrying the synthetic
    /// capacity value, or a refusal code thrown as Settings does.
    /// </summary>
    private sealed class SuggestionCapacityPort : ISettingsManufacturingParameterProviderV1
    {
        public string? Process { get; private set; }

        public Guid? WorkCenter { get; private set; }

        public string? Refusal { get; init; }

        public Task<ManufacturingParameterProfileSnapshotV1> GetManufacturingParametersAsync(string process,
            Guid? workCenterId, DateOnly effectiveOn, CancellationToken cancellationToken = default)
        {
            Process = process;
            WorkCenter = workCenterId;
            if (Refusal is { } code)
            {
                throw new SettingsPrerequisiteException(code);
            }

            return Task.FromResult(new ManufacturingParameterProfileSnapshotV1(Company, process, workCenterId,
                SuggestionProfile, "3", ManufacturingProfileSourcesV1.Settings, new DateOnly(2026, 9, 1), null,
                [
                    new ManufacturingParameterValueV1(ManufacturingDueDateSuggestionV1.CapacityParameterKey, "kg/h",
                        100m, null),
                    new ManufacturingParameterValueV1("lineSpeedMetresPerMinute", "m/min", 20m, null),
                ]));
        }
    }
}
