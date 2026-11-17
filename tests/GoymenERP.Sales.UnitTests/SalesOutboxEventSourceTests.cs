using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Infrastructure.Outbox;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesOutboxEventSourceTests
{
    [Fact]
    public void Source_declaration_keeps_the_existing_identity_and_exact_producer_event_versions()
    {
        Assert.Equal("sales", SalesOutboxContractV1.SourceName);
        Assert.Equal(new[] { ("sales.invoice.finalized.v1", 1), ("sales.return-invoice.finalized.v1", 1) },
            SalesOutboxContractV1.Publishes.Select(item => (item.EventType, item.Version)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("-0000000000000000001")]
    [InlineData("0000000000000000000 ")]
    [InlineData("18446744073709551616")]
    public async Task Invalid_cursor_is_refused_before_database_access(string cursor)
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        await using var database = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>().Options, company);
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => new SalesOutboxEventSourceV1(database, company)
            .ReadAfterAsync(cursor, 1, default));
        Assert.Equal("Satış outbox imleci geçersiz.", refused.Message);
    }

    [Fact]
    public async Task Unresolved_company_empty_page_and_cancellation_do_not_open_a_database()
    {
        var company = new CompanyContext();
        await using var database = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>().Options, company);
        var source = new SalesOutboxEventSourceV1(database, company);
        Assert.Empty(await source.ReadAfterAsync(null, 1, default));
        company.Set(Guid.NewGuid());
        Assert.Empty(await source.ReadAfterAsync(null, 0, default));
        Assert.Empty(await source.ReadAfterAsync(null, -1, default));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadAfterAsync(null, 1, cancelled.Token));
    }
}
