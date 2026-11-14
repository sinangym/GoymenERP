using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// F-017 F17-1 (ALIS_F017 v1 §3.1, STOK_F017 §10 F017-a), the Purchasing side without a database: the future-supply read port
/// answers NotFound / an empty page without a server-resolved company or without an identity, refuses a page size or cursor out of
/// shape BEFORE any connection is opened (the connection string points nowhere, so reaching the database would be a provider
/// exception, not these answers); its cursor is bound to the company AND the product that read it; the contract carries no price
/// member and its fixed values are pinned. What it answers on real rows — approved general lines only, technical slices folded,
/// another company NotFound, keyset paging — is proven on SQL Server (<c>PurchasingSqlServerRelationalAcceptanceTests.FutureSupply</c>).
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseFutureSupplyReadPortTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid ProductA = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid ProductB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly Guid OrderA = Guid.Parse("cccccccc-0000-4000-8000-000000000001");
    private static readonly Guid LineA = Guid.Parse("dddddddd-0000-4000-8000-000000000001");

    [Fact]
    public async Task Without_a_resolved_company_the_line_is_not_found_and_the_list_is_empty_without_any_connection()
    {
        var (context, port) = Port(resolved: false);
        await using (context)
        {
            var answer = await port.GetAllocatableLineAsync(OrderA, LineA);
            Assert.Equal(PurchaseFutureSupplyOutcomeV1.NotFound, answer.Outcome);
            Assert.Null(answer.Line);
            Assert.Null(answer.Status);
            Assert.Null(answer.ParentLineId);

            var page = await port.ListAllocatableLinesAsync(ProductA, 10, null);
            Assert.Empty(page.Items);
            Assert.Null(page.NextCursor);
        }
    }

    [Fact]
    public async Task An_empty_order_line_or_product_identity_is_not_found_or_empty_without_any_connection()
    {
        var (context, port) = Port(resolved: true);
        await using (context)
        {
            Assert.Equal(PurchaseFutureSupplyOutcomeV1.NotFound, (await port.GetAllocatableLineAsync(Guid.Empty, LineA)).Outcome);
            Assert.Equal(PurchaseFutureSupplyOutcomeV1.NotFound, (await port.GetAllocatableLineAsync(OrderA, Guid.Empty)).Outcome);

            var page = await port.ListAllocatableLinesAsync(Guid.Empty, 10, null);
            Assert.Empty(page.Items);
            Assert.Null(page.NextCursor);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task A_page_size_out_of_range_is_refused_before_any_connection_even_without_a_company(int pageSize)
    {
        foreach (var resolved in new[] { true, false })
        {
            var (context, port) = Port(resolved);
            await using (context)
            {
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => port.ListAllocatableLinesAsync(ProductA, pageSize, null));
            }
        }
    }

    [Fact]
    public async Task Another_companys_or_another_products_cursor_and_a_malformed_cursor_are_argument_errors_before_any_connection()
    {
        var at = new DateOnly(2026, 11, 12);
        var foreignCompany = PurchaseFutureSupplyReadPort.Cursor.Encode(CompanyB, ProductA, at, OrderA, 1);
        var foreignProduct = PurchaseFutureSupplyReadPort.Cursor.Encode(CompanyA, ProductB, at, OrderA, 1);
        var (context, port) = Port(resolved: true);
        await using (context)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => port.ListAllocatableLinesAsync(ProductA, 10, foreignCompany));
            await Assert.ThrowsAsync<ArgumentException>(() => port.ListAllocatableLinesAsync(ProductA, 10, foreignProduct));
            await Assert.ThrowsAsync<ArgumentException>(() => port.ListAllocatableLinesAsync(ProductA, 10, "not a cursor!"));
            await Assert.ThrowsAsync<ArgumentException>(() => port.ListAllocatableLinesAsync(ProductA, 10, "AAAA"));
        }
    }

    [Fact]
    public void The_cursor_round_trips_only_for_its_own_company_and_product()
    {
        var at = new DateOnly(2026, 11, 12);
        var cursor = PurchaseFutureSupplyReadPort.Cursor.Encode(CompanyA, ProductA, at, OrderA, 3);

        Assert.DoesNotContain('=', cursor);
        Assert.DoesNotContain('+', cursor);
        Assert.DoesNotContain('/', cursor);
        Assert.Equal((at, OrderA, 3), PurchaseFutureSupplyReadPort.Cursor.Decode(cursor, CompanyA, ProductA));
        Assert.Throws<ArgumentException>(() => PurchaseFutureSupplyReadPort.Cursor.Decode(cursor, CompanyB, ProductA));
        Assert.Throws<ArgumentException>(() => PurchaseFutureSupplyReadPort.Cursor.Decode(cursor, CompanyA, ProductB));
    }

    [Fact]
    public void A_cursor_with_a_line_number_below_one_is_refused()
    {
        var cursor = PurchaseFutureSupplyReadPort.Cursor.Encode(CompanyA, ProductA, new DateOnly(2026, 11, 12), OrderA, 0);

        Assert.Throws<ArgumentException>(() => PurchaseFutureSupplyReadPort.Cursor.Decode(cursor, CompanyA, ProductA));
    }

    [Fact]
    public void The_future_supply_line_carries_no_price_member_and_keeps_its_member_order()
    {
        string[] names = [.. typeof(PurchaseFutureSupplyLineV1).GetProperties().Select(property => property.Name)];
        Assert.Equal(
            ["CompanyId", "OrderId", "LineId", "LineNo", "SupplierPartyId", "CompanyAccountId", "ProductId", "ProductVersion", "UnitId",
                "Kind", "CommittedText", "AcceptedText", "CancelledText", "AllocatableText", "SalesSourceOrderId", "SalesSourceOrderLineId",
                "ObservedOrderVersion"],
            names);
        string[] barred = ["price", "amount", "discount", "currency", "payment", "money", "tax", "cost"];
        Assert.DoesNotContain(names, name => barred.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void The_contract_values_are_pinned()
    {
        Assert.Equal(1, (int)PurchaseFutureSupplyOutcomeV1.Allocatable);
        Assert.Equal(2, (int)PurchaseFutureSupplyOutcomeV1.NotAllocatable);
        Assert.Equal(3, (int)PurchaseFutureSupplyOutcomeV1.NotFound);
        Assert.Equal(3, Enum.GetValues<PurchaseFutureSupplyOutcomeV1>().Length);
        Assert.Equal("technical-line", PurchaseFutureSupplyStatusesV1.TechnicalLine);
        Assert.Equal(100, PurchaseFutureSupplyPageV1.MaximumPageSize);
    }

    [Fact]
    public void The_acceptance_stage_result_names_no_general_line_unless_given_so_older_stored_results_still_read()
    {
        var older = new PurchaseAcceptanceStageResultV1(
            CompanyA, Guid.NewGuid(), OrderA, LineA, "5", "2", "4", false, "correlation");
        Assert.Null(older.PurchaseOrderGeneralLineId);

        var general = Guid.NewGuid();
        Assert.Equal(general, (older with { PurchaseOrderGeneralLineId = general }).PurchaseOrderGeneralLineId);
        Assert.Equal("PurchaseOrderGeneralLineId", typeof(PurchaseAcceptanceStageResultV1).GetProperties().Last().Name);
    }

    private static (PurchasingDbContext Context, PurchaseFutureSupplyReadPort Port) Port(bool resolved)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        var context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        return (context, new PurchaseFutureSupplyReadPort(context, company));
    }
}
