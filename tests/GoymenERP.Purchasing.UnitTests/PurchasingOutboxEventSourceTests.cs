using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Infrastructure.Outbox;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchasingOutboxEventSourceTests
{
    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(ulong.MaxValue)]
    public void Cursor_preserves_the_complete_unsigned_database_position(ulong position)
    {
        var text = PurchasingOutboxEventSource.FormatPosition(position);
        Assert.Equal(20, text.Length);
        Assert.Equal(position, PurchasingOutboxEventSource.ParsePosition(text));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0000000000000000000x")]
    [InlineData("18446744073709551616")]
    [InlineData("+0000000000000000001")]
    public void Foreign_or_overflowing_cursors_are_refused(string text) =>
        Assert.Throws<InvalidOperationException>(() => PurchasingOutboxEventSource.ParsePosition(text));

    [Fact]
    public async Task An_unresolved_company_never_opens_a_connection()
    {
        var company = new CompanyContext();
        await using var database = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=(local);Database=ModelOnly;Integrated Security=true;TrustServerCertificate=true").Options, company);
        Assert.Empty(await new PurchasingOutboxEventSource(database, company).ReadAfterAsync(null, 20, default));
    }
}
