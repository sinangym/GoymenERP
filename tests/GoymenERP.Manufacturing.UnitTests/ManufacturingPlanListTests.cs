using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// MFG-PLAN-LIST at the service and cursor boundary: the order list's rules for the company's plans. The service
/// answers the stored summaries as lossless text, passes the filter and cursor it was given, refuses a request
/// outside the contract before the store is asked, answers a refused cursor on <c>cursor</c> (never a first page),
/// and without the plan-list port answers a retryable 503, never an empty page. The cursor is bound to the company
/// and to the plan list's own ordering, so an order-list cursor is not a plan-list cursor and the reverse.
/// Its own fakes: the order store and command log here are never asked, and the shared command-service fixture is
/// not widened.
/// </summary>
public sealed class ManufacturingPlanListTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid WorkCenter = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Actor = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly DateTimeOffset At = new(2026, 9, 26, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_list_answers_the_stored_plan_summaries_as_text_with_the_page_the_store_gave()
    {
        var fixture = new Fixture();
        fixture.PlanList.Summaries.Add(Summary(PlanStatus.Draft, new Kilograms(1500.250m), new Kilograms(1200m)));
        fixture.PlanList.Summaries.Add(Summary(PlanStatus.Approved, new Kilograms(800m), target: null));
        fixture.PlanList.NextCursor = "cursor-2";

        var result = await fixture.Service.ListPlansAsync(null, null, null, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        var page = result.Resource!;
        Assert.Equal("cursor-2", page.NextCursor);
        Assert.Equal(["Draft", "Approved"], page.Plans.Select(summary => summary.Status));
        Assert.Equal(["Slitting", "Slitting"], page.Plans.Select(summary => summary.Process));
        // Kilograms keep the server's lossless text; a trailing zero is not a digit.
        Assert.Equal("1500.25", page.Plans[0].PlannedInputKg);
        Assert.Equal(2, page.Plans[0].PlannedRollCount);
        Assert.Equal(new TargetViewV1("InputMass", "1200"), page.Plans[0].Target);
        Assert.Null(page.Plans[0].ApprovedAtUtc);
        Assert.Null(page.Plans[1].Target);
        Assert.Equal(At, page.Plans[1].ApprovedAtUtc);
        // The default page and no filter: the store was asked exactly that.
        Assert.Equal((50, (string?)null, (PlanStatus?)null), Assert.Single(fixture.PlanList.Calls));
        // A read: no authority revalidation, no Settings read, no write, no command record.
        Assert.Empty(fixture.Unexpected);
    }

    [Fact]
    public async Task The_list_passes_the_status_filter_and_the_cursor_it_was_given()
    {
        var fixture = new Fixture();

        var draft = await fixture.Service.ListPlansAsync(10, "cursor-1", "Draft", CancellationToken.None);
        var approved = await fixture.Service.ListPlansAsync(100, null, "Approved", CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, ManufacturingApiStatus.Ok), (draft.Status, approved.Status));
        Assert.Equal(2, fixture.PlanList.Calls.Count);
        Assert.Equal((10, (string?)"cursor-1", (PlanStatus?)PlanStatus.Draft), fixture.PlanList.Calls[0]);
        Assert.Equal((100, (string?)null, (PlanStatus?)PlanStatus.Approved), fixture.PlanList.Calls[1]);
    }

    [Theory]
    [InlineData(0, null, null, "pageSize")]
    [InlineData(101, null, null, "pageSize")]
    [InlineData(null, "", null, "cursor")]
    [InlineData(null, null, "draft", "status")]
    [InlineData(null, null, "0", "status")]
    [InlineData(null, null, "Open", "status")]
    [InlineData(null, null, "Closed", "status")]
    [InlineData(null, null, "Converted", "status")]
    public async Task A_plan_list_request_outside_the_contract_is_invalid_and_the_store_is_not_asked(int? pageSize,
        string? cursor, string? status, string field)
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ListPlansAsync(pageSize, cursor, status, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, ManufacturingApiCodes.RequestInvalid, field),
            (result.Status, result.Code, result.Field));
        Assert.Empty(fixture.PlanList.Calls);
    }

    [Fact]
    public async Task A_cursor_longer_than_any_the_surface_produces_is_refused_before_the_store_is_asked()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ListPlansAsync(null, new string('a', 513), null, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, "cursor"), (result.Status, result.Field));
        Assert.Empty(fixture.PlanList.Calls);
    }

    [Fact]
    public async Task A_cursor_the_store_refuses_is_answered_on_the_cursor_never_as_a_first_page()
    {
        var fixture = new Fixture();
        fixture.PlanList.Summaries.Add(Summary(PlanStatus.Draft, new Kilograms(10m), target: null));
        fixture.PlanList.Fault = ManufacturingStoreCodes.ListCursorInvalid;

        var result = await fixture.Service.ListPlansAsync(null, "not-this-company", null, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, ManufacturingApiCodes.RequestInvalid, "cursor"),
            (result.Status, result.Code, result.Field));
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task A_read_that_keeps_meeting_commits_is_retryable_for_the_plan_list_too()
    {
        var fixture = new Fixture();
        fixture.PlanList.Fault = ManufacturingStoreCodes.ConcurrentChange;

        var result = await fixture.Service.ListPlansAsync(null, null, null, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Unavailable, result.Status);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Without_the_plan_list_port_the_answer_is_retryable_never_an_empty_page()
    {
        var fixture = new Fixture(withPlanList: false);

        var result = await fixture.Service.ListPlansAsync(null, null, null, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Unavailable, result.Status);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Without_a_resolved_company_or_its_membership_nothing_is_listed()
    {
        var noCompany = new Fixture(resolveCompany: false);
        var notMember = new Fixture();
        notMember.User.Companies.Clear();

        var first = await noCompany.Service.ListPlansAsync(null, null, null, CancellationToken.None);
        var second = await notMember.Service.ListPlansAsync(null, null, null, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, ManufacturingApiStatus.Denied), (first.Status, second.Status));
        Assert.Empty(noCompany.PlanList.Calls);
        Assert.Empty(notMember.PlanList.Calls);
    }

    [Fact]
    public void The_plan_cursor_round_trips_only_for_its_company_and_its_own_ordering()
    {
        var id = Guid.Parse("55555555-5555-4555-8555-555555555555");
        var planCursor = ManufacturingStore.ListCursor.Encode(Company, At, id, ManufacturingStore.ListCursor.PlanOrdering);
        var orderCursor = ManufacturingStore.ListCursor.Encode(Company, At, id);

        Assert.Equal((At, id),
            ManufacturingStore.ListCursor.Decode(planCursor, Company, ManufacturingStore.ListCursor.PlanOrdering));
        // The order list's cursor is unchanged by the plan list: it still round-trips on the order ordering.
        Assert.Equal((At, id), ManufacturingStore.ListCursor.Decode(orderCursor, Company));
        Assert.NotEqual(planCursor, orderCursor);

        // Another company's plan cursor, an order cursor on the plan list, a plan cursor on the order list and
        // an unreadable text are all refused on the cursor — never read as another list's page.
        Assert.Equal(ManufacturingStoreCodes.ListCursorInvalid, CodeOf(() =>
            ManufacturingStore.ListCursor.Decode(planCursor, OtherCompany, ManufacturingStore.ListCursor.PlanOrdering)));
        Assert.Equal(ManufacturingStoreCodes.ListCursorInvalid, CodeOf(() =>
            ManufacturingStore.ListCursor.Decode(orderCursor, Company, ManufacturingStore.ListCursor.PlanOrdering)));
        Assert.Equal(ManufacturingStoreCodes.ListCursorInvalid, CodeOf(() =>
            ManufacturingStore.ListCursor.Decode(planCursor, Company)));
        Assert.Equal(ManufacturingStoreCodes.ListCursorInvalid, CodeOf(() =>
            ManufacturingStore.ListCursor.Decode("not a cursor!", Company, ManufacturingStore.ListCursor.PlanOrdering)));
    }

    private static string CodeOf(Action action) =>
        Assert.Throws<ManufacturingStoreException>(action).Code;

    private static ManufacturingPlanSummary Summary(PlanStatus status, Kilograms plannedInput, Kilograms? target) =>
        new(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting, status, status == PlanStatus.Approved ? 2 : 1,
            2, plannedInput, target is null ? null : TargetBasis.InputMass, target, At,
            status == PlanStatus.Approved ? Actor : null, status == PlanStatus.Approved ? At : null);

    private sealed class Fixture
    {
        public Fixture(bool withPlanList = true, bool resolveCompany = true)
        {
            var company = new CompanyContext();
            if (resolveCompany)
            {
                company.Set(Company);
            }

            var correlation = new CorrelationContext();
            correlation.Set("corr-plan-list", null, "Web", null, null);
            var unused = new UnusedPorts(Unexpected);
            Service = new ManufacturingCommandService(unused, unused, unused, new ManufacturingMassVerifier(unused), unused,
                company, User, correlation, TimeProvider.System, planList: withPlanList ? PlanList : null);
        }

        /// <summary>Every call a list must not make: authority, Settings, a plan/order read or write, a command record.</summary>
        public List<string> Unexpected { get; } = [];

        public FakePlanList PlanList { get; } = new();

        public User User { get; } = new();

        public ManufacturingCommandService Service { get; }
    }

    private sealed class FakePlanList : IManufacturingPlanListStore
    {
        public List<ManufacturingPlanSummary> Summaries { get; } = [];

        public List<(int PageSize, string? Cursor, PlanStatus? Status)> Calls { get; } = [];

        public string? NextCursor { get; set; }

        public string? Fault { get; set; }

        public Task<ManufacturingPlanPage> ListPlansAsync(int pageSize, string? cursor, PlanStatus? status,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((pageSize, cursor, status));
            if (Fault is { } code)
            {
                throw new ManufacturingStoreException(code, "sentetik liste reddi");
            }

            var rows = Summaries.Where(summary => status is null || summary.Status == status).ToList();
            return Task.FromResult(new ManufacturingPlanPage(rows, NextCursor));
        }
    }

    private sealed class User : ICurrentUser
    {
        public HashSet<Guid> Companies { get; } = [Company];

        public bool IsAuthenticated => true;

        public string? UserId => Actor.ToString("D");

        public IReadOnlySet<Guid> AllowedCompanyIds => Companies;

        public bool CanAccessCompany(Guid companyId) => Companies.Contains(companyId);
    }

    /// <summary>The ports a list never uses: each call is recorded and refused, so a test can prove none was made.</summary>
    private sealed class UnusedPorts(List<string> calls)
        : IManufacturingPlanStore, IManufacturingOrderStore, IManufacturingCommandLog, IErpAuthorityRevalidationService,
            ISettingsPrerequisiteProvider
    {
        private Task<T> Refuse<T>(string name)
        {
            calls.Add(name);
            throw new NotSupportedException(name);
        }

        public Task<ManufacturingWriteOutcome> SavePlanAsync(ProductionPlan plan, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => Refuse<ManufacturingWriteOutcome>("save-plan");

        public Task<ProductionPlan> ApprovePlanAsync(Guid planId, long expectedVersion, Guid approvedByUserId,
            DateTimeOffset approvedAt, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            Refuse<ProductionPlan>("approve-plan");

        public Task<ProductionPlan?> FindPlanAsync(Guid planId, CancellationToken cancellationToken = default) =>
            Refuse<ProductionPlan?>("find-plan");

        public Task<ManufacturingWriteOutcome> CreateOrderAsync(Guid orderId, Guid planId, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => Refuse<ManufacturingWriteOutcome>("create-order");

        public Task<ManufacturingWriteOutcome> IssueInputAsync(Guid orderId, long expectedVersion, Guid issueId,
            Guid parentRollId, Kilograms mass, DateTimeOffset issuedAt, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => Refuse<ManufacturingWriteOutcome>("issue");

        public Task<ManufacturingWriteOutcome> ReturnUnusedInputAsync(Guid orderId, long expectedVersion, Guid returnId,
            Guid parentRollId, Kilograms mass, DateTimeOffset returnedAt, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => Refuse<ManufacturingWriteOutcome>("return");

        public Task<ManufacturingWriteOutcome> DeclareOutputAsync(Guid orderId, long expectedVersion, Guid outputId,
            Guid parentRollId, OutputClass outputClass, Kilograms mass, DateTimeOffset declaredAt,
            ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            Refuse<ManufacturingWriteOutcome>("output");

        public Task<ManufacturingWriteOutcome> RecordWorkInProgressAsync(Guid orderId, long expectedVersion, Guid measurementId,
            Guid parentRollId, Kilograms mass, DateTimeOffset measuredAt, ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => Refuse<ManufacturingWriteOutcome>("wip");

        public Task CloseOrderAsync(Guid orderId, long expectedVersion, Guid closedByUserId, DateTimeOffset closedAt,
            ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) => Refuse<bool>("close");

        public Task<ProductionOrder?> FindOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Refuse<ProductionOrder?>("find-order");

        public Task<ManufacturingOrderPage> ListOrdersAsync(int pageSize, string? cursor, OrderStatus? status,
            CancellationToken cancellationToken = default) => Refuse<ManufacturingOrderPage>("list-orders");

        public Task<ManufacturingCommandRecord?> FindCommandAsync(string commandName, string idempotencyKeyHash,
            CancellationToken cancellationToken = default) => Refuse<ManufacturingCommandRecord?>("find-command");

        public Task RecordCommandAsync(ManufacturingCommandRecord record, CancellationToken cancellationToken = default) =>
            Refuse<bool>("record-command");

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => Refuse<PermissionScope>("authority");

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => Refuse<UnitProfileV1>("settings");

        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => Refuse<MoneyProfileV1>("settings-money");

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => Refuse<PeriodEligibilityV1>("settings-period");
    }
}
