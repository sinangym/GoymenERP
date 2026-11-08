using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// A4 / MOD-03 (SAT-D219), the Purchasing side without a database: the composer's one rule of "open" over stored rows (a general
/// line and its technical slices are never counted twice, a fully accepted or cancelled commitment is closed, a row that cannot be
/// answered throws) and the port's refusals before any connection (no resolved company, another company, a bad query). The query on
/// real rows (Approved orders only, the Sales source index, this company only) is proven on SQL Server in CI. SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseSalesLinkedSupplyReadPortTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Order = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid SalesLine = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid Unit = Guid.Parse("cccccccc-0000-4000-8000-000000000001");

    private static PurchaseSalesLinkedSupplyRow General(Guid? lineId = null, decimal ordered = 100m, decimal technical = 0m,
        decimal accepted = 0m, decimal cancelled = 0m, int lineNo = 1, Guid? company = null, Guid? salesLine = null) =>
        new(company ?? Company, Order, lineId ?? Guid.NewGuid(), lineNo, (int)PurchaseOrderLineKind.General, null, salesLine ?? SalesLine, Unit,
            true, ordered, technical, accepted, cancelled);

    private static PurchaseSalesLinkedSupplyRow Slice(Guid parent, decimal ordered, decimal accepted = 0m, decimal cancelled = 0m) =>
        new(Company, Order, Guid.NewGuid(), 2, (int)PurchaseOrderLineKind.Technical, parent, SalesLine, Unit, true, ordered, 0m, accepted, cancelled);

    [Fact]
    public void No_rows_is_an_empty_answer()
    {
        Assert.Empty(PurchaseSalesLinkedSupplyComposer.Compose([], Company));
    }

    [Fact]
    public void An_open_general_line_is_listed_with_its_exact_remaining_text()
    {
        var line = General(ordered: 100m, accepted: 25.5m);
        var answer = Assert.Single(PurchaseSalesLinkedSupplyComposer.Compose([line], Company));

        Assert.Equal("74.5", answer.OpenQuantityText);
        Assert.Equal(line.LineId, answer.LineId);
        Assert.Equal(Order, answer.PurchaseOrderId);
        Assert.Equal(SalesLine, answer.SalesOrderLineId);
        Assert.Equal(Unit, answer.UnitId);
        Assert.Equal("Approved", answer.Status);
    }

    [Fact]
    public void A_general_line_and_its_technical_breakdown_are_one_commitment_never_summed_twice()
    {
        // 100 ordered, 40 carved into two slices (one 30 slice with 10 accepted, one 10 slice untouched), 20 accepted on the general share.
        var general = General(ordered: 100m, technical: 40m, accepted: 20m);
        var answer = Assert.Single(PurchaseSalesLinkedSupplyComposer.Compose(
            [Slice(general.LineId, 30m, accepted: 10m), general, Slice(general.LineId, 10m)], Company));

        // (100 - 40 - 20) + (30 - 10) + 10 = 70 = ordered 100 - accepted 30; not 100 + 40 + ... and one item, not three.
        Assert.Equal("70", answer.OpenQuantityText);
        Assert.Equal(general.LineId, answer.LineId);
    }

    [Fact]
    public void An_open_slice_keeps_the_commitment_open_even_when_the_general_share_is_done()
    {
        var general = General(ordered: 100m, technical: 40m, accepted: 60m);
        var answer = Assert.Single(PurchaseSalesLinkedSupplyComposer.Compose([general, Slice(general.LineId, 40m)], Company));

        Assert.Equal("40", answer.OpenQuantityText);
    }

    [Fact]
    public void A_fully_accepted_or_cancelled_commitment_is_closed_and_not_listed()
    {
        var accepted = General(ordered: 10m, accepted: 10m);
        var cancelled = General(ordered: 10m, accepted: 4m, cancelled: 6m);
        var sliced = General(ordered: 10m, technical: 4m, accepted: 6m);
        var sliceDone = Slice(sliced.LineId, 4m, accepted: 4m);

        Assert.Empty(PurchaseSalesLinkedSupplyComposer.Compose([accepted, cancelled, sliced, sliceDone], Company));
    }

    [Fact]
    public void Several_commitments_are_listed_by_order_then_line_number()
    {
        var second = General(lineNo: 2, salesLine: Guid.NewGuid());
        var first = General(lineNo: 1);

        var lines = PurchaseSalesLinkedSupplyComposer.Compose([second, first], Company);

        Assert.Equal([first.LineId, second.LineId], lines.Select(line => line.LineId));
    }

    [Fact]
    public void A_piece_unit_quantity_is_exact_whole_text()
    {
        var piece = General() with { UnitIsKilogram = false, Ordered = 12m };
        Assert.Equal("12", Assert.Single(PurchaseSalesLinkedSupplyComposer.Compose([piece], Company)).OpenQuantityText);
    }

    [Fact]
    public void Another_companys_row_a_missing_sales_line_an_orphan_slice_or_a_negative_remainder_cannot_be_answered()
    {
        Assert.Throws<InvalidOperationException>(() => PurchaseSalesLinkedSupplyComposer.Compose([General(company: OtherCompany)], Company));
        Assert.Throws<InvalidOperationException>(() => PurchaseSalesLinkedSupplyComposer.Compose([General() with { SalesOrderLineId = null }], Company));
        Assert.Throws<InvalidOperationException>(() => PurchaseSalesLinkedSupplyComposer.Compose([Slice(Guid.NewGuid(), 5m)], Company));
        Assert.Throws<InvalidOperationException>(() => PurchaseSalesLinkedSupplyComposer.Compose([General(ordered: 10m, accepted: 11m)], Company));
        Assert.Throws<InvalidOperationException>(() => PurchaseSalesLinkedSupplyComposer.Compose([General() with { Kind = 99 }], Company));
    }

    [Fact]
    public async Task Without_a_resolved_company_or_for_another_company_the_port_refuses_before_any_connection()
    {
        await using (var context = Context(resolved: false))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PurchaseSalesLinkedSupplyReadPort(context, new CompanyContext()).ListOpenForSalesOrderAsync(Company, Order, null));
        }

        var company = new CompanyContext();
        company.Set(Company);
        await using var scoped = Context(resolved: true);
        var port = new PurchaseSalesLinkedSupplyReadPort(scoped, company);
        await Assert.ThrowsAsync<InvalidOperationException>(() => port.ListOpenForSalesOrderAsync(OtherCompany, Order, null));
    }

    [Fact]
    public async Task An_empty_order_or_line_id_and_an_oversized_filter_are_refused_before_any_connection()
    {
        var company = new CompanyContext();
        company.Set(Company);
        await using var context = Context(resolved: true);
        var port = new PurchaseSalesLinkedSupplyReadPort(context, company);

        await Assert.ThrowsAsync<ArgumentException>(() => port.ListOpenForSalesOrderAsync(Company, Guid.Empty, null));
        await Assert.ThrowsAsync<ArgumentException>(() => port.ListOpenForSalesOrderAsync(Company, Order, [Guid.Empty]));
        await Assert.ThrowsAsync<ArgumentException>(() => port.ListOpenForSalesOrderAsync(Company, Order,
            [.. Enumerable.Range(0, PurchaseSalesLinkedSupplyReadPort.MaximumLineFilter + 1).Select(_ => Guid.NewGuid())]));
    }

    private static PurchasingDbContext Context(bool resolved)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(Company);
        }

        return new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }
}
