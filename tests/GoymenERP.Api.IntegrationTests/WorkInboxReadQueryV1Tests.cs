using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Work.Domain;
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
        var logistics = new Source("logistics", "logistics.plan-excess.v1", "logistics.plan-excess.approve",
            WorkApprovalDurationClassV1.Hours)
        {
            Result = ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId,
                [Item("logistics", "logistics.plan-excess.v1", Second, Late, Maker)], hasMore: false),
        };
        var finance = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve",
            WorkApprovalDurationClassV1.BusinessDays)
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
        var finance = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve",
            WorkApprovalDurationClassV1.BusinessDays)
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
        var granted = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve",
            WorkApprovalDurationClassV1.BusinessDays)
        {
            Result = ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId,
                [Item("finance", "finance.manual-bank-payment.v1", First, Early, Maker)], hasMore: false),
        };
        var refused = new Source("sales", "sales.risk-exception.v1", "sales.orders.risk-exception.approve",
            WorkApprovalDurationClassV1.BusinessDays);
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
        var source = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve",
            WorkApprovalDurationClassV1.BusinessDays);
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
        var broken = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve",
            WorkApprovalDurationClassV1.BusinessDays)
        {
            Result = ctx => Malformed(defect, ctx.CompanyId),
        };
        var healthy = new Source("logistics", "logistics.plan-excess.v1", "logistics.plan-excess.approve",
            WorkApprovalDurationClassV1.Hours)
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
        var finance = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve",
            WorkApprovalDurationClassV1.BusinessDays)
        {
            Result = ctx => WorkInboxSourceResultV1.Found(ctx.CompanyId, [], hasMore: false),
        };
        var logistics = new Source("logistics", "logistics.plan-excess.v1", "logistics.plan-excess.approve",
            WorkApprovalDurationClassV1.Hours);
        var fixture = Fixture([finance, logistics], policy: null);

        Assert.Equal(WorkInboxReadOutcomeV1.Invalid, (await fixture.Query.ListAsync("prepared-by-me", null, default)).Outcome);
        Assert.Equal(WorkInboxReadOutcomeV1.Invalid, (await fixture.Query.ListAsync(null, "sales.unknown.v1", default)).Outcome);
        var filtered = await fixture.Query.ListAsync(WorkInboxViewsV1.AwaitingMyDecision, finance.Kind, default);

        Assert.Equal(WorkInboxReadOutcomeV1.Found, filtered.Outcome);
        Assert.Equal((1, 0), (finance.Calls, logistics.Calls));
    }

    [Fact]
    public async Task A_kind_registered_twice_or_reusing_a_typed_kind_makes_the_inbox_unavailable()
    {
        var first = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve",
            WorkApprovalDurationClassV1.BusinessDays);
        var second = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve",
            WorkApprovalDurationClassV1.BusinessDays);
        var typed = new Source("sales", NativeApprovalKindsV1.SalesOrderDraft, "sales.orders.approve",
            WorkApprovalDurationClassV1.BusinessDays);

        Assert.Equal(WorkInboxReadOutcomeV1.Unavailable,
            (await Fixture([first, second], policy: null).Query.ListAsync(null, null, default)).Outcome);
        Assert.Equal(WorkInboxReadOutcomeV1.Unavailable,
            (await Fixture([typed], policy: null).Query.ListAsync(null, null, default)).Outcome);
    }

    [Fact]
    public async Task A_company_switch_during_a_source_read_denies_the_whole_answer()
    {
        WorkInboxFixture? fixture = null;
        var source = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve",
            WorkApprovalDurationClassV1.BusinessDays)
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
        var finance = new Source("finance", "finance.manual-bank-payment.v1", "finance.manual-bank-payment.approve",
            WorkApprovalDurationClassV1.BusinessDays)
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
            new SettingsWorkApprovalDurationPolicyV1(Company, 4, 2, null, 0), Early, Now);
        var undefined = WorkInboxDurationCalculatorV1.Compute(WorkApprovalDurationClassV1.BusinessDays,
            new SettingsWorkApprovalDurationPolicyV1(Company, 4, 2, null, 0), Early, Now);

        Assert.Equal((WorkInboxDurationStatesV1.Defined, Early.AddHours(4), true, "work.approval-duration-hours:2"),
            (defined.State, defined.DueAtUtc, defined.IsOverdue, defined.PolicyVersion));
        Assert.Equal((WorkInboxDurationStatesV1.Undefined, (int?)null, (DateTimeOffset?)null),
            (undefined.State, undefined.Limit, undefined.DueAtUtc));
    }

    [Theory]
    [InlineData("2026-11-16T07:00:00Z", 2, "2026-11-18T07:00:00Z")] // Monday 10:00 local → Wednesday 10:00
    [InlineData("2026-11-20T12:00:00Z", 2, "2026-11-24T12:00:00Z")] // Friday 15:00 local → Tuesday 15:00
    [InlineData("2026-11-21T10:00:00Z", 1, "2026-11-23T21:00:00Z")] // Saturday → counted from Monday 00:00 local
    public void The_business_day_class_skips_weekends_in_turkey_local_time(string start, int days, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected),
            WorkInboxDurationCalculatorV1.AddBusinessDays(DateTimeOffset.Parse(start), days));
    }

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

    private static WorkInboxFixture Fixture(IReadOnlyList<IWorkInboxSourceV1> sources,
        SettingsWorkApprovalDurationPolicyV1? policy)
    {
        var company = new MutableCompanyContext(Company);
        var user = new MutableUser(Checker);
        var correlation = new MutableCorrelation();
        var authority = new Authority();
        var measurements = new Measurements();
        var query = new WorkInboxReadQueryV1(company, user, correlation, authority, new Durations(policy), measurements,
            new FixedTime(Now), NullLogger<WorkInboxReadQueryV1>.Instance, sources);
        return new(query, company, authority, measurements);
    }

    private sealed record WorkInboxFixture(
        WorkInboxReadQueryV1 Query,
        MutableCompanyContext Company,
        Authority Authority,
        Measurements Measurements);

    private sealed class Source(string module, string kind, string permission, WorkApprovalDurationClassV1 durationClass)
        : IWorkInboxSourceV1
    {
        public string Module => module;
        public string Kind => kind;
        public string ApproverPermission => permission;
        public WorkApprovalDurationClassV1 DurationClass => durationClass;
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
