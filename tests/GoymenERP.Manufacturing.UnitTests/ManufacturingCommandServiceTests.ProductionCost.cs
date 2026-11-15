using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-S17 (SAT-D317) at the service boundary: the order cost view is read under the reports grant
/// (manufacturing.reports.read) measured after the reads, for the resolved company's order only. Every figure comes
/// from the production cost port version 2 as recorded there; with no port (Inventory gives no stamped issue cost yet)
/// or a failing port the view is "unavailable" with a reason, never a zero. Every figure here is a synthetic test value.
/// </summary>
public sealed partial class ManufacturingCommandServiceTests
{
    private static readonly Guid CostRemainderLabel = Guid.Parse("a1000000-0000-0000-0000-00000000c017");
    private static readonly Guid CostSecondLabel = Guid.Parse("a2000000-0000-0000-0000-00000000c017");
    private static readonly Guid CostScrapLabel = Guid.Parse("a3000000-0000-0000-0000-00000000c017");
    private static readonly Guid CostIssue = Guid.Parse("b1000000-0000-0000-0000-00000000c017");
    private static readonly Guid CostCurrency = Guid.Parse("c1000000-0000-0000-0000-00000000c017");

    private static (Fixture Fixture, ManufacturingCommandService Service) ProductionCostCase(
        IManufacturingProductionCostPortV2? port)
    {
        var fixture = new Fixture();
        var service = new ManufacturingCommandService(fixture.Store, fixture.Store, fixture.Store,
            new ManufacturingMassVerifier(fixture.Settings), fixture.Authority, fixture.CompanyContext, fixture.User,
            fixture.Correlation, new FixedTime(), productionCost: port);
        return (fixture, service);
    }

