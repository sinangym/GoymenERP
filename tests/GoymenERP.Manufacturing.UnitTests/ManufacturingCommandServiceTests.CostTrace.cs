using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-S19 (B-27, DEC-PRDCT-009, API-42) at the service boundary: the cost trace is read under its own grant
/// (manufacturing.reports.read) measured after the reads, for the resolved company's order only. The cost figures come
/// only from the production cost port; with no port (Inventory gives no stamped issue cost yet) or a failing port the
/// cost part is "unavailable" with a reason, never a zero. Every figure here is a synthetic test value.
/// </summary>
public sealed partial class ManufacturingCommandServiceTests
{
    private static readonly Guid TraceSalesOrder = Guid.Parse("70000000-0000-0000-0000-00000000c019");
    private static readonly Guid TraceSalesOrderLine = Guid.Parse("71000000-0000-0000-0000-00000000c019");
    private static readonly Guid TraceProduct = Guid.Parse("80000000-0000-0000-0000-00000000c019");

    private static (Fixture Fixture, ManufacturingCommandService Service) CostTraceCase(
        IManufacturingScrapCostDistributionPortV1? port)
    {
        var fixture = new Fixture();
        var service = new ManufacturingCommandService(fixture.Store, fixture.Store, fixture.Store,
            new ManufacturingMassVerifier(fixture.Settings), fixture.Authority, fixture.CompanyContext, fixture.User,
            fixture.Correlation, new FixedTime(), costDistribution: port);
        return (fixture, service);
    }

