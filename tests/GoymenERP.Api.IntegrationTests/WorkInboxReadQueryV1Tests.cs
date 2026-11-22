using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.ApprovalRules;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class WorkInboxReadQueryV1Tests
{
    private static readonly Guid Company = Guid.Parse("d1000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("d1000000-0000-0000-0000-000000000002");
    private static readonly Guid First = Guid.Parse("d2000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("d2000000-0000-0000-0000-000000000002");
    private static readonly Guid Third = Guid.Parse("d2000000-0000-0000-0000-000000000003");
    private static readonly DateTimeOffset Early = DateTimeOffset.Parse("2026-11-16T07:00:00Z");
    private static readonly DateTimeOffset Late = DateTimeOffset.Parse("2026-11-17T07:00:00Z");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-18T07:00:00Z");
    private const string Checker = "checker";
    private const string Maker = "maker";

    [Fact]
    public async Task Items_are_merged_oldest_first_with_permission_duration_and_explanation_and_the_makers_own_record_is_hidden()
    {
        var logistics = new Source("logistics", "logistics.plan-excess.v1", "logistics.plan-excess.approve")
        {
            Result = ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId,
                [Item("logistics", "logistics.plan-excess.v1", Second, Late, Maker)], hasMore: false),
        };
        var finance = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve")
        {
            Result = ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId,
                [Item("finance", "finance.manual-bank-payment.v1", First, Early, Maker),
                    Item("finance", "finance.manual-bank-payment.v1", Third, Early, Checker)], hasMore: true),
        };
        var fixture = Fixture([logistics, finance], policy: new(Company, 4, 1, 2, 3));

        var result = await fixture.Query.ListAsync(null, null, default);

        Assert.Equal(WorkInboxReadOutcomeV1.Found, result.Outcome);
        var page = result.Resource!;
        Assert.Equal((Company, WorkInboxViewsV1.AwaitingMyDecision), (page.CompanyId, page.View));
        Assert.Equal([First, Second], page.Items.Select(item => item.ItemId).ToArray());
        var payment = page.Items[0];
        Assert.Equal("finance.manual-bank-payment.approve", payment.AwaitingPermission);
        Assert.Equal(("BusinessDays", WorkInboxDurationStatesV1.Defined, 2, "work.approval-duration-business-days:3"),
            (payment.Duration.Class, payment.Duration.State, payment.Duration.Limit, payment.Duration.PolicyVersion));
        Assert.Equal("finance.rule", Assert.Single(payment.Explanation.Rules).RuleCode);
        Assert.Equal("party", Assert.Single(payment.Explanation.References).Role);
        var excess = page.Items[1];
        Assert.Equal((Late.AddHours(4), true), (excess.Duration.DueAtUtc, excess.Duration.IsOverdue));
        Assert.Equal(
            [("finance", "finance.manual-bank-payment.v1", WorkInboxReadQueryV1.SourceReady, true),
                ("logistics", "logistics.plan-excess.v1", WorkInboxReadQueryV1.SourceReady, false)],
            page.Sources.Select(source => (source.Module, source.Kind, source.State, source.HasMore)).ToArray());
        Assert.Equal((Company, Checker, NativeApprovalQueueRulesV1.MaxItemsPerSource),
            (finance.Context!.CompanyId, finance.Context.UserId, finance.MaxCount));
    }

    [Fact]
    public async Task Measurement_sees_every_pending_cycle_including_the_makers_own_and_resolution_only_on_a_complete_read()
    {
        var finance = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve")
        {
            Result = ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId,
                [Item("finance", "finance.manual-bank-payment.v1", First, Early, Checker)], hasMore: true),
        };
        var fixture = Fixture([finance], policy: new(Company, null, 0, null, 0));

        var result = await fixture.Query.ListAsync(null, null, default);

        Assert.Empty(result.Resource!.Items);
        var observation = Assert.Single(fixture.Measurements.Observations);
        Assert.Equal((Company, "finance.manual-bank-payment.v1", false), (observation.CompanyId, observation.Kind, observation.Complete));
        Assert.Equal([new WorkApprovalInboxCycleV1(First, Early)], observation.Pending);
    }

    [Fact]
    public async Task A_kind_without_the_approvers_company_wide_permission_is_not_asked_and_not_listed()
    {
        var granted = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve")
        {
            Result = ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId,
                [Item("finance", "finance.manual-bank-payment.v1", First, Early, Maker)], hasMore: false),
        };
        var refused = new Source("sales", "sales.risk-exception.v1", "sales.orders.risk-exception.approve");
        var fixture = Fixture([granted, refused], policy: null);
        fixture.Authority.Scopes[refused.ApproverPermission] = Scope(granted: true, companyWide: false);

        var result = await fixture.Query.ListAsync(null, null, default);

        Assert.Equal(WorkInboxReadOutcomeV1.Found, result.Outcome);
        Assert.Equal(0, refused.Calls);
        Assert.Equal(["finance.manual-bank-payment.v1"], result.Resource!.Sources.Select(source => source.Kind).ToArray());
        Assert.Equal(WorkInboxDurationStatesV1.Unavailable, Assert.Single(result.Resource.Items).Duration.State);
    }

    [Fact]
    public async Task Without_any_decidable_kind_the_inbox_is_denied()
    {
        var source = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve");
        var fixture = Fixture([source], policy: null);
        fixture.Authority.Scopes[source.ApproverPermission] = PermissionScope.Denied;

        var result = await fixture.Query.ListAsync(null, null, default);

        Assert.Equal(WorkInboxReadOutcomeV1.Denied, result.Outcome);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("kind")]
    [InlineData("empty-label")]
    [InlineData("offset")]
    [InlineData("absolute-link")]
    [InlineData("no-rules")]
    [InlineData("foreign-rule-code")]
    [InlineData("bad-decimal")]
    [InlineData("unit-without-number")]
    [InlineData("bad-reference-role")]
    [InlineData("empty-preparer")]
    public async Task A_malformed_source_answer_marks_only_that_source_unavailable(string defect)
    {
        var broken = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve")
        {
            Result = ctx => Malformed(defect, ctx.CompanyId),
        };
        var healthy = new Source("logistics", "logistics.plan-excess.v1", "logistics.plan-excess.approve")
        {
            Result = ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId,
                [Item("logistics", "logistics.plan-excess.v1", Second, Late, Maker)], hasMore: false),
        };

        var result = await Fixture([broken, healthy], policy: null).Query.ListAsync(null, null, default);

        Assert.Equal(WorkInboxReadOutcomeV1.Found, result.Outcome);
        Assert.Equal([Second], result.Resource!.Items.Select(item => item.ItemId).ToArray());
        Assert.Equal(WorkInboxReadQueryV1.SourceUnavailable,
            result.Resource.Sources.Single(source => source.Module == "finance").State);
    }

    [Fact]
    public async Task An_unknown_view_or_kind_filter_is_invalid_and_a_known_kind_filter_asks_only_that_source()
    {
        var finance = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve")
        {
            Result = ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId, [], hasMore: false),
        };
        var logistics = new Source("logistics", "logistics.plan-excess.v1", "logistics.plan-excess.approve");
        var fixture = Fixture([finance, logistics], policy: null);

        Assert.Equal(WorkInboxReadOutcomeV1.Invalid, (await fixture.Query.ListAsync("prepared-by-me", null, default)).Outcome);
        Assert.Equal(WorkInboxReadOutcomeV1.Invalid, (await fixture.Query.ListAsync(null, "sales.unknown.v1", default)).Outcome);
        var filtered = await fixture.Query.ListAsync(WorkInboxViewsV1.AwaitingMyDecision, finance.Kind, default);

        Assert.Equal(WorkInboxReadOutcomeV1.Found, filtered.Outcome);
        Assert.Equal((1, 0), (finance.Calls, logistics.Calls));
    }

    [Fact]
    public async Task A_kind_registered_twice_makes_the_inbox_unavailable_and_a_native_kind_is_one_more_source_of_the_one_box()
    {
        var first = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve");
        var second = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve");
        var typed = new Source("sales", NativeApprovalKindsV1.SalesOrderDraft, "sales.orders.approve")
        {
            Result = ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId,
                [Item("sales", NativeApprovalKindsV1.SalesOrderDraft, First, Early, Maker)], hasMore: false),
        };
        var typedAgain = new Source("sales", NativeApprovalKindsV1.SalesOrderDraft, "sales.orders.approve");

        Assert.Equal(WorkInboxReadOutcomeV1.Unavailable,
            (await Fixture([first, second], policy: null).Query.ListAsync(null, null, default)).Outcome);
        Assert.Equal(WorkInboxReadOutcomeV1.Unavailable,
            (await Fixture([typed, typedAgain], policy: null).Query.ListAsync(null, null, default)).Outcome);
        // WP1 D5: the native sales order draft is listed in the same inbox, business-day class by the YOL-D18 default.
        var one = await Fixture([typed], policy: Defaults).Query.ListAsync(null, null, default);
        var item = Assert.Single(one.Resource!.Items);
        Assert.Equal((NativeApprovalKindsV1.SalesOrderDraft, "BusinessDays", "work.approval-rule:sales.order-draft.v1:0"),
            (item.Kind, item.Duration.Class, item.RuleVersion));
    }

    [Fact]
    public async Task A_company_switch_during_a_source_read_denies_the_whole_answer()
    {
        WorkInboxFixture? fixture = null;
        var source = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve")
        {
            Result = ctx =>
            {
                fixture!.Company.Set(OtherCompany);
                return WorkInboxSourceResultV1.Found(ctx.CompanyId, [], hasMore: false);
            },
        };
        fixture = Fixture([source], policy: null);

        var result = await fixture.Query.ListAsync(null, null, default);

        Assert.Equal(WorkInboxReadOutcomeV1.Denied, result.Outcome);
    }

    [Fact]
    public async Task An_interaction_is_recorded_only_for_an_item_the_caller_may_decide_now()
    {
        var finance = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve")
        {
            Result = ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId,
                [Item("finance", "finance.manual-bank-payment.v1", First, Early, Maker),
                    Item("finance", "finance.manual-bank-payment.v1", Second, Early, Checker)], hasMore: false),
        };
        var fixture = Fixture([finance], policy: null);
        var clientEventId = Guid.Parse("d3000000-0000-0000-0000-000000000001");

        var recorded = await fixture.Query.RecordEventAsync(
            new(clientEventId, finance.Kind, First, Early, "Opened"), default);
        var own = await fixture.Query.RecordEventAsync(
            new(Guid.NewGuid(), finance.Kind, Second, Early, "OwnerScreenOpened"), default);
        var staleCycle = await fixture.Query.RecordEventAsync(
            new(Guid.NewGuid(), finance.Kind, First, Late, "Opened"), default);
        var badType = await fixture.Query.RecordEventAsync(
            new(Guid.NewGuid(), finance.Kind, First, Early, "Arrived"), default);

        Assert.Equal((WorkInboxReadOutcomeV1.Found, clientEventId, false),
            (recorded.Outcome, recorded.Resource!.ClientEventId, recorded.Resource.Replayed));
        Assert.Equal(WorkInboxReadOutcomeV1.NotFound, own.Outcome);
        Assert.Equal(WorkInboxReadOutcomeV1.NotFound, staleCycle.Outcome);
        Assert.Equal(WorkInboxReadOutcomeV1.Invalid, badType.Outcome);
        var interaction = Assert.Single(fixture.Measurements.Interactions);
        Assert.Equal((clientEventId, First, WorkApprovalInboxEventTypeV1.Opened),
            (interaction.ClientEventId, interaction.ItemId, interaction.EventType));
    }

    [Fact]
    public void The_hour_class_adds_whole_hours_and_an_unrecorded_length_is_undefined()
    {
        var defined = WorkInboxDurationCalculatorV1.Compute(WorkApprovalDurationClassV1.Hours,
            new SettingsWorkApprovalDurationPolicyV1(Company, 4, 2, null, 0), Early, Now, Istanbul);
        var undefined = WorkInboxDurationCalculatorV1.Compute(WorkApprovalDurationClassV1.BusinessDays,
            new SettingsWorkApprovalDurationPolicyV1(Company, 4, 2, null, 0), Early, Now, Istanbul);

        Assert.Equal((WorkInboxDurationStatesV1.Defined, Early.AddHours(4), true, "work.approval-duration-hours:2"),
            (defined.State, defined.DueAtUtc, defined.IsOverdue, defined.PolicyVersion));
        Assert.Equal((WorkInboxDurationStatesV1.Undefined, (int?)null, (DateTimeOffset?)null),
            (undefined.State, undefined.Limit, undefined.DueAtUtc));
    }

    [Theory]
    [InlineData("2026-11-16T07:00:00Z", 2, "2026-11-18T07:00:00Z")] // Monday 10:00 local → Wednesday 10:00
    [InlineData("2026-11-20T12:00:00Z", 2, "2026-11-24T12:00:00Z")] // Friday 15:00 local → Tuesday 15:00
    [InlineData("2026-11-21T10:00:00Z", 1, "2026-11-23T21:00:00Z")] // Saturday → counted from Monday 00:00 local
    public void The_business_day_class_skips_weekends_in_the_business_time_zone(string start, int days, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected),
            WorkInboxDurationCalculatorV1.AddBusinessDays(DateTimeOffset.Parse(start), days, Istanbul));
    }

    [Fact]
    public async Task A_business_day_item_skips_the_recorded_holidays_and_says_when_a_year_has_no_calendar()
    {
        var finance = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve")
        {
            Result = ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId,
                [Item("finance", "finance.manual-bank-payment.v1", First, Early, Maker)], hasMore: false),
        };
        var policy = new SettingsWorkApprovalDurationPolicyV1(Company, 4, 1, 2, 1);
        var recorded = new Holidays(new SettingsWorkHolidayCalendarsV1(Company,
            [new SettingsWorkHolidayCalendarYearV1(2026, 1, [new DateOnly(2026, 11, 17)]),
                new SettingsWorkHolidayCalendarYearV1(2027, 1, [])]));
        var partial = new Holidays(new SettingsWorkHolidayCalendarsV1(Company,
            [new SettingsWorkHolidayCalendarYearV1(2027, 1, [])]));
        var unreadable = new Holidays(null);

        var defined = Assert.Single((await Fixture([finance], policy, recorded).Query.ListAsync(null, null, default)).Resource!.Items);
        var undefined = Assert.Single((await Fixture([finance], policy, partial).Query.ListAsync(null, null, default)).Resource!.Items);
        var unavailable = Assert.Single((await Fixture([finance], policy, unreadable).Query.ListAsync(null, null, default)).Resource!.Items);

        // Monday 2026-11-16 10:00 local + 2 business days, Tuesday 17th a holiday → Thursday 19th 10:00 local.
        Assert.Equal((DateTimeOffset.Parse("2026-11-19T07:00:00Z"), WorkInboxHolidayCalendarStatesV1.Defined),
            (defined.Duration.DueAtUtc, defined.Duration.HolidayCalendar));
        Assert.Equal((2026, 2027), recorded.Asked);
        Assert.Equal((DateTimeOffset.Parse("2026-11-18T07:00:00Z"), WorkInboxHolidayCalendarStatesV1.Undefined),
            (undefined.Duration.DueAtUtc, undefined.Duration.HolidayCalendar));
        Assert.Equal((DateTimeOffset.Parse("2026-11-18T07:00:00Z"), WorkInboxHolidayCalendarStatesV1.Unavailable),
            (unavailable.Duration.DueAtUtc, unavailable.Duration.HolidayCalendar));
    }

    [Fact]
    public void A_wait_starting_on_a_holiday_counts_from_the_next_business_day_and_the_hour_class_has_no_calendar()
    {
        var holidays = new Dictionary<int, IReadOnlySet<DateOnly>>
        {
            [2026] = new HashSet<DateOnly> { new(2026, 11, 16) },
        };
        var due = WorkInboxDurationCalculatorV1.AddBusinessDays(Early, 1, Istanbul, holidays, out var first, out var last);
        var hours = WorkInboxDurationCalculatorV1.Compute(WorkApprovalDurationClassV1.Hours,
            new SettingsWorkApprovalDurationPolicyV1(Company, 4, 1, 2, 1), Early, Now, Istanbul,
            new WorkInboxHolidaysV1(holidays));

        Assert.Equal((DateTimeOffset.Parse("2026-11-17T21:00:00Z"), 2026, 2026), (due, first, last));
        Assert.Null(hours.HolidayCalendar);
    }

    [Fact]
    public async Task The_rule_tables_recorded_version_gives_the_duration_class_and_its_version_read_in_the_callers_company()
    {
        var finance = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve")
        {
            Result = ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId,
                [Item("finance", "finance.manual-bank-payment.v1", First, Early, Maker)], hasMore: false),
        };
        var rules = new Rules();
        rules.Recorded[finance.Kind] = new WorkApprovalRuleResolutionV1(finance.Kind, WorkApprovalDurationClassV1.Hours,
            "finance.manual-bank-payment.approve", 3, false, true);

        var result = await Fixture([finance], Defaults, rules: rules).Query.ListAsync(null, null, default);

        var item = Assert.Single(result.Resource!.Items);
        Assert.Equal(("Hours", 4, Early.AddHours(4), "work.approval-rule:finance.manual-bank-payment.v1:3"),
            (item.Duration.Class, item.Duration.Limit!.Value, item.Duration.DueAtUtc!.Value, item.RuleVersion));
        var state = Assert.Single(result.Resource.Sources);
        Assert.Equal(("Hours", "work.approval-rule:finance.manual-bank-payment.v1:3"), (state.DurationClass, state.RuleVersion));
        Assert.Equal([Company], rules.AskedCompanies);
    }

    [Fact]
    public async Task A_rule_recorded_against_another_approver_permission_marks_only_that_source_unavailable_without_asking_it()
    {
        var finance = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve");
        var logistics = new Source("logistics", "logistics.plan-excess.v1", "logistics.plan-excess.approve")
        {
            Result = ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId,
                [Item("logistics", "logistics.plan-excess.v1", Second, Late, Maker)], hasMore: false),
        };
        var rules = new Rules();
        rules.Recorded[finance.Kind] = new WorkApprovalRuleResolutionV1(finance.Kind, WorkApprovalDurationClassV1.BusinessDays,
            null, 2, false, ApproverPermissionMatches: false);

        var result = await Fixture([finance, logistics], Defaults, rules: rules).Query.ListAsync(null, null, default);

        Assert.Equal(0, finance.Calls);
        Assert.Equal([Second], result.Resource!.Items.Select(item => item.ItemId).ToArray());
        Assert.Equal(WorkInboxReadQueryV1.SourceUnavailable, result.Resource.Sources.Single(source => source.Module == "finance").State);
    }

    [Fact]
    public async Task An_unreadable_rule_table_makes_the_inbox_unavailable_and_asks_no_source()
    {
        var finance = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve");

        var result = await Fixture([finance], Defaults, rules: new Rules { Unreadable = true }).Query.ListAsync(null, null, default);

        Assert.Equal((WorkInboxReadOutcomeV1.Unavailable, 0), (result.Outcome, finance.Calls));
    }

    [Fact]
    public async Task The_owners_may_not_decide_set_hides_the_item_and_a_maker_is_matched_as_the_owner_compares_actors()
    {
        const string actor = "a1b2c3d4-0000-4000-8000-000000000001";
        var fourth = Guid.Parse("d2000000-0000-0000-0000-000000000004");
        var purchasing = new Source("purchasing", "purchasing.incoming-invoice-variance.v1", "purchasing.invoices.approve-variance")
        {
            Result = ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId,
            [
                Item("purchasing", "purchasing.incoming-invoice-variance.v1", First, Early, actor.ToUpperInvariant()),
                Item("purchasing", "purchasing.incoming-invoice-variance.v1", Second, Early, Maker) with
                {
                    ExcludedDeciders = ["recorder", actor],
                },
                Item("purchasing", "purchasing.incoming-invoice-variance.v1", Third, Early, Maker) with { PreparedBy = null },
                Item("purchasing", "purchasing.incoming-invoice-variance.v1", fourth, Early, Maker) with
                {
                    ExcludedDeciders = ["recorder"],
                },
            ], hasMore: false),
        };
        var fixture = Fixture([purchasing], Defaults, actor: actor);

        var result = await fixture.Query.ListAsync(null, null, default);
        var excluded = await fixture.Query.RecordEventAsync(new(Guid.NewGuid(), purchasing.Kind, Second, Early, "Opened"), default);

        // D4 / ROL-D10 / YOL-D23: the maker (same Guid, other text form) and the owner's further exclusions are hidden; a record
        // whose owner names no maker and a record whose exclusions do not name the caller are shown.
        Assert.Equal([Third, fourth], result.Resource!.Items.Select(item => item.ItemId).Order().ToArray());
        Assert.Equal(WorkInboxReadOutcomeV1.NotFound, excluded.Outcome);
    }

    [Fact]
    public async Task A_scoped_grant_sees_only_its_scope_of_a_source_whose_owner_accepts_scoped_approvers()
    {
        var inside = Guid.Parse("d5000000-0000-0000-0000-000000000001");
        var outside = Guid.Parse("d5000000-0000-0000-0000-000000000002");
        var scoped = new Source("inventory", "inventory.scoped.v1", "inventory.scoped.approve", scoped: true)
        {
            Result = ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId,
            [
                Item("inventory", "inventory.scoped.v1", First, Early, Maker) with { Scope = new(null, inside, null) },
                Item("inventory", "inventory.scoped.v1", Second, Early, Maker) with { Scope = new(null, outside, null) },
            ], hasMore: false),
        };
        var companyOnly = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve");
        var fixture = Fixture([scoped, companyOnly], Defaults);
        fixture.Authority.Scopes[scoped.ApproverPermission] = Scoped(locations: [inside]);
        fixture.Authority.Scopes[companyOnly.ApproverPermission] = Scoped(locations: [inside]);

        var result = await fixture.Query.ListAsync(null, null, default);

        // D11: the scoped approver is asked with its scope, sees only the record inside it, and never closes other scopes' cycles.
        Assert.Equal([First], result.Resource!.Items.Select(item => item.ItemId).ToArray());
        Assert.Equal([inside], scoped.Context!.GrantScope!.LocationIds.ToArray());
        Assert.Equal(0, companyOnly.Calls);
        Assert.False(Assert.Single(fixture.Measurements.Observations).Complete);
    }

    [Fact]
    public async Task A_scoped_source_item_without_a_scope_under_a_scoped_read_marks_the_source_unavailable()
    {
        var inside = Guid.Parse("d5000000-0000-0000-0000-000000000001");
        var scoped = new Source("inventory", "inventory.scoped.v1", "inventory.scoped.approve", scoped: true)
        {
            Result = ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId,
                [Item("inventory", "inventory.scoped.v1", First, Early, Maker)], hasMore: false),
        };
        var fixture = Fixture([scoped], Defaults);
        fixture.Authority.Scopes[scoped.ApproverPermission] = Scoped(sites: [inside]);

        var result = await fixture.Query.ListAsync(null, null, default);

        Assert.Equal(WorkInboxReadOutcomeV1.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task Without_the_business_time_zone_a_business_day_duration_is_unavailable_and_the_hour_class_still_counts()
    {
        var finance = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve")
        {
            Result = ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId,
                [Item("finance", "finance.manual-bank-payment.v1", First, Early, Maker)], hasMore: false),
        };
        var logistics = new Source("logistics", "logistics.plan-excess.v1", "logistics.plan-excess.approve")
        {
            Result = ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId,
                [Item("logistics", "logistics.plan-excess.v1", Second, Late, Maker)], hasMore: false),
        };

        var result = await Fixture([finance, logistics], Defaults, zoneMissing: true).Query.ListAsync(null, null, default);

        Assert.Equal(WorkInboxDurationStatesV1.Unavailable, result.Resource!.Items.Single(item => item.ItemId == First).Duration.State);
        Assert.Equal((WorkInboxDurationStatesV1.Defined, Late.AddHours(4)),
            (result.Resource.Items.Single(item => item.ItemId == Second).Duration.State,
                result.Resource.Items.Single(item => item.ItemId == Second).Duration.DueAtUtc!.Value));
    }

    private static PermissionScope Scoped(Guid[]? sites = null, Guid[]? locations = null) => new(
        true, false, new HashSet<string>(StringComparer.Ordinal), new HashSet<Guid>(sites ?? []), new HashSet<Guid>(locations ?? []),
        new HashSet<Guid>());

    private static WorkInboxItemV1 Item(string module, string kind, Guid id, DateTimeOffset since, string preparedBy) =>
        new(module, kind, id, "DOC-" + id.ToString("N")[..4], since, $"/{module}/{id:D}", preparedBy,
            [new WorkApprovalRuleResultV1(module + ".rule", "10", "12.5", "2.5", "%")],
            [new WorkApprovalReferenceV1("party", "Cari A", Guid.Parse("d4000000-0000-0000-0000-000000000001"), null)]);

    private static WorkInboxSourceResultV1 Malformed(string defect, Guid companyId)
    {
        const string module = "finance";
        const string kind = "finance.manual-bank-payment.v1";
        var item = Item(module, kind, First, Early, Maker);
        return defect switch
        {
            "company" => WorkInboxSourceResultV1.Found(OtherCompany, [item], false),
            "kind" => WorkInboxSourceResultV1.Found(companyId, [item with { Kind = "finance.other.v1" }], false),
            "empty-label" => WorkInboxSourceResultV1.Found(companyId, [item with { DocumentLabel = " " }], false),
            "offset" => WorkInboxSourceResultV1.Found(companyId, [item with { WaitingSinceUtc = Early.ToOffset(TimeSpan.FromHours(3)) }], false),
            "absolute-link" => WorkInboxSourceResultV1.Found(companyId, [item with { Link = "https://example.test/x" }], false),
            "no-rules" => WorkInboxSourceResultV1.Found(companyId, [item with { Rules = [] }], false),
            "foreign-rule-code" => WorkInboxSourceResultV1.Found(companyId,
                [item with { Rules = [new WorkApprovalRuleResultV1("sales.rule", null, null, null, null)] }], false),
            "bad-decimal" => WorkInboxSourceResultV1.Found(companyId,
                [item with { Rules = [new WorkApprovalRuleResultV1("finance.rule", "1,5", null, null, null)] }], false),
            "unit-without-number" => WorkInboxSourceResultV1.Found(companyId,
                [item with { Rules = [new WorkApprovalRuleResultV1("finance.rule", null, null, null, "TRY")] }], false),
            "bad-reference-role" => WorkInboxSourceResultV1.Found(companyId,
                [item with { References = [new WorkApprovalReferenceV1("Party", "Cari", null, null)] }], false),
            "empty-preparer" => WorkInboxSourceResultV1.Found(companyId, [item with { PreparedBy = "" }], false),
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
    }

    private static PermissionScope Scope(bool granted, bool companyWide) => new(
        granted, companyWide, new HashSet<string>(StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(),
        new HashSet<Guid>());

    private static readonly TimeZoneInfo Istanbul = new SystemWorkBusinessTimeZoneSourceV1().BusinessTimeZone
        ?? throw new InvalidOperationException("Europe/Istanbul time zone rules are required by these tests.");

    /// <summary>The YOL-D18 decided defaults as Settings answers them before any version is recorded.</summary>
    private static readonly SettingsWorkApprovalDurationPolicyV1 Defaults = new(Company, 4, 0, 2, 0);

    private static WorkInboxFixture Fixture(IReadOnlyList<IWorkInboxSourceV1> sources,
        SettingsWorkApprovalDurationPolicyV1? policy, Holidays? holidays = null, Rules? rules = null,
        bool zoneMissing = false, string actor = Checker)
    {
        var company = new MutableCompanyContext(Company);
        var user = new MutableUser(actor);
        var correlation = new MutableCorrelation();
        var authority = new Authority();
        var measurements = new Measurements();
        rules ??= new Rules();
        var query = new WorkInboxReadQueryV1(company, user, correlation, authority, new Durations(policy),
            holidays ?? new Holidays(new SettingsWorkHolidayCalendarsV1(Company, [])), measurements, rules,
            new Zone(zoneMissing ? null : Istanbul), new FixedTime(Now), NullLogger<WorkInboxReadQueryV1>.Instance, sources);
        return new(query, company, authority, measurements, rules);
    }

    private sealed record WorkInboxFixture(
        WorkInboxReadQueryV1 Query,
        MutableCompanyContext Company,
        Authority Authority,
        Measurements Measurements,
        Rules Rules);

    /// <summary>The rule table double: the YOL-D18 default per kind unless a recorded version is set; unreadable = null.</summary>
    private sealed class Rules : IWorkApprovalRuleReaderV1
    {
        public Dictionary<string, WorkApprovalRuleResolutionV1> Recorded { get; } = new(StringComparer.Ordinal);
        public bool Unreadable { get; init; }
        public List<Guid> AskedCompanies { get; } = [];

        public Task<IReadOnlyDictionary<string, WorkApprovalRuleResolutionV1>?> ResolveAsync(Guid companyId,
            IReadOnlyList<IWorkInboxSourceV1> sources, CancellationToken cancellationToken)
        {
            AskedCompanies.Add(companyId);
            if (Unreadable)
            {
                return Task.FromResult<IReadOnlyDictionary<string, WorkApprovalRuleResolutionV1>?>(null);
            }
            var answer = sources.ToDictionary(source => source.Kind, source => Recorded.TryGetValue(source.Kind, out var rule)
                ? rule
                : new WorkApprovalRuleResolutionV1(source.Kind, WorkApprovalRuleDefaultsV1.DurationClassOf(source.Kind), null, 0, true, true),
                StringComparer.Ordinal);
            return Task.FromResult<IReadOnlyDictionary<string, WorkApprovalRuleResolutionV1>?>(answer);
        }
    }

    private sealed class Zone(TimeZoneInfo? zone) : IWorkBusinessTimeZoneSourceV1
    {
        public TimeZoneInfo? BusinessTimeZone => zone;
    }

    private sealed class Source(string module, string kind, string permission, bool scoped = false)
        : IWorkInboxSourceV1
    {
        public string Module => module;
        public string Kind => kind;
        public string ApproverPermission => permission;
        public bool AcceptsScopedApprovers => scoped;
        public Func<WorkInboxSourceContextV1, WorkInboxSourceResultV1> Result { get; init; } =
            ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId, [], false);
        public WorkInboxSourceContextV1? Context { get; private set; }
        public int MaxCount { get; private set; }
        public int Calls { get; private set; }

        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount,
            CancellationToken cancellationToken)
        {
            Calls++;
            Context = context;
            MaxCount = maxCount;
            return Task.FromResult(Result(context));
        }
    }

    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public Dictionary<string, PermissionScope> Scopes { get; } = new(StringComparer.Ordinal);

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Scopes.TryGetValue(permission, out var scope) ? scope : Scope(true, true));
    }

    private sealed class Durations(SettingsWorkApprovalDurationPolicyV1? policy) : ISettingsWorkApprovalDurationPolicyProviderV1
    {
        public Task<SettingsWorkApprovalDurationPolicyV1> GetWorkApprovalDurationAsync(Guid companyId,
            CancellationToken cancellationToken = default) =>
            policy is null ? throw new InvalidOperationException("Settings unavailable.") : Task.FromResult(policy);
    }

    private sealed class Holidays(SettingsWorkHolidayCalendarsV1? answer) : ISettingsWorkHolidayCalendarProviderV1
    {
        public (int From, int To)? Asked { get; private set; }

        public Task<SettingsWorkHolidayCalendarsV1> GetWorkHolidayCalendarsAsync(Guid companyId, int fromYear, int toYear,
            CancellationToken cancellationToken = default)
        {
            Asked = (fromYear, toYear);
            return answer is null ? throw new InvalidOperationException("Settings unavailable.") : Task.FromResult(answer);
        }
    }

    private sealed class Measurements : IWorkApprovalInboxMeasurementsV1
    {
        public List<(Guid CompanyId, string Kind, IReadOnlyList<WorkApprovalInboxCycleV1> Pending, bool Complete)> Observations { get; } = [];
        public List<WorkApprovalInboxInteractionV1> Interactions { get; } = [];

        public Task ObserveAsync(Guid companyId, string kind, IReadOnlyList<WorkApprovalInboxCycleV1> pending, bool complete,
            CancellationToken cancellationToken = default)
        {
            Observations.Add((companyId, kind, pending, complete));
            return Task.CompletedTask;
        }

        public Task<WorkApprovalInboxInteractionOutcomeV1> RecordInteractionAsync(Guid companyId,
            WorkApprovalInboxInteractionV1 interaction, CancellationToken cancellationToken = default)
        {
            Interactions.Add(interaction);
            return Task.FromResult(WorkApprovalInboxInteractionOutcomeV1.Recorded);
        }
        public Task<bool> RecordDecisionAsync(Guid companyId, string kind, WorkApprovalInboxCycleV1 cycle, DateTimeOffset decidedAtUtc,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<IReadOnlyList<WorkApprovalInboxCycleV1>> ListOpenCyclesAsync(Guid companyId, string kind,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkApprovalInboxCycleV1>>([]);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved { get; private set; } = true;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid value) { CompanyId = value; IsResolved = value != Guid.Empty; }
    }

    private sealed class MutableUser(string actorId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actorId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company, OtherCompany };
        public bool CanAccessCompany(Guid value) => AllowedCompanyIds.Contains(value);
    }

    private sealed class MutableCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "corr-1";
        public string? CausationId => "cause-1";
        public string SourceChannel => "api";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress,
            string? userAgent) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
    }
}