    private static ProductionOrder CostedOrder(Fixture fixture)
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(Roll, new Kilograms(1000m))]);
        plan.WithDueDate().Approve(plan.Version, Actor, At);
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
        fixture.Store.Plans[plan.Id] = plan;
        fixture.Store.Orders[order.Id] = order;
        return order;
    }

    /// <summary>
    /// A recorded answer: 2 good product lines and a scrap line; the remainder (-0.01 TL, already inside that line's
    /// amount) sits on the good product line with the most kg, so the TL that went in (1000) equals the TL distributed.
    /// </summary>
    private static ManufacturingProductionCostResultV2 RecordedCost(Guid company, Guid order) =>
        new(ManufacturingProductionCostResultV2.CurrentContractVersion, company, order,
            ManufacturingScrapCostDistributionStatusV1.Available, "production-input-average-plus-scrap", 12.3457m,
            12.1000m, 1000.00m, 0m, 1000.00m, -0.01m, CostRemainderLabel,
            [
                new ManufacturingProductionCostLineV2(CostRemainderLabel, OutputClass.GoodProduct, 60m, 12.3457m,
                    740.74m, null, -0.01m, "allocated-at-product-cost-per-kg", null),
                new ManufacturingProductionCostLineV2(CostSecondLabel, OutputClass.GoodProduct, 21m, 12.3457m, 259.26m,
                    null, 0m, "allocated-at-product-cost-per-kg", null),
                new ManufacturingProductionCostLineV2(CostScrapLabel, OutputClass.Scrap, 2.5m, null, null, null, 0m,
                    null, "scrap_label_cost_not_decided"),
            ],
            [new ManufacturingProductionCostCurrencyLineV2(CostIssue, CostCurrency, 0.3500m, 35.2500m, 12.3375m, 81m,
                28.35m)],
            [], ["paint_cost_not_included"], null);

    [Fact]
    public async Task No_production_cost_port_is_unavailable_with_its_reason_and_no_figure_under_the_reports_grant()
    {
        var (fixture, service) = ProductionCostCase(port: null);
        var order = CostedOrder(fixture);
        var effects = (fixture.Store.Writes.Count, fixture.Store.Commands.Count, fixture.Store.AuditEvents.Count);
        fixture.Authority.Permissions.Clear();

        var result = await service.GetProductionCostAsync(order.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, false, (string?)null), (result.Status, result.IsReplay, result.Code));
        var view = result.Resource!;
        Assert.Equal((1, order.Id, "Open", order.Version, "unavailable", "cost_source_unavailable"),
            (view.ContractVersion, view.OrderId, view.OrderStatus, view.OrderVersion, view.Status, view.ReasonCode));
        Assert.Equal(((string?)null, (string?)null, (string?)null, (string?)null, (string?)null, (Guid?)null),
            (view.CostPerKgTl, view.RawMaterialCostPerKgTl, view.InputTotalTl, view.DistributedTotalTl,
                view.RemainderTl, view.RemainderLabelId));
        Assert.Empty(view.Lines);
        Assert.Empty(view.CurrencyLines);
        Assert.Empty(view.IssueReasons);
        Assert.Empty(view.NoteCodes);
        Assert.Equal([ErpPermissions.ManufacturingReportsRead], fixture.Authority.Permissions);
        Assert.Equal((ErpPermissions.ManufacturingReportsRead, Company), fixture.Authority.LastAsked);
        Assert.Equal(effects,
            (fixture.Store.Writes.Count, fixture.Store.Commands.Count, fixture.Store.AuditEvents.Count));
    }

    [Fact]
    public async Task An_available_answer_passes_the_recorded_figures_lines_and_currency_lines_as_lossless_text()
    {
        var port = new FakeProductionCostPort();
        var (fixture, service) = ProductionCostCase(port);
        var order = CostedOrder(fixture);
        port.Answer = RecordedCost(Company, order.Id);

        var result = await service.GetProductionCostAsync(order.Id, CancellationToken.None);

        var view = result.Resource!;
        Assert.Equal(("available", "production-input-average-plus-scrap", "12.3457", "12.1", "1000", "0"),
            (view.Status, view.CostBasis, view.CostPerKgTl, view.RawMaterialCostPerKgTl, view.InputTotalTl,
                view.WeighedScrapExpenseTl));
        Assert.Equal(("1000", "-0.01", (Guid?)CostRemainderLabel, (string?)null),
            (view.DistributedTotalTl, view.RemainderTl, view.RemainderLabelId, view.ReasonCode));
        Assert.Equal(
            [
                new ProductionCostLineViewV1(CostRemainderLabel, "GoodProduct", "60", "12.3457", "740.74", null,
                    "-0.01", "allocated-at-product-cost-per-kg", null),
                new ProductionCostLineViewV1(CostSecondLabel, "GoodProduct", "21", "12.3457", "259.26", null, "0",
                    "allocated-at-product-cost-per-kg", null),
                new ProductionCostLineViewV1(CostScrapLabel, "Scrap", "2.5", null, null, null, "0", null,
                    "scrap_label_cost_not_decided"),
            ],
            view.Lines);
        Assert.Equal(
            [new ProductionCostCurrencyLineViewV1(CostIssue, CostCurrency, "0.35", "35.25", "12.3375", "81", "28.35")],
            view.CurrencyLines);
        Assert.Empty(view.IssueReasons);
        Assert.Equal(["paint_cost_not_included"], view.NoteCodes);
        Assert.Equal((Company, order.Id), Assert.Single(port.Calls));
    }

    [Fact]
    public async Task An_open_order_is_pending_with_order_not_closed_and_carries_no_figure()
    {
        var port = new FakeProductionCostPort();
        var (fixture, service) = ProductionCostCase(port);
        var order = CostedOrder(fixture);
        port.Answer = ManufacturingProductionCostResultV2.Empty(Company, order.Id,
            ManufacturingScrapCostDistributionStatusV1.Pending, "order_not_closed");

        var result = await service.GetProductionCostAsync(order.Id, CancellationToken.None);

        var view = result.Resource!;
        Assert.Equal(("Open", "pending", "order_not_closed"), (view.OrderStatus, view.Status, view.ReasonCode));
        Assert.Equal(((string?)null, (string?)null, (string?)null),
            (view.CostPerKgTl, view.InputTotalTl, view.RemainderTl));
        Assert.Empty(view.Lines);
    }

    [Fact]
    public async Task An_unreadable_answer_names_the_issues_that_keep_the_cost_from_being_computed()
    {
        var port = new FakeProductionCostPort();
        var (fixture, service) = ProductionCostCase(port);
        var order = CostedOrder(fixture);
        port.Answer = ManufacturingProductionCostResultV2.Empty(Company, order.Id,
                ManufacturingScrapCostDistributionStatusV1.Unreadable, "input_cost_zero")
            with
            {
                IssueReasons = [new ManufacturingProductionCostIssueReasonV2(CostIssue, "input_cost_zero")],
                NoteCodes = ["paint_cost_not_included"],
            };

        var result = await service.GetProductionCostAsync(order.Id, CancellationToken.None);

        var view = result.Resource!;
        Assert.Equal(("unreadable", "input_cost_zero", (string?)null), (view.Status, view.ReasonCode, view.CostPerKgTl));
        Assert.Equal([new ProductionCostIssueReasonViewV1(CostIssue, "input_cost_zero")], view.IssueReasons);
        Assert.Equal(["paint_cost_not_included"], view.NoteCodes);
    }

    [Theory]
    [InlineData(ManufacturingScrapCostDistributionStatusV1.Unavailable, "cost_source_scope_mismatch",
        "cost_source_scope_mismatch")]
    [InlineData(ManufacturingScrapCostDistributionStatusV1.NotFound, "order_not_found", "cost_answer_invalid")]
    public async Task An_unavailable_or_not_found_answer_is_unavailable_without_a_figure(
        ManufacturingScrapCostDistributionStatusV1 status, string portReason, string expectedReason)
    {
        var port = new FakeProductionCostPort();
        var (fixture, service) = ProductionCostCase(port);
        var order = CostedOrder(fixture);
        port.Answer = ManufacturingProductionCostResultV2.Empty(Company, order.Id, status, portReason);

        var result = await service.GetProductionCostAsync(order.Id, CancellationToken.None);

        var view = result.Resource!;
        Assert.Equal(("unavailable", expectedReason, (string?)null), (view.Status, view.ReasonCode, view.CostPerKgTl));
        Assert.Empty(view.Lines);
    }

    [Theory]
    [InlineData("other-company")]
    [InlineData("other-order")]
    [InlineData("other-version")]
    [InlineData("available-without-figures")]
    [InlineData("distributed-differs-from-input")]
    public async Task A_production_cost_answer_out_of_scope_or_inconsistent_is_cost_answer_invalid(string fault)
    {
        var port = new FakeProductionCostPort();
        var (fixture, service) = ProductionCostCase(port);
        var order = CostedOrder(fixture);
        port.Answer = fault switch
        {
            "other-company" => RecordedCost(OtherCompany, order.Id),
            "other-order" => RecordedCost(Company, Guid.NewGuid()),
            "other-version" => RecordedCost(Company, order.Id) with { ContractVersion = 1 },
            "available-without-figures" => RecordedCost(Company, order.Id) with { RecordedCostPerKgTl = null },
            _ => RecordedCost(Company, order.Id) with { DistributedTotalTl = 999.99m },
        };

        var result = await service.GetProductionCostAsync(order.Id, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        var view = result.Resource!;
        Assert.Equal(("unavailable", "cost_answer_invalid", (string?)null), (view.Status, view.ReasonCode, view.CostPerKgTl));
        Assert.Empty(view.Lines);
        Assert.Empty(view.CurrencyLines);
    }

    [Fact]
    public async Task A_throwing_production_cost_port_answers_cost_source_unavailable()
    {
        var port = new FakeProductionCostPort { Fault = new InvalidOperationException("sentetik kaynak hatası") };
        var (fixture, service) = ProductionCostCase(port);
        var order = CostedOrder(fixture);

        var result = await service.GetProductionCostAsync(order.Id, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        Assert.Equal(("unavailable", "cost_source_unavailable"), (result.Resource!.Status, result.Resource.ReasonCode));
    }

    [Fact]
    public async Task A_cancelled_production_cost_read_is_not_swallowed()
    {
        var port = new FakeProductionCostPort { Fault = new OperationCanceledException() };
        var (fixture, service) = ProductionCostCase(port);
        var order = CostedOrder(fixture);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetProductionCostAsync(order.Id, CancellationToken.None));
    }

    [Fact]
    public async Task The_production_cost_answers_not_found_for_an_empty_unknown_or_other_company_order()
    {
        var port = new FakeProductionCostPort();
        var (fixture, service) = ProductionCostCase(port);
        var foreign = CostedOrder(fixture);
        // The order stays the first company's; the caller's resolved company becomes another one it may access.
        fixture.CompanyContext.SwitchForTest(OtherCompany);
        fixture.User.Companies.Add(OtherCompany);

        Assert.Equal(ManufacturingApiStatus.NotFound,
            (await service.GetProductionCostAsync(Guid.Empty, CancellationToken.None)).Status);
        Assert.Equal(ManufacturingApiStatus.NotFound,
            (await service.GetProductionCostAsync(Guid.NewGuid(), CancellationToken.None)).Status);
        var other = await service.GetProductionCostAsync(foreign.Id, CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.NotFound, (ProductionCostViewV1?)null), (other.Status, other.Resource));
        // The other company's order never reaches the port.
        Assert.Empty(port.Calls);
    }

    [Fact]
    public async Task A_production_cost_read_meeting_concurrent_commits_is_a_retryable_503()
    {
        var (fixture, service) = ProductionCostCase(port: null);
        var order = CostedOrder(fixture);
        fixture.Store.ReadFault = ManufacturingStoreCodes.ConcurrentChange;

        var result = await service.GetProductionCostAsync(order.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unavailable, (string?)null, (ProductionCostViewV1?)null),
            (result.Status, result.Code, result.Resource));
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("actor")]
    [InlineData("company")]
    public async Task A_production_cost_read_rechecks_the_captured_context_and_the_reports_grant_after_the_read(
        string fault)
    {
        var port = new FakeProductionCostPort();
        var (fixture, service) = ProductionCostCase(port);
        var order = CostedOrder(fixture);
        port.Answer = RecordedCost(Company, order.Id);
        fixture.Authority.Permissions.Clear();
        fixture.Store.AfterFindOrder = _ => ApplyFreshFenceFault(fixture, fault);

        var result = await service.GetProductionCostAsync(order.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, ManufacturingApiCodes.AuthorityDenied),
            (result.Status, result.Code));
        Assert.Null(result.Resource);
        Assert.Equal([ErpPermissions.ManufacturingReportsRead], fixture.Authority.Permissions);
    }

    [Fact]
    public async Task A_production_cost_read_without_a_resolved_company_context_is_denied_before_any_read()
    {
        var port = new FakeProductionCostPort();
        var (fixture, service) = ProductionCostCase(port);
        var order = CostedOrder(fixture);
        fixture.User.Companies.Clear();
        fixture.Authority.Permissions.Clear();

        var result = await service.GetProductionCostAsync(order.Id, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, (ProductionCostViewV1?)null), (result.Status, result.Resource));
        Assert.Empty(port.Calls);
    }

    [Fact]
    public void The_production_cost_view_answers_no_port_answer_as_cost_source_unavailable()
    {
        var order = Guid.NewGuid();

        var view = ManufacturingCommandService.ProductionCostView(order, "Closed", 7, Company, null);

        Assert.Equal((order, "Closed", 7L, "unavailable", "cost_source_unavailable"),
            (view.OrderId, view.OrderStatus, view.OrderVersion, view.Status, view.ReasonCode));
    }

    /// <summary>The production cost port version 2 as a test double: one answer, or a fault thrown before answering.</summary>
    private sealed class FakeProductionCostPort : IManufacturingProductionCostPortV2
    {
        public ManufacturingProductionCostResultV2? Answer { get; set; }

        public Exception? Fault { get; init; }

        public List<(Guid CompanyId, Guid OrderId)> Calls { get; } = [];

        public Task<ManufacturingProductionCostResultV2> ReadAsync(Guid companyId, Guid orderId,
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