    /// <summary>An approved order whose plan is stored too (the trace reads the plan's sales-order link).</summary>
    private static ProductionOrder TracedOrder(Fixture fixture, bool linked = false)
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(Roll, new Kilograms(1000m), commercialContext:
                new PlanCommercialContextSnapshotV1(Party, PartyAccount, "party-v1", "account-v1"))]);
        plan.WithDueDate().Approve(plan.Version, Actor, At);
        if (linked)
        {
            plan.ChangeSalesOrderLink(plan.Version, plan.SalesOrderLinkRevision,
                new PlanSalesOrderLink(TraceSalesOrder, TraceSalesOrderLine, Party, PartyAccount, TraceProduct, 2,
                    Actor.ToString("D"), At), orderClosed: false);
        }

        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
        fixture.Store.Plans[plan.Id] = plan;
        fixture.Store.Orders[order.Id] = order;
        return order;
    }

    private static ManufacturingScrapCostDistributionResultV1 Answer(Guid company, Guid order,
        ManufacturingScrapCostDistributionStatusV1 status, string? reason = null) =>
        status == ManufacturingScrapCostDistributionStatusV1.Available
            ? new ManufacturingScrapCostDistributionResultV1(1, company, order, status, "raw-material-kg", 12.5000m,
                12.25m, 3.10m, 1000m, [], ["paint_not_included"], null)
            : ManufacturingScrapCostDistributionResultV1.Empty(company, order, status, reason ?? "cost_pending");

    [Fact]
    public async Task The_trace_answers_the_order_raw_data_under_the_reports_grant_and_no_port_is_cost_unavailable()
    {
        var (fixture, service) = CostTraceCase(port: null);
        var order = TracedOrder(fixture);
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(400.5m), At);
        order.ReturnUnusedInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(0.25m), At.AddMinutes(1));
        order.DeclareOutput(order.Version, Guid.NewGuid(), Roll, OutputClass.GoodProduct, new Kilograms(390m),
            At.AddMinutes(2));
        var effects = (fixture.Store.Writes.Count, fixture.Store.Commands.Count, fixture.Store.AuditEvents.Count);
        fixture.Authority.Permissions.Clear();

        var result = await service.GetCostTraceAsync(order.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, false, (string?)null), (result.Status, result.IsReplay, result.Code));
        var view = result.Resource!;
        Assert.Equal((1, "weighted-average-raw-material-kg", order.Id, order.PlanId, WorkCenter, "Slitting", "Open"),
            (view.ContractVersion, view.MethodCode, view.OrderId, view.PlanId, view.WorkCenterId, view.Process,
                view.Status));
        var lot = Assert.Single(view.Lots);
        Assert.Equal((Roll, "1000", "400.5", "0.25", "400.25", 1, 1),
            (lot.ParentRollId, lot.PlannedKg, lot.IssuedKg, lot.ReturnedKg, lot.ConsumedKg, lot.IssueCount,
                lot.ReturnCount));
        Assert.Equal(((Guid?)Party, (Guid?)PartyAccount), (lot.CommercialPartyId, lot.CommercialPartyAccountId));
        Assert.Equal(("400.5", "0.25", "400.25"), (view.TotalIssuedKg, view.TotalReturnedKg, view.TotalConsumedKg));
        Assert.Equal([new CostTraceOutputViewV1("GoodProduct", "390", 1, 0)], view.Outputs);
        Assert.Equal(("time_records_not_recorded", "time_records_not_recorded"),
            (view.Timeline.StoppageCode, view.Timeline.DurationCode));
        Assert.Null(view.SalesOrderLink);
        // No issue-cost source yet: the cost part says why and carries no figure, never a zero.
        Assert.Equal(("unavailable", "cost_source_unavailable"), (view.Cost.Status, view.Cost.ReasonCode));
        Assert.Equal(((string?)null, (string?)null, (string?)null, (string?)null),
            (view.Cost.CostBasis, view.Cost.CostPerKgTl, view.Cost.RawMaterialCostPerKgTl,
                view.Cost.WeighedScrapExpenseTl));
        Assert.Empty(view.Cost.NoteCodes);
        Assert.Equal([ErpPermissions.ManufacturingReportsRead], fixture.Authority.Permissions);
        Assert.Equal((ErpPermissions.ManufacturingReportsRead, Company), fixture.Authority.LastAsked);
        Assert.Equal(effects,
            (fixture.Store.Writes.Count, fixture.Store.Commands.Count, fixture.Store.AuditEvents.Count));
    }

    [Fact]
    public async Task The_trace_shows_the_plan_sales_order_link_without_the_linking_actor()
    {
        var (fixture, service) = CostTraceCase(port: null);
        var order = TracedOrder(fixture, linked: true);

        var result = await service.GetCostTraceAsync(order.Id, CancellationToken.None);

        Assert.Equal(new CostTraceSalesOrderLinkViewV1(TraceSalesOrder, TraceSalesOrderLine, Party, TraceProduct, 2, At),
            result.Resource!.SalesOrderLink);
    }

    [Fact]
    public async Task An_available_port_answer_passes_its_figures_as_lossless_text()
    {
        var port = new FakeCostPort();
        var (fixture, service) = CostTraceCase(port);
        var order = TracedOrder(fixture);
        port.Answer = Answer(Company, order.Id, ManufacturingScrapCostDistributionStatusV1.Available);

        var result = await service.GetCostTraceAsync(order.Id, CancellationToken.None);

        var cost = result.Resource!.Cost;
        Assert.Equal(("available", "raw-material-kg", "12.5", "12.25", "3.1", (string?)null),
            (cost.Status, cost.CostBasis, cost.CostPerKgTl, cost.RawMaterialCostPerKgTl, cost.WeighedScrapExpenseTl,
                cost.ReasonCode));
        Assert.Equal(["paint_not_included"], cost.NoteCodes);
        Assert.Equal((Company, order.Id), Assert.Single(port.Calls));
    }

    [Theory]
    [InlineData(ManufacturingScrapCostDistributionStatusV1.Pending, "pending", "cost_pending")]
    [InlineData(ManufacturingScrapCostDistributionStatusV1.Unreadable, "unreadable", "cost_unreadable")]
    [InlineData(ManufacturingScrapCostDistributionStatusV1.Unavailable, "unavailable", "cost_source_unavailable")]
    [InlineData(ManufacturingScrapCostDistributionStatusV1.NotFound, "unavailable", "cost_answer_invalid")]
    public async Task A_port_answer_without_figures_carries_its_status_and_reason_and_no_figure(
        ManufacturingScrapCostDistributionStatusV1 status, string expectedStatus, string expectedReason)
    {
        var port = new FakeCostPort();
        var (fixture, service) = CostTraceCase(port);
        var order = TracedOrder(fixture);
        port.Answer = Answer(Company, order.Id, status,
            status == ManufacturingScrapCostDistributionStatusV1.NotFound ? "order_not_found" : expectedReason);

        var result = await service.GetCostTraceAsync(order.Id, CancellationToken.None);

        var cost = result.Resource!.Cost;
        Assert.Equal((expectedStatus, expectedReason), (cost.Status, cost.ReasonCode));
        Assert.Equal(((string?)null, (string?)null, (string?)null, (string?)null),
            (cost.CostBasis, cost.CostPerKgTl, cost.RawMaterialCostPerKgTl, cost.WeighedScrapExpenseTl));
    }

    [Theory]
    [InlineData("other-company")]
    [InlineData("other-order")]
    [InlineData("available-without-figures")]
    public async Task A_port_answer_for_another_scope_or_without_figures_is_cost_answer_invalid(string fault)
    {
        var port = new FakeCostPort();
        var (fixture, service) = CostTraceCase(port);
        var order = TracedOrder(fixture);
        port.Answer = fault switch
        {
            "other-company" => Answer(OtherCompany, order.Id, ManufacturingScrapCostDistributionStatusV1.Available),
            "other-order" => Answer(Company, Guid.NewGuid(), ManufacturingScrapCostDistributionStatusV1.Available),
            _ => Answer(Company, order.Id, ManufacturingScrapCostDistributionStatusV1.Available)
                with { CostPerKgTl = null },
        };

        var result = await service.GetCostTraceAsync(order.Id, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        var cost = result.Resource!.Cost;
        Assert.Equal(("unavailable", "cost_answer_invalid", (string?)null),
            (cost.Status, cost.ReasonCode, cost.CostPerKgTl));
    }

    [Fact]
    public async Task A_throwing_port_never_fails_the_trace_and_answers_cost_source_unavailable()
    {
        var port = new FakeCostPort { Fault = new InvalidOperationException("sentetik kaynak hatası") };
        var (fixture, service) = CostTraceCase(port);
        var order = TracedOrder(fixture);

        var result = await service.GetCostTraceAsync(order.Id, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        Assert.Equal(("unavailable", "cost_source_unavailable"),
            (result.Resource!.Cost.Status, result.Resource.Cost.ReasonCode));
    }

    [Fact]
    public async Task A_cancelled_port_read_is_not_swallowed()
    {
        var port = new FakeCostPort { Fault = new OperationCanceledException() };
        var (fixture, service) = CostTraceCase(port);
        var order = TracedOrder(fixture);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetCostTraceAsync(order.Id, CancellationToken.None));
    }

    [Fact]
    public async Task The_trace_answers_not_found_for_an_empty_unknown_or_other_company_order()
    {
        var (fixture, service) = CostTraceCase(port: null);
        var foreign = TracedOrder(fixture);
        // The order stays the first company's; the caller's resolved company becomes another one it may access.
        fixture.CompanyContext.SwitchForTest(OtherCompany);
        fixture.User.Companies.Add(OtherCompany);

        Assert.Equal(ManufacturingApiStatus.NotFound,
            (await service.GetCostTraceAsync(Guid.Empty, CancellationToken.None)).Status);
        Assert.Equal(ManufacturingApiStatus.NotFound,
            (await service.GetCostTraceAsync(Guid.NewGuid(), CancellationToken.None)).Status);
        var other = await service.GetCostTraceAsync(foreign.Id, CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.NotFound, (CostTraceViewV1?)null), (other.Status, other.Resource));
    }

    [Fact]
    public async Task A_stored_order_without_its_plan_fails_closed_as_a_retryable_503()
    {
        var (fixture, service) = CostTraceCase(port: null);
        var order = TracedOrder(fixture);
        fixture.Store.Plans.Remove(order.PlanId);

        var result = await service.GetCostTraceAsync(order.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unavailable, (CostTraceViewV1?)null), (result.Status, result.Resource));
    }

    [Fact]
    public async Task A_trace_read_meeting_concurrent_commits_is_a_retryable_503()
    {
        var (fixture, service) = CostTraceCase(port: null);
        var order = TracedOrder(fixture);
        fixture.Store.ReadFault = ManufacturingStoreCodes.ConcurrentChange;

        var result = await service.GetCostTraceAsync(order.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unavailable, (string?)null, (CostTraceViewV1?)null),
            (result.Status, result.Code, result.Resource));
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("actor")]
    [InlineData("company")]
    public async Task A_trace_read_rechecks_the_captured_context_and_the_reports_grant_after_the_read(string fault)
    {
        var port = new FakeCostPort();
        var (fixture, service) = CostTraceCase(port);
        var order = TracedOrder(fixture);
        port.Answer = Answer(Company, order.Id, ManufacturingScrapCostDistributionStatusV1.Available);
        fixture.Authority.Permissions.Clear();
        fixture.Store.AfterFindOrder = _ => ApplyFreshFenceFault(fixture, fault);

        var result = await service.GetCostTraceAsync(order.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, ManufacturingApiCodes.AuthorityDenied),
            (result.Status, result.Code));
        Assert.Null(result.Resource);
        Assert.Equal([ErpPermissions.ManufacturingReportsRead], fixture.Authority.Permissions);
    }

    [Fact]
    public async Task A_trace_without_a_resolved_company_context_is_denied_before_any_read()
    {
        var (fixture, service) = CostTraceCase(port: null);
        var order = TracedOrder(fixture);
        fixture.User.Companies.Clear();
        fixture.Authority.Permissions.Clear();

        var result = await service.GetCostTraceAsync(order.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, (CostTraceViewV1?)null), (result.Status, result.Resource));
    }

    [Fact]
    public void The_cost_view_refuses_a_null_answer_as_cost_answer_invalid()
    {
        var view = ManufacturingCommandService.CostView(Company, Guid.NewGuid(), null);

        Assert.Equal(("unavailable", "cost_answer_invalid"), (view.Status, view.ReasonCode));
    }

    /// <summary>The production cost port as a test double: one answer, or a fault thrown before answering.</summary>
    private sealed class FakeCostPort : IManufacturingScrapCostDistributionPortV1
    {
        public ManufacturingScrapCostDistributionResultV1? Answer { get; set; }

        public Exception? Fault { get; init; }

        public List<(Guid CompanyId, Guid OrderId)> Calls { get; } = [];

        public Task<ManufacturingScrapCostDistributionResultV1> DistributeAsync(Guid companyId, Guid orderId,
            CancellationToken cancellationToken)
        {
            Calls.Add((companyId, orderId));
            if (Fault is { } fault)
            {
                throw fault;
            }

            return Task.FromResult(Answer!);
        }
    }
}
