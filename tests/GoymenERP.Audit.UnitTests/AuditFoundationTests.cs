using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Audit.UnitTests;

public sealed class AuditFoundationTests
{
    [Fact]
    public void CorrelationContext_IsImmutableInsideRequestScope()
    {
        var context = new CorrelationContext();
        context.Set("correlation-1", "cause-1", SourceChannels.Web, "127.0.0.1", "test");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            context.Set("correlation-2", "cause-1", SourceChannels.Web, null, null));

        Assert.Equal("correlation-1", context.CorrelationId);
        Assert.Equal("cause-1", context.CausationId);
        Assert.Contains("değiştirilemez", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuditDbContext_RejectsNormalApplicationWrites()
    {
        var companyContext = new CompanyContext();
        companyContext.Set(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase($"audit-unit-{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new AuditDbContext(options, companyContext);
        dbContext.AuditEvents.Add(CreateEvent(companyContext.CompanyId));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dbContext.SaveChangesAsync());

        Assert.Contains("değiştirilemez", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditEvent_RequiresValidSha256Evidence()
    {
        var data = CreateData(Guid.NewGuid()) with { IntegrityHash = "not-a-hash" };

        var exception = Assert.Throws<ArgumentException>(() => AuditEvent.Create(data));

        Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
    }

    private static AuditEvent CreateEvent(Guid companyId)
    {
        return AuditEvent.Create(CreateData(companyId));
    }

    private static AuditEventData CreateData(Guid companyId)
    {
        return new AuditEventData(
            Guid.NewGuid(),
            companyId,
            DateTimeOffset.UtcNow,
            "test.event.v1",
            "Test.Entity",
            Guid.NewGuid().ToString("D"),
            "Update",
            1,
            2,
            "User",
            Guid.NewGuid().ToString("D"),
            "test.permission",
            "[\"TEST_ROLE\"]",
            "{\"value\":1}",
            "{\"value\":2}",
            "Birim testi",
            null,
            "None",
            null,
            SourceChannels.Web,
            "127.0.0.1",
            "unit-test",
            "correlation-test",
            "causation-test",
            new string('A', 64),
            "Succeeded",
            1,
            new string('B', 64));
    }
}
