using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// A4 / MOD-03 (SAT-D219 / SAT-D220), the host adapters over fake owner ports (no database): the Purchasing adapter of Sales's
/// linked-supply read answers None / Present / Unreadable (never None on a failure, never a double count), declares itself as
/// the Purchasing recorder, and the SAT-D220 reader maps Sales's approved-line answer fail-closed. SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchasingSalesLinkedSupplyHostPortTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Order = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid SalesLine = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid Unit = Guid.Parse("cccccccc-0000-4000-8000-000000000001");
    private static readonly Guid PurchaseOrder = Guid.Parse("dddddddd-0000-4000-8000-000000000001");
    private static readonly Guid PurchaseLine = Guid.Parse("eeeeeeee-0000-4000-8000-000000000001");

    private static SalesOrderLinkedSupplyQueryV1 Query(IReadOnlyList<Guid>? lines = null) => new(Company, Order, lines);

    private static PurchaseSalesLinkedSupplyLineV1 Line(Guid? purchaseLine = null, string quantity = "74.5", Guid? company = null) =>
        new(company ?? Company, PurchaseOrder, purchaseLine ?? PurchaseLine, SalesLine, "Approved", quantity, Unit);

    [Fact]
    public void The_adapter_answers_for_the_purchasing_kind_and_the_recorder_declares_the_same_kind()
    {
        Assert.Equal(SalesLinkedSupplyKindsV1.Purchasing, new PurchasingSalesOrderLinkedSupplyReadPort(new FakeRead([])).SupplyKind);
        Assert.Equal(SalesLinkedSupplyKindsV1.Purchasing, new PurchasingSalesOrderLinkedSupplyRecorder().SupplyKind);
    }

    [Fact]
    public async Task No_open_line_is_None_with_no_item()
    {
        var answer = await new PurchasingSalesOrderLinkedSupplyReadPort(new FakeRead([])).ReadOrderLinkedSupplyAsync(Query());

        Assert.Equal(SalesCancellationImpactStatusesV1.None, answer.Status);
        Assert.Empty(answer.OpenItems);
        Assert.Null(answer.ReasonCode);
        Assert.Equal((Company, Order, SalesLinkedSupplyKindsV1.Purchasing), (answer.CompanyId, answer.OrderId, answer.SupplyKind));
    }

    [Fact]
    public async Task An_open_line_is_Present_as_the_purchase_order_and_its_line_with_the_exact_open_quantity()
    {
        var answer = await new PurchasingSalesOrderLinkedSupplyReadPort(new FakeRead([Line()])).ReadOrderLinkedSupplyAsync(Query([SalesLine]));

        Assert.Equal(SalesCancellationImpactStatusesV1.Present, answer.Status);
        var item = Assert.Single(answer.OpenItems);
        Assert.Equal(PurchaseOrder, item.SupplyId);
        Assert.Equal(PurchaseLine, item.SupplyLineId);
        Assert.Equal(SalesLinkedSupplyDocumentTypesV1.PurchaseOrder, item.DocumentType);
        Assert.Null(item.DocumentNumber);
        Assert.Equal(SalesLine, item.OrderLineId);
        Assert.Equal("Approved", item.Status);
        Assert.Equal("74.5", item.OpenQuantity);
        Assert.Equal(Unit, item.OpenQuantityUnitId);
        Assert.Null(item.PlannedKg);
        Assert.Null(item.ActualKg);
        Assert.Null(answer.ReasonCode);
    }

    [Fact]
    public async Task A_failing_or_closed_owner_is_Unreadable_with_a_reason_never_None()
    {
        var throwing = await new PurchasingSalesOrderLinkedSupplyReadPort(new FakeRead(new InvalidOperationException("db down")))
            .ReadOrderLinkedSupplyAsync(Query());

        Assert.Equal(SalesCancellationImpactStatusesV1.Unreadable, throwing.Status);
        Assert.Equal(PurchasingSalesOrderLinkedSupplyReadPort.UnreadableReason, throwing.ReasonCode);
        Assert.Empty(throwing.OpenItems);
    }

    [Fact]
    public async Task A_malformed_answer_is_Unreadable_another_company_a_duplicate_a_bad_quantity_or_an_unrequested_line()
    {
        Guid[] narrowed = [Guid.NewGuid()];
        var cases = new (IReadOnlyList<PurchaseSalesLinkedSupplyLineV1> Lines, IReadOnlyList<Guid>? Narrowing)[]
        {
            ([Line(company: Guid.NewGuid())], null),
            ([Line(), Line()], null),
            ([Line(quantity: "0")], null),
            ([Line(quantity: "abc")], null),
            ([Line(quantity: "")], null),
            ([Line()], narrowed),
        };
        foreach (var (lines, narrowing) in cases)
        {
            var answer = await new PurchasingSalesOrderLinkedSupplyReadPort(new FakeRead(lines)).ReadOrderLinkedSupplyAsync(Query(narrowing));
            Assert.Equal(SalesCancellationImpactStatusesV1.Unreadable, answer.Status);
            Assert.Empty(answer.OpenItems);
        }
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new PurchasingSalesOrderLinkedSupplyReadPort(new FakeRead(new OperationCanceledException())).ReadOrderLinkedSupplyAsync(Query()));
    }

    [Fact]
    public async Task The_order_and_company_of_the_query_reach_the_purchasing_port_unchanged()
    {
        var read = new FakeRead([]);
        await new PurchasingSalesOrderLinkedSupplyReadPort(read).ReadOrderLinkedSupplyAsync(Query([SalesLine]));

        Assert.Equal((Company, Order), read.Seen);
        Assert.Equal([SalesLine], read.SeenLines);
    }

    [Fact]
    public async Task A_sales_source_is_accepted_only_when_the_approved_line_is_that_order_and_line()
    {
        Assert.Equal(PurchaseRequestSalesSourceOutcomeV1.Approved, await Check(Approved(Order, SalesLine)));
        Assert.Equal(PurchaseRequestSalesSourceOutcomeV1.NotApproved,
            await Check(new(SalesOrderLineReferenceOutcomeV1.NotApproved, null, "PendingApproval")));
        Assert.Equal(PurchaseRequestSalesSourceOutcomeV1.NotFound, await Check(new(SalesOrderLineReferenceOutcomeV1.NotFound, null, null)));
        // Fail-closed: an approved answer without a line, or for another order or line, is never Approved.
        Assert.Equal(PurchaseRequestSalesSourceOutcomeV1.Unavailable, await Check(new(SalesOrderLineReferenceOutcomeV1.Approved, null, null)));
        Assert.Equal(PurchaseRequestSalesSourceOutcomeV1.Unavailable, await Check(Approved(Guid.NewGuid(), SalesLine)));
        Assert.Equal(PurchaseRequestSalesSourceOutcomeV1.Unavailable, await Check(Approved(Order, Guid.NewGuid())));
        Assert.Equal(PurchaseRequestSalesSourceOutcomeV1.Unavailable, await Check(new((SalesOrderLineReferenceOutcomeV1)99, null, null)));
    }

    [Fact]
    public async Task A_failing_sales_port_is_Unavailable_and_the_stable_codes_are_pinned()
    {
        var failing = new PurchasingRequestSalesSourcePort(new FakeSales(new InvalidOperationException("off")));
        Assert.Equal(PurchaseRequestSalesSourceOutcomeV1.Unavailable, await failing.CheckSalesOrderLineAsync(Order, SalesLine));

        Assert.Null(PurchaseRequestSalesSourceCodesV1.CodeOf(PurchaseRequestSalesSourceOutcomeV1.Approved));
        Assert.Equal("purchasing_request_source_sales_not_approved", PurchaseRequestSalesSourceCodesV1.CodeOf(PurchaseRequestSalesSourceOutcomeV1.NotApproved));
        Assert.Equal("purchasing_request_source_sales_not_found", PurchaseRequestSalesSourceCodesV1.CodeOf(PurchaseRequestSalesSourceOutcomeV1.NotFound));
        Assert.Equal("purchasing_request_source_sales_unavailable", PurchaseRequestSalesSourceCodesV1.CodeOf(PurchaseRequestSalesSourceOutcomeV1.Unavailable));
    }

    private static Task<PurchaseRequestSalesSourceOutcomeV1> Check(SalesOrderLineReferenceAnswerV1 answer) =>
        new PurchasingRequestSalesSourcePort(new FakeSales(answer)).CheckSalesOrderLineAsync(Order, SalesLine);

    private static SalesOrderLineReferenceAnswerV1 Approved(Guid order, Guid line) => new(SalesOrderLineReferenceOutcomeV1.Approved,
        new SalesOrderLineReferenceV1(Company, order, line, 1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1", Unit, "10", 1, 1), null);

    private sealed class FakeRead : IPurchaseSalesLinkedSupplyReadPortV1
    {
        private readonly IReadOnlyList<PurchaseSalesLinkedSupplyLineV1>? lines;
        private readonly Exception? failure;

        public FakeRead(IReadOnlyList<PurchaseSalesLinkedSupplyLineV1> lines) => this.lines = lines;

        public FakeRead(Exception failure) => this.failure = failure;

        public (Guid Company, Guid Order)? Seen { get; private set; }

        public IReadOnlyList<Guid>? SeenLines { get; private set; }

        public Task<IReadOnlyList<PurchaseSalesLinkedSupplyLineV1>> ListOpenForSalesOrderAsync(
            Guid companyId, Guid salesOrderId, IReadOnlyList<Guid>? salesOrderLineIds, CancellationToken cancellationToken = default)
        {
            Seen = (companyId, salesOrderId);
            SeenLines = salesOrderLineIds;
            return failure is null ? Task.FromResult(lines!) : Task.FromException<IReadOnlyList<PurchaseSalesLinkedSupplyLineV1>>(failure);
        }
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
