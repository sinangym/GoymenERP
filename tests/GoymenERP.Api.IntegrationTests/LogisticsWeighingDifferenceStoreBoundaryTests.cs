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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Configured_nonrelational_store_refuses_with_clean_or_pending_audit_without_invoking_callbacks(bool pendingAudit)
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        await using var database = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseInMemoryDatabase("SENTETIK-weighing-entry-" + Guid.NewGuid()).Options, company);
        var writer = new CallerWriter(pendingAudit);
        var store = new LogisticsWeighingDifferenceStore(database, company, new AuditEventCollector(), TimeProvider.System, writer);
        var called = false;
        var error = await Assert.ThrowsAsync<LogisticsStoreException>(() => store.MutateAsync(
            new("SENTETIK-actor", "SENTETIK.permission", [], "SENTETIK-correlation", null, "SENTETIK-key", "Web"),
            new string('a', 64), LogisticsWeighingDifferenceCommandsV1.Approve, Guid.NewGuid(),
            (_, _) => throw new InvalidOperationException("Must not mutate"),
            _ => { called = true; return Task.CompletedTask; }));
        Assert.Equal(LogisticsWeighingDifferenceCodesV1.Unavailable, error.Code);
        Assert.False(called); Assert.False(writer.Discarded); Assert.Equal(pendingAudit, writer.HasPendingDrafts);
        Assert.Empty(database.ChangeTracker.Entries());
    }

    private sealed class CallerWriter(bool pendingAudit) : IAuditTransactionWriterV1
    {
        public bool Discarded; public bool HasPendingDrafts => pendingAudit;
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Must not flush caller drafts");
        public void DiscardPending() => Discarded = true;
    }
}
