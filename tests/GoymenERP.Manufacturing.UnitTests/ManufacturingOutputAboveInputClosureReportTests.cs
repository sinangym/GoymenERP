using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Balance;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-D42 (3) at the service and cursor boundary: the read-only report of orders closed under the earlier rule with
/// output above input. A page of the company's closed orders is examined through the close's own definition
/// (<see cref="MassBalance.OutputExceedsInput"/>); only the orders with such a parent roll are listed, and only those
/// rolls (URT-D42 (1)). The order read's grant is measured again after the read, a page that breaks the port's contract
/// fails closed with nothing listed, and nothing is written. Its own fakes, like the plan list's.
/// </summary>
public sealed class ManufacturingOutputAboveInputClosureReportTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-11111111e042");
    private static readonly Guid OtherCompany = Guid.Parse("22222222-2222-4222-8222-22222222e042");
    private static readonly Guid WorkCenter = Guid.Parse("33333333-3333-4333-8333-33333333e042");
    private static readonly Guid Actor = Guid.Parse("44444444-4444-4444-8444-44444444e042");
    private static readonly Guid Planner = Guid.Parse("44444444-4444-4444-8444-44444444e043");
    private static readonly Guid Closer = Guid.Parse("44444444-4444-4444-8444-44444444e044");
    private static readonly Guid Profile = Guid.Parse("55555555-5555-4555-8555-55555555e042");
    private static readonly Guid RollA = Guid.Parse("66666666-6666-4666-8666-66666666e001");
    private static readonly Guid RollB = Guid.Parse("66666666-6666-4666-8666-66666666e002");
    // Closed the day before URT-D41 (2026-11-13); the policy is the one selected for the close's UTC business date.
    private static readonly DateOnly CloseDay = new(2026, 11, 12);
    private static readonly DateTimeOffset CloseAt = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_report_lists_only_the_closed_orders_with_output_above_input_and_only_those_rolls()
    {
        var fixture = new Fixture();
        // Roll A: 15 kg more output than input, inside the earlier ± 2 % (closed without approval); roll B: 10 kg short.
        var withinTolerance = OpenOrder(Company, RollA, RollB);
        Realize(withinTolerance, RollA, 1_000m, 30m, 985m);
        Realize(withinTolerance, RollB, 1_000m, 30m, 960m);
        // 50 kg more output than input, beyond the earlier tolerance: closed with the earlier exception approval.
        var approved = OpenOrder(Company, RollA);
        Realize(approved, RollA, 1_000m, 30m, 1_020m);
        // A shortfall inside the tolerance and a legacy close that reconciles exactly: examined, not listed.
        var shortfall = OpenOrder(Company, RollA);
        Realize(shortfall, RollA, 1_000m, 30m, 960m);
        var legacy = OpenOrder(Company, RollA);
        Realize(legacy, RollA, 1_000m, 30m, 970m);
        legacy.Close(legacy.Version, Closer, CloseAt);
        fixture.ClosedOrders.Orders.AddRange([StoredClose(withinTolerance), StoredClose(approved, withApproval: true),
            StoredClose(shortfall), legacy]);
        fixture.ClosedOrders.NextCursor = "cursor-2";
        var versions = fixture.ClosedOrders.Orders.Select(order => (order.Id, order.Status, order.Version)).ToList();

        var result = await fixture.Service.ListOutputAboveInputClosuresAsync(null, null, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        var report = result.Resource!;
        Assert.Equal((MassBalanceEngineV1.FormulaVersion, 4, "cursor-2"),
            (report.FormulaVersion, report.ExaminedOrderCount, report.NextCursor));
        Assert.Equal([withinTolerance.Id, approved.Id], report.Closures.Select(closure => closure.OrderId));

        var first = report.Closures[0];
        Assert.Equal((withinTolerance.PlanId, WorkCenter, "Slitting", withinTolerance.Version + 1, Closer, CloseAt, false),
            (first.PlanId, first.WorkCenterId, first.Process, first.Version, first.ClosedByUserId, first.ClosedAtUtc,
                first.ClosedWithExceptionApproval));
        // Per roll: roll B's shortfall does not offset roll A, and roll B is not listed.
        var roll = Assert.Single(first.Rolls);
        Assert.Equal(new OutputAboveInputRollViewV1(RollA, new MassDifferenceViewV1("1000", "1015", "-15", false)), roll);

        var second = report.Closures[1];
        Assert.True(second.ClosedWithExceptionApproval);
        Assert.Equal(new OutputAboveInputRollViewV1(RollA, new MassDifferenceViewV1("1000", "1050", "-50", false)),
            Assert.Single(second.Rolls));

        // The default page and no cursor; the order read's grant measured once, for this company.
        Assert.Equal((50, (string?)null), Assert.Single(fixture.ClosedOrders.Calls));
        Assert.Equal((ErpPermissions.ManufacturingOrdersRead, Company), Assert.Single(fixture.Authority.Calls));
        // A read: no order changed, no write, no command record, no Settings read.
        Assert.Equal(versions, fixture.ClosedOrders.Orders.Select(order => (order.Id, order.Status, order.Version)));
        Assert.Empty(fixture.Unexpected);
    }

    [Fact]
    public async Task A_page_without_output_above_input_lists_nothing_and_still_hands_on_the_cursor_it_was_given()
    {
        var fixture = new Fixture();
        var shortfall = OpenOrder(Company, RollA);
        Realize(shortfall, RollA, 1_000m, 30m, 950m);
        fixture.ClosedOrders.Orders.Add(StoredClose(shortfall));
        fixture.ClosedOrders.NextCursor = "cursor-3";

        var result = await fixture.Service.ListOutputAboveInputClosuresAsync(10, "cursor-2", CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        Assert.Equal((1, 0, "cursor-3"),
            (result.Resource!.ExaminedOrderCount, result.Resource.Closures.Count, result.Resource.NextCursor));
        Assert.Equal((10, (string?)"cursor-2"), Assert.Single(fixture.ClosedOrders.Calls));
    }

    [Theory]
    [InlineData(0, null, "pageSize")]
    [InlineData(101, null, "pageSize")]
    [InlineData(-1, null, "pageSize")]
    [InlineData(null, "", "cursor")]
    public async Task A_report_request_outside_the_contract_is_invalid_and_nothing_is_asked(int? pageSize,
        string? cursor, string field)
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ListOutputAboveInputClosuresAsync(pageSize, cursor, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, ManufacturingApiCodes.RequestInvalid, field),
            (result.Status, result.Code, result.Field));
        Assert.Empty(fixture.ClosedOrders.Calls);
        Assert.Empty(fixture.Authority.Calls);
    }

    [Fact]
    public async Task A_cursor_longer_than_any_the_surface_produces_is_refused_before_the_store_is_asked()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ListOutputAboveInputClosuresAsync(null, new string('a', 513),
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, "cursor"), (result.Status, result.Field));
        Assert.Empty(fixture.ClosedOrders.Calls);
    }

    [Fact]
    public async Task A_cursor_the_store_refuses_is_answered_on_the_cursor_never_as_a_first_page()
    {
        var fixture = new Fixture();
        fixture.ClosedOrders.Fault = ManufacturingStoreCodes.ListCursorInvalid;

        var result = await fixture.Service.ListOutputAboveInputClosuresAsync(null, "not-this-company",
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, ManufacturingApiCodes.RequestInvalid, "cursor"),
            (result.Status, result.Code, result.Field));
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task A_read_that_meets_a_concurrent_change_is_retryable()
    {
        var fixture = new Fixture();
        fixture.ClosedOrders.Fault = ManufacturingStoreCodes.ConcurrentChange;

        var result = await fixture.Service.ListOutputAboveInputClosuresAsync(null, null, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Unavailable, result.Status);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Without_the_closed_order_port_the_answer_is_retryable_never_an_empty_page()
    {
        var fixture = new Fixture(withClosedOrders: false);

        var result = await fixture.Service.ListOutputAboveInputClosuresAsync(null, null, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Unavailable, result.Status);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Without_a_resolved_company_or_its_membership_nothing_is_listed()
    {
        var noCompany = new Fixture(resolveCompany: false);
        var notMember = new Fixture();
        notMember.User.Companies.Clear();

        var first = await noCompany.Service.ListOutputAboveInputClosuresAsync(null, null, CancellationToken.None);
        var second = await notMember.Service.ListOutputAboveInputClosuresAsync(null, null, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, ManufacturingApiStatus.Denied), (first.Status, second.Status));
        Assert.Empty(noCompany.ClosedOrders.Calls);
        Assert.Empty(notMember.ClosedOrders.Calls);
    }

    [Fact]
    public async Task A_grant_or_membership_lost_while_the_page_was_read_answers_nothing_of_it()
    {
        var withdrawn = new Fixture();
        withdrawn.ClosedOrders.Orders.Add(ApprovedExcessClose(Company));
        withdrawn.Authority.Granted = false;
        var leftCompany = new Fixture();
        leftCompany.ClosedOrders.Orders.Add(ApprovedExcessClose(Company));
        leftCompany.ClosedOrders.OnList = () => leftCompany.User.Companies.Clear();

        var first = await withdrawn.Service.ListOutputAboveInputClosuresAsync(null, null, CancellationToken.None);
        var second = await leftCompany.Service.ListOutputAboveInputClosuresAsync(null, null, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, (OutputAboveInputClosureReportViewV1?)null),
            (first.Status, first.Resource));
        Assert.Equal((ManufacturingApiStatus.Denied, (OutputAboveInputClosureReportViewV1?)null),
            (second.Status, second.Resource));
    }

    [Fact]
    public async Task A_page_holding_another_companys_order_or_an_open_order_fails_closed_with_nothing_listed()
    {
        var foreign = new Fixture();
        foreign.ClosedOrders.Orders.AddRange([ApprovedExcessClose(Company), ApprovedExcessClose(OtherCompany)]);
        var open = new Fixture();
        var stillOpen = OpenOrder(Company, RollA);
        Realize(stillOpen, RollA, 1_000m, 30m, 1_020m);
        open.ClosedOrders.Orders.AddRange([ApprovedExcessClose(Company), stillOpen]);

        var first = await foreign.Service.ListOutputAboveInputClosuresAsync(null, null, CancellationToken.None);
        var second = await open.Service.ListOutputAboveInputClosuresAsync(null, null, CancellationToken.None);

        // Never a partial page: the valid closure in the same page is not answered either.
        Assert.Equal((ManufacturingApiStatus.Unavailable, (OutputAboveInputClosureReportViewV1?)null),
            (first.Status, first.Resource));
        Assert.Equal((ManufacturingApiStatus.Unavailable, (OutputAboveInputClosureReportViewV1?)null),
            (second.Status, second.Resource));
    }

    [Fact]
    public void The_closed_order_cursor_round_trips_only_for_its_company_and_its_own_ordering()
    {
        var id = Guid.Parse("77777777-7777-4777-8777-77777777e042");
        var closedCursor = ManufacturingStore.ListCursor.Encode(Company, CloseAt, id,
            ManufacturingStore.ListCursor.ClosedOrdering);
        var orderCursor = ManufacturingStore.ListCursor.Encode(Company, CloseAt, id);
        var planCursor = ManufacturingStore.ListCursor.Encode(Company, CloseAt, id,
            ManufacturingStore.ListCursor.PlanOrdering);

        Assert.Equal((CloseAt, id),
            ManufacturingStore.ListCursor.Decode(closedCursor, Company, ManufacturingStore.ListCursor.ClosedOrdering));
        Assert.Equal(3, new HashSet<string>([closedCursor, orderCursor, planCursor]).Count);

        // Another company's report cursor, an order-list or plan-list cursor on the report, a report cursor on either
        // list and an unreadable text are all refused on the cursor — never read as another list's page.
        Assert.Equal(ManufacturingStoreCodes.ListCursorInvalid, CodeOf(() => ManufacturingStore.ListCursor.Decode(
            closedCursor, OtherCompany, ManufacturingStore.ListCursor.ClosedOrdering)));
        Assert.Equal(ManufacturingStoreCodes.ListCursorInvalid, CodeOf(() => ManufacturingStore.ListCursor.Decode(
            orderCursor, Company, ManufacturingStore.ListCursor.ClosedOrdering)));
        Assert.Equal(ManufacturingStoreCodes.ListCursorInvalid, CodeOf(() => ManufacturingStore.ListCursor.Decode(
            planCursor, Company, ManufacturingStore.ListCursor.ClosedOrdering)));
        Assert.Equal(ManufacturingStoreCodes.ListCursorInvalid, CodeOf(() => ManufacturingStore.ListCursor.Decode(
            closedCursor, Company)));
        Assert.Equal(ManufacturingStoreCodes.ListCursorInvalid, CodeOf(() => ManufacturingStore.ListCursor.Decode(
            closedCursor, Company, ManufacturingStore.ListCursor.PlanOrdering)));
        Assert.Equal(ManufacturingStoreCodes.ListCursorInvalid, CodeOf(() => ManufacturingStore.ListCursor.Decode(
            "not a cursor!", Company, ManufacturingStore.ListCursor.ClosedOrdering)));
    }

    private static string CodeOf(Action action) =>
        Assert.Throws<ManufacturingStoreException>(action).Code;

    private static Kilograms Kg(decimal value) => new(value);

    private static ProductionOrder OpenOrder(Guid company, params Guid[] rolls)
    {
        var plan = new ProductionPlan(Guid.NewGuid(), company, WorkCenter, ManufacturingProcess.Slitting,
            rolls.Select(roll => new PlannedInput(roll, Kg(10_000m))));
        plan.Approve(plan.Version, Planner, DateTimeOffset.UnixEpoch);
        return ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
    }

    private static void Realize(ProductionOrder order, Guid roll, decimal issued, decimal scrap, decimal good)
    {
        order.Issue(roll, Kg(issued));
        order.Declare(roll, OutputClass.Scrap, Kg(scrap));
        order.Declare(roll, OutputClass.GoodProduct, Kg(good));
    }

    /// <summary>The reviewed version-0 catalog: fire maximum 5 %, the earlier ± 2 % mass-balance tolerance.</summary>
    private static ClosureTolerancePolicySnapshotV1 Catalog(Guid company) =>
        new(company, Profile, 0, ManufacturingProcess.Slitting, 5m, true, new DateOnly(2026, 11, 1), null, CloseDay, 2m,
            ClosureTolerancePolicySnapshotV1.CatalogSource);

    /// <summary>
    /// A close stored under the earlier rule, rebuilt from its facts the way the store rebuilds it (URT-D42 (3)): the
    /// stored close replays unchanged even where a new close would now be refused.
    /// </summary>
    private static ProductionOrder StoredClose(ProductionOrder open, bool withApproval = false)
    {
        var catalog = Catalog(open.CompanyId);
        var approval = withApproval
            ? ClosureExceptionApprovalV1.Create(open.CompanyId, open.Id, Closer, "SENTETIK teyitli sapma", open.Version,
                catalog.PolicyId, catalog.PolicyVersion, CloseAt)
            : null;
        return ProductionOrder.Restore(open.Id, open.PlanId, open.CompanyId, open.WorkCenterId, open.Process,
            open.Target, open.PlannedInputs, open.Issues, open.Returns, open.Outputs, open.WipMeasurements, Closer, CloseAt,
            open.Version + 1, catalog, approval);
    }

    private static ProductionOrder ApprovedExcessClose(Guid company)
    {
        var open = OpenOrder(company, RollA);
        Realize(open, RollA, 1_000m, 30m, 1_020m);
        return StoredClose(open, withApproval: true);
    }

    private sealed class Fixture
    {
        public Fixture(bool withClosedOrders = true, bool resolveCompany = true)
        {
            var company = new CompanyContext();
            if (resolveCompany)
            {
                company.Set(Company);
            }

            var correlation = new CorrelationContext();
            correlation.Set("corr-output-above-input-report", null, "Web", null, null);
            var unused = new UnusedPorts(Unexpected);
            Service = new ManufacturingCommandService(unused, unused, unused, new ManufacturingMassVerifier(unused),
                Authority, company, User, correlation, TimeProvider.System,
                closedOrders: withClosedOrders ? ClosedOrders : null);
        }

        /// <summary>Every call the report must not make: a plan/order read or write, a command record, Settings.</summary>
        public List<string> Unexpected { get; } = [];

        public FakeClosedOrders ClosedOrders { get; } = new();

        public FakeAuthority Authority { get; } = new();

        public User User { get; } = new();

        public ManufacturingCommandService Service { get; }
    }

    private sealed class FakeClosedOrders : IManufacturingClosedOrderListStore
    {
        public List<ProductionOrder> Orders { get; } = [];

        public List<(int PageSize, string? Cursor)> Calls { get; } = [];

        public string? NextCursor { get; set; }

        public string? Fault { get; set; }

        public Action? OnList { get; set; }

        public Task<ManufacturingClosedOrderPage> ListClosedOrdersAsync(int pageSize, string? cursor,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((pageSize, cursor));
            OnList?.Invoke();
            if (Fault is { } code)
            {
                throw new ManufacturingStoreException(code, "sentetik liste reddi");
            }

            return Task.FromResult(new ManufacturingClosedOrderPage([.. Orders], NextCursor));
        }
    }

    private sealed class FakeAuthority : IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true;

        public List<(string Permission, Guid CompanyId)> Calls { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((permission, companyId));
            return Task.FromResult(Granted
                ? new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal), new HashSet<Guid>(),
                    new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
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

    /// <summary>The ports the report never uses: each call is recorded and refused, so a test can prove none was made.</summary>
    private sealed class UnusedPorts(List<string> calls)
        : IManufacturingPlanStore, IManufacturingOrderStore, IManufacturingCommandLog, ISettingsPrerequisiteProvider
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

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => Refuse<UnitProfileV1>("settings");

        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => Refuse<MoneyProfileV1>("settings-money");

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => Refuse<PeriodEligibilityV1>("settings-period");
    }
}
