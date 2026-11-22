using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.WorkDeadlines;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP15 (DALGA_PLANI_v2 1.16): the Work "yaklaşan süreler" view and its first source, Settings' legal deadline calendar.
/// The shipped state has no calendar version: every obligation is "undefined" with no counter (fail closed). Numbers used for
/// a defined calendar are SENTETIK, not confirmed legal deadlines.
/// </summary>
public sealed class WorkDeadlineReadQueryV1Tests
{
    private static readonly Guid Company = Guid.Parse("15000000-0000-0000-0000-0000000000d1");
    private static readonly Guid OtherCompany = Guid.Parse("15000000-0000-0000-0000-0000000000d2");

    // 2026-11-22 08:00 UTC is 11:00 in Turkey; 2026-11-27 22:30 UTC is already the 28th in Turkey.
    private static readonly DateTimeOffset Morning = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LateNightUtc = new(2026, 11, 27, 22, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task With_no_calendar_version_every_obligation_is_undefined_and_no_counter_runs()
    {
        var world = new World();
        var result = await world.Source().ListUpcomingAsync(world.Context(), 50);
        Assert.Equal(WorkDeadlineSourceOutcomeV1.Found, result.Outcome);
        Assert.Equal(Enum.GetValues<LegalDeadlineObligationV1>().Length, result.Items.Count);
        Assert.All(result.Items, item =>
        {
            Assert.Equal(WorkDeadlineStatesV1.Undefined, item.State);
            Assert.Null(item.DueDate);
            Assert.Null(item.DaysRemaining);
            Assert.Null(item.DefinitionVersion);
            Assert.Equal("/ayarlar/yasal-sure-takvimi", item.Link);
        });
        Assert.Equal("settings.legal-deadline.vat-return", result.Items[0].Code);
    }

    [Fact]
    public async Task A_recorded_entry_counts_from_the_Turkey_business_date_and_comes_first()
    {
        var world = new World { Now = LateNightUtc };
        world.Calendar = new(Company, 2, [new(LegalDeadlineObligationV1.BaBsForms, 1, 1, 28)]);
        var result = await world.Source().ListUpcomingAsync(world.Context(), 50);
        var first = result.Items[0];
        Assert.Equal(("settings.legal-deadline.ba-bs-forms", WorkDeadlineStatesV1.Defined, new DateOnly(2026, 11, 28), 0,
                "legal.deadline-calendar:2"),
            (first.Code, first.State, first.DueDate!.Value, first.DaysRemaining!.Value, first.DefinitionVersion));
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), (first.PeriodStart!.Value, first.PeriodEnd!.Value));
        Assert.All(result.Items.Skip(1), item => Assert.Equal(WorkDeadlineStatesV1.Undefined, item.State));
    }

    [Fact]
    public async Task YOL_D32_a_weekend_due_date_is_shown_on_the_first_business_day_with_the_legal_date_as_the_original()
    {
        var world = new World { HolidayYears = [new(2026, 1, []), new(2027, 1, [])] };
        world.Calendar = new(Company, 2, [new(LegalDeadlineObligationV1.VatReturn, 1, 1, 28)]);
        var first = (await world.Source().ListUpcomingAsync(world.Context(), 50)).Items[0];
        Assert.Equal((new DateOnly(2026, 11, 30), new DateOnly(2026, 11, 28), WorkDeadlineAdjustmentsV1.MovedToBusinessDay, 8),
            (first.DueDate!.Value, first.OriginalDueDate!.Value, first.DueDateAdjustment, first.DaysRemaining!.Value));
    }

    [Fact]
    public async Task YOL_D32_without_a_holiday_calendar_the_date_is_not_moved_and_the_item_says_so()
    {
        var world = new World();
        world.Calendar = new(Company, 2, [new(LegalDeadlineObligationV1.VatReturn, 1, 1, 28)]);
        var first = (await world.Source().ListUpcomingAsync(world.Context(), 50)).Items[0];
        Assert.Equal((new DateOnly(2026, 11, 28), new DateOnly(2026, 11, 28), WorkDeadlineAdjustmentsV1.HolidayCalendarUndefined),
            (first.DueDate!.Value, first.OriginalDueDate!.Value, first.DueDateAdjustment));
        var failing = new World { FailHolidays = true };
        Assert.Equal(WorkDeadlineSourceOutcomeV1.Unavailable, (await failing.Source().ListUpcomingAsync(failing.Context(), 50)).Outcome);
    }

    [Fact]
    public async Task The_page_bound_reports_has_more()
    {
        var world = new World();
        var result = await world.Source().ListUpcomingAsync(world.Context(), 3);
        Assert.Equal(3, result.Items.Count);
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task Without_the_read_permission_the_source_is_denied_and_reads_nothing()
    {
        var world = new World { Granted = false };
        var result = await world.Source().ListUpcomingAsync(world.Context(), 50);
        Assert.Equal(WorkDeadlineSourceOutcomeV1.Denied, result.Outcome);
        Assert.Empty(result.Items);
        Assert.Equal(0, world.CalendarReads);
    }

    [Fact]
    public async Task A_context_for_another_company_is_denied()
    {
        var world = new World();
        var result = await world.Source().ListUpcomingAsync(new WorkDeadlineSourceContextV1(OtherCompany, "SENTETIK-user", "SENTETIK-c"), 50);
        Assert.Equal(WorkDeadlineSourceOutcomeV1.Denied, result.Outcome);
    }

    [Fact]
    public async Task Permission_lost_during_the_read_is_denied_not_answered()
    {
        var world = new World();
        world.OnCalendarRead = () => world.Granted = false;
        Assert.Equal(WorkDeadlineSourceOutcomeV1.Denied, (await world.Source().ListUpcomingAsync(world.Context(), 50)).Outcome);
    }

    [Fact]
    public async Task A_calendar_that_cannot_be_read_or_belongs_to_another_company_is_unavailable_never_an_assumed_date()
    {
        var failing = new World { FailCalendar = true };
        Assert.Equal(WorkDeadlineSourceOutcomeV1.Unavailable, (await failing.Source().ListUpcomingAsync(failing.Context(), 50)).Outcome);
        var foreign = new World { Calendar = new(OtherCompany, 1, []) };
        Assert.Equal(WorkDeadlineSourceOutcomeV1.Unavailable, (await foreign.Source().ListUpcomingAsync(foreign.Context(), 50)).Outcome);
        var authority = new World { FailAuthority = true };
        Assert.Equal(WorkDeadlineSourceOutcomeV1.Unavailable, (await authority.Source().ListUpcomingAsync(authority.Context(), 50)).Outcome);
    }

    [Fact]
    public async Task The_Work_view_lays_out_visible_sources_reports_failures_and_leaves_denied_sources_out()
    {
        var world = new World();
        var query = new WorkDeadlineReadQueryV1([
                new StubSource("zeta", "zeta.deadline.v1", WorkDeadlineSourceResultV1.Unavailable()),
                world.Source(),
                new StubSource("alpha", "alpha.deadline.v1", WorkDeadlineSourceResultV1.Denied())],
            world.CompanyContext, world, world);
        var result = await query.ListAsync();
        Assert.Equal(WorkDeadlineReadOutcomeV1.Found, result.Outcome);
        Assert.Equal([("settings", "found"), ("zeta", "unavailable")],
            result.Resource!.Sources.Select(source => (source.Module, source.Outcome)));
        Assert.Equal(Enum.GetValues<LegalDeadlineObligationV1>().Length, result.Resource.Items.Count);
        Assert.All(result.Resource.Items, item => Assert.Equal(WorkDeadlineStatesV1.Undefined, item.State));
    }

    [Fact]
    public async Task A_source_answering_an_inconsistent_item_is_reported_unavailable_and_its_items_are_dropped()
    {
        var world = new World();
        var bad = new WorkDeadlineItemV1("finance", "finance.deadline.v1", "finance.x", WorkDeadlineStatesV1.Undefined,
            null, null, new DateOnly(2026, 12, 1), 9, null, "/finans");
        // A "moved" item whose shown date is not after its original is inconsistent too.
        var notMoved = new WorkDeadlineItemV1("finance", "finance.deadline.v1", "finance.y", WorkDeadlineStatesV1.Defined,
            null, null, new DateOnly(2026, 12, 1), 9, "v1", "/finans", new DateOnly(2026, 12, 1), WorkDeadlineAdjustmentsV1.MovedToBusinessDay);
        var otherQuery = new WorkDeadlineReadQueryV1([new StubSource("finance", "finance.deadline.v1",
            WorkDeadlineSourceResultV1.Found([notMoved], false))], world.CompanyContext, world, world);
        Assert.Equal("unavailable", Assert.Single((await otherQuery.ListAsync()).Resource!.Sources).Outcome);
        var query = new WorkDeadlineReadQueryV1([new StubSource("finance", "finance.deadline.v1",
            WorkDeadlineSourceResultV1.Found([bad], false))], world.CompanyContext, world, world);
        var result = await query.ListAsync();
        Assert.Empty(result.Resource!.Items);
        Assert.Equal("unavailable", Assert.Single(result.Resource.Sources).Outcome);
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_denied_and_an_unresolved_correlation_is_unavailable()
    {
        var anonymous = new World { Authenticated = false };
        Assert.Equal(WorkDeadlineReadOutcomeV1.Denied,
            (await new WorkDeadlineReadQueryV1([], anonymous.CompanyContext, anonymous, anonymous).ListAsync()).Outcome);
        var uncorrelated = new World { CorrelationResolved = false };
        Assert.Equal(WorkDeadlineReadOutcomeV1.Unavailable,
            (await new WorkDeadlineReadQueryV1([], uncorrelated.CompanyContext, uncorrelated, uncorrelated).ListAsync()).Outcome);
    }

    [Theory]
    [InlineData(LegalDeadlineObligationV1.VatReturn, "settings.legal-deadline.vat-return")]
    [InlineData(LegalDeadlineObligationV1.ELedgerBerat, "settings.legal-deadline.e-ledger-berat")]
    [InlineData(LegalDeadlineObligationV1.BaBsForms, "settings.legal-deadline.ba-bs-forms")]
    [InlineData(LegalDeadlineObligationV1.SocialSecurityReturn, "settings.legal-deadline.social-security-return")]
    public void Codes_are_kebab_case_under_the_owner_module(LegalDeadlineObligationV1 obligation, string code)
    {
        Assert.Equal(code, SettingsLegalDeadlineWorkSourceV1.Code(obligation));
    }

    private sealed class StubSource(string module, string kind, WorkDeadlineSourceResultV1 result) : IWorkDeadlineSourceV1
    {
        public string Module => module;
        public string Kind => kind;
        public Task<WorkDeadlineSourceResultV1> ListUpcomingAsync(WorkDeadlineSourceContextV1 context, int maxCount,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class World : ICurrentUser, IErpAuthorityRevalidationService, ISettingsLegalDeadlineCalendarProviderV1, ICorrelationContext,
        ISettingsWorkHolidayCalendarProviderV1
    {
        public IReadOnlyList<SettingsWorkHolidayCalendarYearV1> HolidayYears { get; set; } = [];
        public bool FailHolidays { get; set; }
        public Task<SettingsWorkHolidayCalendarsV1> GetWorkHolidayCalendarsAsync(Guid companyId, int fromYear, int toYear,
            CancellationToken cancellationToken = default)
        {
            if (FailHolidays) throw new InvalidOperationException("SENTETIK holiday failure");
            return Task.FromResult(new SettingsWorkHolidayCalendarsV1(companyId,
                HolidayYears.Where(year => year.Year >= fromYear && year.Year <= toYear).ToArray()));
        }

        public World()
        {
            CompanyContext.Set(Company);
        }

        public CompanyContext CompanyContext { get; } = new();
        public bool Authenticated { get; set; } = true;
        public bool Granted { get; set; } = true;
        public bool FailAuthority { get; set; }
        public bool FailCalendar { get; set; }
        public bool CorrelationResolved { get; set; } = true;
        public DateTimeOffset Now { get; set; } = Morning;
        public SettingsLegalDeadlineCalendarV1 Calendar { get; set; } = new(Company, 0, []);
        public Action? OnCalendarRead { get; set; }
        public int CalendarReads { get; private set; }

        public SettingsLegalDeadlineWorkSourceV1 Source() => new(CompanyContext, this, this, this, new Clock(Now), this);

        public WorkDeadlineSourceContextV1 Context() => new(Company, "SENTETIK-user", "SENTETIK-correlation");

        public bool IsAuthenticated => Authenticated;
        public string? UserId => Authenticated ? "SENTETIK-user" : null;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            if (FailAuthority) throw new InvalidOperationException("SENTETIK authority failure");
            Assert.Equal(SettingsLegalDeadlinePermissionsV1.DeadlinesRead, permission);
            return Task.FromResult(new PermissionScope(Granted, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(),
                new HashSet<Guid>()));
        }

        public Task<SettingsLegalDeadlineCalendarV1> GetLegalDeadlineCalendarAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            CalendarReads++;
            OnCalendarRead?.Invoke();
            if (FailCalendar) throw new InvalidOperationException("SENTETIK calendar failure");
            return Task.FromResult(Calendar);
        }

        public bool IsResolved => CorrelationResolved;
        public string CorrelationId => "SENTETIK-correlation";
        public string? CausationId => null;
        public string SourceChannel => "Test";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) { }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
