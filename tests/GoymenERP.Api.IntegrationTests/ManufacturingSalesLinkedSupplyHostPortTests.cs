using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Üretim U2-b / API-16 (SAT-D219), the host adapters over fake owner ports (no database): the Production adapter of Sales's
/// linked-supply read answers None / Present / Unreadable (never None on a failure, never a double count), declares itself as
/// the Production recorder, and the approved-line reader maps Sales's answer fail-closed. SENTETIK / NOT_ENV.
/// </summary>
public sealed class ManufacturingSalesLinkedSupplyHostPortTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Order = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");
    private static readonly Guid SalesLine = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly Guid Plan = Guid.Parse("dddddddd-0000-4000-8000-000000000002");
    private static readonly Guid ProductionOrder = Guid.Parse("eeeeeeee-0000-4000-8000-000000000002");
    private static readonly Guid Party = Guid.Parse("f1000000-0000-4000-8000-000000000002");
    private static readonly Guid Account = Guid.Parse("f2000000-0000-4000-8000-000000000002");
    private static readonly Guid Product = Guid.Parse("f3000000-0000-4000-8000-000000000002");

    private static SalesOrderLinkedSupplyQueryV1 Query(IReadOnlyList<Guid>? lines = null) => new(Company, Order, lines);

    private static ManufacturingSalesOrderSupplyItemV1 Item(Guid? plan = null, Guid? order = null, string? orderStatus = null,
        Guid? company = null, decimal plannedKg = 15_000m, string planStatus = "Draft", Guid? line = null) =>
        new(company ?? Company, plan ?? Plan, planStatus, order, orderStatus, line ?? SalesLine, plannedKg, null);

    [Fact]
    public void The_adapter_answers_for_the_production_kind_and_the_recorder_declares_the_same_kind()
    {
        Assert.Equal(SalesLinkedSupplyKindsV1.Production, new ManufacturingSalesOrderLinkedSupplyReadPort(new FakeRead([])).SupplyKind);
        Assert.Equal(SalesLinkedSupplyKindsV1.Production, new ManufacturingSalesOrderLinkedSupplyRecorder().SupplyKind);
    }

    [Fact]
    public async Task No_open_plan_or_order_is_None_with_no_item()
    {
        var answer = await new ManufacturingSalesOrderLinkedSupplyReadPort(new FakeRead([])).ReadOrderLinkedSupplyAsync(Query());

        Assert.Equal(SalesCancellationImpactStatusesV1.None, answer.Status);
        Assert.Empty(answer.OpenItems);
        Assert.Null(answer.ReasonCode);
        Assert.Equal((Company, Order, SalesLinkedSupplyKindsV1.Production), (answer.CompanyId, answer.OrderId, answer.SupplyKind));
    }

    [Fact]
    public async Task An_open_plan_is_Present_as_the_plan_and_a_plan_with_an_open_order_as_the_order()
    {
        var otherPlan = Guid.Parse("dddddddd-0000-4000-8000-000000000003");
        var answer = await new ManufacturingSalesOrderLinkedSupplyReadPort(new FakeRead([
                Item(plannedKg: 15_000.5m),
                Item(plan: otherPlan, order: ProductionOrder, orderStatus: "Open", planStatus: "Approved"),
            ]))
            .ReadOrderLinkedSupplyAsync(Query([SalesLine]));

        Assert.Equal(SalesCancellationImpactStatusesV1.Present, answer.Status);
        Assert.Null(answer.ReasonCode);
        Assert.Equal(2, answer.OpenItems.Count);

        var plan = answer.OpenItems[0];
        Assert.Equal(Plan, plan.SupplyId);
        Assert.Null(plan.SupplyLineId);
        Assert.Equal(SalesLinkedSupplyDocumentTypesV1.ProductionPlan, plan.DocumentType);
        Assert.Null(plan.DocumentNumber);
        Assert.Equal(SalesLine, plan.OrderLineId);
        Assert.Equal("Draft", plan.Status);
        Assert.Null(plan.OpenQuantity);
        Assert.Null(plan.OpenQuantityUnitId);
        Assert.Equal("15000.5", plan.PlannedKg);
        Assert.Null(plan.ActualKg);

        var order = answer.OpenItems[1];
        Assert.Equal(ProductionOrder, order.SupplyId);
        Assert.Equal(SalesLinkedSupplyDocumentTypesV1.ProductionOrder, order.DocumentType);
        Assert.Equal("Open", order.Status);
        Assert.Equal("15000", order.PlannedKg);
    }

    [Fact]
    public async Task A_failing_owner_is_Unreadable_with_a_reason_never_None()
    {
        var throwing = await new ManufacturingSalesOrderLinkedSupplyReadPort(new FakeRead(new InvalidOperationException("db down")))
            .ReadOrderLinkedSupplyAsync(Query());

        Assert.Equal(SalesCancellationImpactStatusesV1.Unreadable, throwing.Status);
        Assert.Equal(ManufacturingSalesOrderLinkedSupplyReadPort.UnreadableReason, throwing.ReasonCode);
        Assert.Empty(throwing.OpenItems);
    }

    [Fact]
    public async Task A_malformed_answer_is_Unreadable_another_company_a_duplicate_a_half_order_a_negative_mass_or_an_unrequested_line()
    {
        Guid[] narrowed = [Guid.NewGuid()];
        var cases = new (IReadOnlyList<ManufacturingSalesOrderSupplyItemV1>? Items, IReadOnlyList<Guid>? Narrowing)[]
        {
            (null, null),
            ([Item(company: Guid.NewGuid())], null),
            ([Item(), Item()], null),
            ([Item(order: ProductionOrder)], null),
            ([Item(orderStatus: "Open")], null),
            ([Item(order: Guid.Empty, orderStatus: "Open")], null),
            ([Item(plan: Guid.Empty)], null),
            ([Item(line: Guid.Empty)], null),
            ([Item(planStatus: " ")], null),
            ([Item(plannedKg: -1m)], null),
            ([Item()], narrowed),
        };
        foreach (var (items, narrowing) in cases)
        {
            var answer = await new ManufacturingSalesOrderLinkedSupplyReadPort(new FakeRead(items)).ReadOrderLinkedSupplyAsync(Query(narrowing));
            Assert.Equal(SalesCancellationImpactStatusesV1.Unreadable, answer.Status);
            Assert.Equal(ManufacturingSalesOrderLinkedSupplyReadPort.UnreadableReason, answer.ReasonCode);
            Assert.Empty(answer.OpenItems);
        }
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed_by_either_adapter()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new ManufacturingSalesOrderLinkedSupplyReadPort(new FakeRead(new OperationCanceledException())).ReadOrderLinkedSupplyAsync(Query()));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new ManufacturingSalesOrderLineReferencePort(new FakeSales(new OperationCanceledException()), new FixedCompany(Company))
                .GetApprovedLineAsync(Order, SalesLine));
    }

    [Fact]
    public async Task The_order_company_and_lines_of_the_query_reach_the_manufacturing_port_unchanged()
    {
        var read = new FakeRead([]);
        await new ManufacturingSalesOrderLinkedSupplyReadPort(read).ReadOrderLinkedSupplyAsync(Query([SalesLine]));

        Assert.Equal((Company, Order), read.Seen);
        Assert.Equal([SalesLine], read.SeenLines);
    }

    [Fact]
    public async Task An_approved_line_is_read_with_its_customer_account_product_and_revision()
    {
        var answer = await Read(Approved(Order, SalesLine));

        Assert.Equal(ManufacturingSalesOrderLineOutcomeV1.Approved, answer.Outcome);
        Assert.Equal(new ManufacturingSalesOrderLineV1(Company, Order, SalesLine, Party, Account, Product, 3), answer.Line);
    }

    [Fact]
    public async Task Only_that_order_and_line_of_the_resolved_company_is_Approved_and_everything_else_fails_closed()
    {
        Assert.Equal(ManufacturingSalesOrderLineOutcomeV1.NotApproved,
            (await Read(new(SalesOrderLineReferenceOutcomeV1.NotApproved, null, "PendingApproval"))).Outcome);
        Assert.Equal(ManufacturingSalesOrderLineOutcomeV1.NotFound,
            (await Read(new(SalesOrderLineReferenceOutcomeV1.NotFound, null, null))).Outcome);

        SalesOrderLineReferenceAnswerV1[] unavailable =
        [
            new(SalesOrderLineReferenceOutcomeV1.Approved, null, null),
            Approved(Guid.NewGuid(), SalesLine),
            Approved(Order, Guid.NewGuid()),
            Approved(Order, SalesLine, company: Guid.NewGuid()),
            Approved(Order, SalesLine, party: Guid.Empty),
            Approved(Order, SalesLine, account: Guid.Empty),
            Approved(Order, SalesLine, product: Guid.Empty),
            Approved(Order, SalesLine, revision: -1),
            new((SalesOrderLineReferenceOutcomeV1)99, null, null),
        ];
        foreach (var sales in unavailable)
        {
            var answer = await Read(sales);
            Assert.Equal(ManufacturingSalesOrderLineOutcomeV1.Unavailable, answer.Outcome);
            Assert.Null(answer.Line);
        }

        Assert.Equal(ManufacturingSalesOrderLineOutcomeV1.Unavailable,
            (await new ManufacturingSalesOrderLineReferencePort(new FakeSales(Approved(Order, SalesLine)), new FixedCompany(Guid.Empty))
                .GetApprovedLineAsync(Order, SalesLine)).Outcome);
        Assert.Equal(ManufacturingSalesOrderLineOutcomeV1.Unavailable,
            (await new ManufacturingSalesOrderLineReferencePort(new FakeSales(new InvalidOperationException("off")), new FixedCompany(Company))
                .GetApprovedLineAsync(Order, SalesLine)).Outcome);
    }

    private static Task<ManufacturingSalesOrderLineAnswerV1> Read(SalesOrderLineReferenceAnswerV1 answer) =>
        new ManufacturingSalesOrderLineReferencePort(new FakeSales(answer), new FixedCompany(Company)).GetApprovedLineAsync(Order, SalesLine);

    private static SalesOrderLineReferenceAnswerV1 Approved(Guid order, Guid line, Guid? company = null, Guid? party = null,
        Guid? account = null, Guid? product = null, int revision = 3) =>
        new(SalesOrderLineReferenceOutcomeV1.Approved,
            new SalesOrderLineReferenceV1(company ?? Company, order, line, 1, party ?? Party, account ?? Account, product ?? Product,
                "1", Guid.NewGuid(), "10", revision, 1), null);

    private sealed class FakeRead : IManufacturingSalesOrderSupplyReadPortV1
    {
        private readonly IReadOnlyList<ManufacturingSalesOrderSupplyItemV1>? items;
        private readonly Exception? failure;

        public FakeRead(IReadOnlyList<ManufacturingSalesOrderSupplyItemV1>? items) => this.items = items;

        public FakeRead(Exception failure) => this.failure = failure;

        public (Guid Company, Guid Order)? Seen { get; private set; }

        public IReadOnlyList<Guid>? SeenLines { get; private set; }

        public Task<IReadOnlyList<ManufacturingSalesOrderSupplyItemV1>> ListOpenForSalesOrderAsync(Guid companyId,
            Guid salesOrderId, IReadOnlyList<Guid>? salesOrderLineIds, CancellationToken cancellationToken = default)
        {
            Seen = (companyId, salesOrderId);
            SeenLines = salesOrderLineIds;
            return failure is null
                ? Task.FromResult(items!)
                : Task.FromException<IReadOnlyList<ManufacturingSalesOrderSupplyItemV1>>(failure);
        }
    }

    private sealed class FixedCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => companyId != Guid.Empty;
        public Guid CompanyId => companyId;
        public void Set(Guid value) => throw new NotSupportedException();
    }

    private sealed class FakeSales : ISalesOrderLineReferencePortV1
    {
        private readonly SalesOrderLineReferenceAnswerV1? answer;
        private readonly Exception? failure;

        public FakeSales(SalesOrderLineReferenceAnswerV1 answer) => this.answer = answer;

        public FakeSales(Exception failure) => this.failure = failure;

        public Task<SalesOrderLineReferenceAnswerV1> GetApprovedLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default) =>
            failure is null ? Task.FromResult(answer!) : Task.FromException<SalesOrderLineReferenceAnswerV1>(failure);

        public Task<SalesApprovedLinePageV1> ListApprovedLinesAsync(int pageSize, string? cursor, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
