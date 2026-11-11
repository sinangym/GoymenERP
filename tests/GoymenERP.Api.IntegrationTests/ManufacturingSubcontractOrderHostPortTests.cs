using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Üretim U5-b / API-36 (URT-D16, URT-D22, SAT-D175), the host adapters over fake owner ports (no database): Sales's subcontract
/// service order answer is mapped to Manufacturing's vocabulary fail-closed (never Found on a malformed or foreign answer), the
/// usable basis follows SAT-D115, and Sales's binding question is Bound when a plan binds and NotBound when none does (customer
/// goods are allocated only through a plan), Unreadable on any failure. SENTETIK / NOT_ENV.
/// </summary>
public sealed class ManufacturingSubcontractOrderHostPortTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Order = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000005");
    private static readonly Guid Line = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000005");
    private static readonly Guid Party = Guid.Parse("f1000000-0000-4000-8000-000000000005");
    private static readonly Guid Account = Guid.Parse("f2000000-0000-4000-8000-000000000005");
    private static readonly Guid Product = Guid.Parse("f3000000-0000-4000-8000-000000000005");

    private static SalesSubcontractServiceOrderV1 SalesOrder(bool isOpen = true, bool isApproved = true,
        string acceptance = SalesSubcontractCustomerAcceptanceStatesV1.Accepted,
        string basis = SalesSubcontractServiceOrderBasesV1.CurrentRevision) =>
        new(Company, Order, "SS-0001", 2, 7, "Approved", isApproved, acceptance, isOpen ? "Draft" : "Closed", isOpen, Party,
            Account, 2, basis,
            [new SalesSubcontractServiceLineV1(Line, 1, Product, "p-v1", "P-01", "Dilme hizmeti", "1000", 1000m, Guid.NewGuid(),
                "KG", "Service")]);

    private static ManufacturingSubcontractServiceOrderPort Port(FakeSales sales, Guid? company = null) =>
        new(sales, new FixedCompany(company ?? Company));

    [Fact]
    public async Task A_found_subcontract_order_maps_its_customer_state_and_basis_lines()
    {
        var sales = new FakeSalesOrders(new(SalesSubcontractServiceOrderOutcomeV1.Found, SalesOrder(isOpen: false,
            isApproved: false)));

        var answer = await Port(sales).GetAsync(Order);

        Assert.Equal(ManufacturingSubcontractServiceOrderOutcomeV1.Found, answer.Outcome);
        var order = Assert.IsType<ManufacturingSubcontractServiceOrderV1>(answer.Order);
        Assert.Equal((Company, Order, Party, Account, false, false, true), (order.CompanyId, order.SalesOrderId,
            order.CustomerPartyId, order.CustomerAccountId, order.IsOpen, order.IsApproved, order.BasisUsable));
        Assert.Equal([Line], order.BasisLineIds);
        Assert.Equal([(Company, Order)], sales.Asked);
    }

    [Theory]
    [InlineData(SalesSubcontractServiceOrderBasesV1.CurrentRevision, SalesSubcontractCustomerAcceptanceStatesV1.Accepted, true)]
    [InlineData(SalesSubcontractServiceOrderBasesV1.CurrentRevision, SalesSubcontractCustomerAcceptanceStatesV1.NotRequired, true)]
    [InlineData(SalesSubcontractServiceOrderBasesV1.CurrentRevision, SalesSubcontractCustomerAcceptanceStatesV1.Pending, false)]
    [InlineData(SalesSubcontractServiceOrderBasesV1.CurrentRevision, SalesSubcontractCustomerAcceptanceStatesV1.Rejected, false)]
    [InlineData(SalesSubcontractServiceOrderBasesV1.CurrentRevision, "accepted", false)]
    [InlineData(SalesSubcontractServiceOrderBasesV1.CustomerAcceptedRevision, SalesSubcontractCustomerAcceptanceStatesV1.Rejected, true)]
    [InlineData("Unknown", SalesSubcontractCustomerAcceptanceStatesV1.Accepted, false)]
    public void The_basis_is_usable_only_when_the_customer_stands_behind_it(string basis, string acceptance, bool usable)
    {
        Assert.Equal(usable, ManufacturingSubcontractServiceOrderPort.BasisUsable(SalesOrder(acceptance: acceptance, basis: basis)));
    }

    [Fact]
    public async Task Not_a_subcontract_order_and_not_found_pass_through_without_an_order()
    {
        var normal = await Port(new FakeSales(new(SalesSubcontractServiceOrderOutcomeV1.NotSubcontractService, null))).GetAsync(Order);
        var missing = await Port(new FakeSales(new(SalesSubcontractServiceOrderOutcomeV1.NotFound, null))).GetAsync(Order);

        Assert.Equal((ManufacturingSubcontractServiceOrderOutcomeV1.NotSubcontractService, null),
            (normal.Outcome, normal.Order));
        Assert.Equal((ManufacturingSubcontractServiceOrderOutcomeV1.NotFound, null), (missing.Outcome, missing.Order));
    }

    public static TheoryData<string> MalformedAnswers() =>
        ["unreadable", "unknown-outcome", "null-answer", "found-without-order", "another-company", "another-order",
            "empty-party", "empty-account", "empty-line", "not-found-with-order", "throws"];

    [Theory]
    [MemberData(nameof(MalformedAnswers))]
    public async Task Any_unreadable_malformed_or_foreign_answer_is_Unavailable_never_Found(string variant)
    {
        SalesSubcontractServiceOrderAnswerV1? answer = variant switch
        {
            "unreadable" => new(SalesSubcontractServiceOrderOutcomeV1.Unreadable, null,
                SalesSubcontractServiceOrderUnreadableReasonsV1.NoBasis),
            "unknown-outcome" => new((SalesSubcontractServiceOrderOutcomeV1)42, SalesOrder()),
            "null-answer" => null,
            "found-without-order" => new(SalesSubcontractServiceOrderOutcomeV1.Found, null),
            "another-company" => new(SalesSubcontractServiceOrderOutcomeV1.Found, SalesOrder() with { CompanyId = Guid.NewGuid() }),
            "another-order" => new(SalesSubcontractServiceOrderOutcomeV1.Found, SalesOrder() with { OrderId = Guid.NewGuid() }),
            "empty-party" => new(SalesSubcontractServiceOrderOutcomeV1.Found, SalesOrder() with { PartyId = Guid.Empty }),
            "empty-account" => new(SalesSubcontractServiceOrderOutcomeV1.Found, SalesOrder() with { CompanyAccountId = Guid.Empty }),
            "empty-line" => new(SalesSubcontractServiceOrderOutcomeV1.Found, SalesOrder() with
            {
                Lines = [SalesOrder().Lines[0] with { LineId = Guid.Empty }],
            }),
            "not-found-with-order" => new(SalesSubcontractServiceOrderOutcomeV1.NotFound, SalesOrder()),
            _ => null,
        };

        var result = await Port(new FakeSales(answer, throws: variant == "throws")).GetAsync(Order);

        Assert.Equal((ManufacturingSubcontractServiceOrderOutcomeV1.Unavailable, null), (result.Outcome, result.Order));
    }

    [Fact]
    public async Task Without_a_resolved_company_or_with_an_empty_order_sales_is_not_asked()
    {
        var sales = new FakeSales(new(SalesSubcontractServiceOrderOutcomeV1.Found, SalesOrder()));

        var noCompany = await Port(sales, Guid.Empty).GetAsync(Order);
        var emptyOrder = await Port(sales).GetAsync(Guid.Empty);

        Assert.Equal(ManufacturingSubcontractServiceOrderOutcomeV1.Unavailable, noCompany.Outcome);
        Assert.Equal(ManufacturingSubcontractServiceOrderOutcomeV1.Unavailable, emptyOrder.Outcome);
        Assert.Empty(sales.Asked);
    }

    [Fact]
    public async Task A_cancellation_is_not_swallowed()
    {
        var sales = new FakeSales(null, throws: true, cancel: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Port(sales).GetAsync(Order));
    }

    [Fact]
    public async Task The_binding_is_Bound_when_a_plan_binds_and_NotBound_when_none_does()
    {
        var bound = new ManufacturingSalesSubcontractOrderBindingReadPort(new FakeBinding(true), new FixedCompany(Company));
        var unbound = new FakeBinding(false);
        var notBound = new ManufacturingSalesSubcontractOrderBindingReadPort(unbound, new FixedCompany(Company));

        Assert.Equal(SalesSubcontractOrderBindingOutcomeV1.Bound, (await bound.GetAsync(Company, Order)).Outcome);
        Assert.Equal(SalesSubcontractOrderBindingOutcomeV1.NotBound, (await notBound.GetAsync(Company, Order)).Outcome);
        Assert.Equal([(Company, Order)], unbound.Asked);
    }

    [Fact]
    public async Task The_binding_of_another_company_an_empty_id_or_a_failing_read_is_Unreadable()
    {
        var binding = new FakeBinding(true);
        var port = new ManufacturingSalesSubcontractOrderBindingReadPort(binding, new FixedCompany(Company));
        var throwing = new ManufacturingSalesSubcontractOrderBindingReadPort(new FakeBinding(true, throws: true),
            new FixedCompany(Company));
        var unresolved = new ManufacturingSalesSubcontractOrderBindingReadPort(binding, new FixedCompany(Guid.Empty));

        Assert.Equal(SalesSubcontractOrderBindingOutcomeV1.Unreadable, (await port.GetAsync(Guid.NewGuid(), Order)).Outcome);
        Assert.Equal(SalesSubcontractOrderBindingOutcomeV1.Unreadable, (await port.GetAsync(Company, Guid.Empty)).Outcome);
        Assert.Equal(SalesSubcontractOrderBindingOutcomeV1.Unreadable, (await port.GetAsync(Guid.Empty, Order)).Outcome);
        Assert.Equal(SalesSubcontractOrderBindingOutcomeV1.Unreadable, (await unresolved.GetAsync(Company, Order)).Outcome);
        Assert.Equal(SalesSubcontractOrderBindingOutcomeV1.Unreadable, (await throwing.GetAsync(Company, Order)).Outcome);
        Assert.Empty(binding.Asked);
    }

    private class FakeSales(SalesSubcontractServiceOrderAnswerV1? answer, bool throws = false, bool cancel = false)
        : ISalesSubcontractServiceOrderReadPortV1
    {
        public List<(Guid Company, Guid Order)> Asked { get; } = [];

        public Task<SalesSubcontractServiceOrderAnswerV1> GetAsync(Guid companyId, Guid orderId,
            CancellationToken cancellationToken = default)
        {
            Asked.Add((companyId, orderId));
            if (cancel)
            {
                throw new OperationCanceledException();
            }

            return throws ? throw new InvalidOperationException("Satış deposu okunamadı.") : Task.FromResult(answer!);
        }
    }

    private sealed class FakeSalesOrders(SalesSubcontractServiceOrderAnswerV1 answer) : FakeSales(answer);

    private sealed class FakeBinding(bool bound, bool throws = false) : IManufacturingSalesOrderBindingReadPortV1
    {
        public List<(Guid Company, Guid Order)> Asked { get; } = [];

        public Task<bool> HasBindingPlanAsync(Guid companyId, Guid salesOrderId, CancellationToken cancellationToken = default)
        {
            Asked.Add((companyId, salesOrderId));
            return throws ? throw new InvalidOperationException("Üretim okunamadı.") : Task.FromResult(bound);
        }
    }

    private sealed class FixedCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => companyId != Guid.Empty;
        public Guid CompanyId => companyId;
        public void Set(Guid value) => throw new NotSupportedException();
    }
}
