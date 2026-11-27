using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed class LogisticsWeighingDifferenceStoreBoundaryTests
{
    [Fact]
    public async Task Configured_nonrelational_store_refuses_before_callback_and_preserves_caller_audit()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        await using var database = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseInMemoryDatabase("SENTETIK-weighing-entry-" + Guid.NewGuid()).Options, company);
        var writer = new CallerWriter();
        var store = new LogisticsWeighingDifferenceStore(database, company, new AuditEventCollector(), TimeProvider.System, writer);
        var called = false;
        var error = await Assert.ThrowsAsync<LogisticsStoreException>(() => store.MutateAsync(
            new("SENTETIK-actor", "SENTETIK.permission", [], "SENTETIK-correlation", null, "SENTETIK-key", "Web"),
            new string('a', 64), LogisticsWeighingDifferenceCommandsV1.Approve, Guid.NewGuid(),
            (_, _) => throw new InvalidOperationException("Must not mutate"),
            _ => { called = true; return Task.CompletedTask; }));
        Assert.Equal(LogisticsWeighingDifferenceCodesV1.Unavailable, error.Code);
        Assert.False(called); Assert.False(writer.Discarded); Assert.True(writer.HasPendingDrafts);
        Assert.Empty(database.ChangeTracker.Entries());
    }

    private sealed class CallerWriter : IAuditTransactionWriterV1
    {
        public bool Discarded; public bool HasPendingDrafts => true;
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Must not flush caller drafts");
        public void DiscardPending() => Discarded = true;
    }
}
