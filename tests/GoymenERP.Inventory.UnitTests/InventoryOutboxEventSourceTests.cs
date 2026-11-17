using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryOutboxEventSourceTests
{
    [Theory]
    [InlineData(0UL, "00000000000000000000")]
    [InlineData(1UL, "00000000000000000001")]
    [InlineData(ulong.MaxValue, "18446744073709551615")]
    public void Cursor_has_canonical_numeric_order_and_round_trips(ulong value, string cursor)
    {
        Assert.Equal(cursor, InventoryOutboxEventSource.FormatPosition(value));
        Assert.Equal(value, InventoryOutboxEventSource.ParsePosition(cursor));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("0000000000000000000+")]
    [InlineData("0000000000000000000 ")]
    [InlineData("18446744073709551616")]
    [InlineData("0000000000000000000١")]
    public void Foreign_or_overflowing_cursor_is_refused(string cursor) =>
        Assert.Throws<InvalidOperationException>(() => InventoryOutboxEventSource.ParsePosition(cursor));

    [Fact]
    public async Task Unresolved_company_reads_no_database_and_cancelled_reads_do_not_hide_cancellation()
    {
        var company = new CompanyContext();
        await using var db = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;").Options, company);
        var source = new InventoryOutboxEventSource(db, company);
        Assert.Empty(await source.ReadAfterAsync(null, 10, CancellationToken.None));
        await Assert.ThrowsAsync<OperationCanceledException>(() => source.ReadAfterAsync(null, 10, new CancellationToken(true)));
    }
}
