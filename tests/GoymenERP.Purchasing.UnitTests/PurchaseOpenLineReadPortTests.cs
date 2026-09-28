using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// AT-025, the Purchasing side without a database: the open-line port refuses to read without a server-resolved company and
/// refuses a page size or cursor out of shape BEFORE any connection is opened (the connection string points nowhere, so
/// reaching the database would be a provider exception, not these refusals); its cursor is bound to the company that read
/// it; the contract type carries no price member. What it lists on real rows — approved only, remaining above zero, this
/// company only, newest first — is proven on SQL Server (<c>LogisticsOperationsWorkSqlServerRelationalAcceptanceTests</c>).
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseOpenLineReadPortTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("22222222-2222-4222-8222-222222222222");

    [Fact]
    public async Task Without_a_resolved_company_the_port_refuses_before_any_connection_and_never_reads_unscoped()
    {
        await using var context = Context(resolved: false);
        var port = new PurchaseOpenLineReadPort(context, new CompanyContext());

        await Assert.ThrowsAsync<InvalidOperationException>(() => port.ListOpenApprovedLinesAsync(10, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task A_page_size_out_of_range_is_refused_before_any_connection(int pageSize)
    {
        var (context, port) = Port();
        await using (context)
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => port.ListOpenApprovedLinesAsync(pageSize, null));
        }
    }

    [Fact]
    public async Task Another_companys_cursor_and_a_malformed_cursor_are_argument_errors_before_any_connection()
    {
        var foreign = PurchaseOpenLineReadPort.Cursor.Encode(CompanyB, DateTimeOffset.UtcNow, Guid.NewGuid(), 1);
        var (context, port) = Port();
        await using (context)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => port.ListOpenApprovedLinesAsync(10, foreign));
            await Assert.ThrowsAsync<ArgumentException>(() => port.ListOpenApprovedLinesAsync(10, "not a cursor!"));
            await Assert.ThrowsAsync<ArgumentException>(() => port.ListOpenApprovedLinesAsync(10, "AAAA"));
        }
    }

    [Fact]
    public void The_cursor_round_trips_only_for_its_own_company()
    {
        var at = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);
        var order = Guid.NewGuid();
        var cursor = PurchaseOpenLineReadPort.Cursor.Encode(CompanyA, at, order, 3);

        Assert.Equal((at, order, 3), PurchaseOpenLineReadPort.Cursor.Decode(cursor, CompanyA));
        Assert.Throws<ArgumentException>(() => PurchaseOpenLineReadPort.Cursor.Decode(cursor, CompanyB));
    }

    [Fact]
    public void The_open_line_carries_no_price_member()
    {
        string[] names = [.. typeof(PurchaseOpenLineReferenceV1).GetProperties().Select(property => property.Name)];
        Assert.Equal(
            ["CompanyId", "OrderId", "OrderEffectiveOn", "LineId", "LineNo", "Kind", "SupplierPartyId", "CompanyAccountId", "ProductId",
                "ProductVersion", "UnitId", "CommittedText", "AcceptedText", "RemainingText"],
            names);
        string[] barred = ["price", "amount", "discount", "currency", "payment", "money", "tax", "cost"];
        Assert.DoesNotContain(names, name => barred.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    private static (PurchasingDbContext Context, PurchaseOpenLineReadPort Port) Port()
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        var context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        return (context, new PurchaseOpenLineReadPort(context, company));
    }

    private static PurchasingDbContext Context(bool resolved)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        return new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }
}
