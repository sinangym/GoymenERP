using System.Data;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Logistics.UnitTests;

public sealed class LogisticsWeighingDifferenceStoreSafetyTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Missing_writer_or_ambient_boundary_refuses_before_callback_and_never_discards_caller_audit(bool supplyWriter)
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        await using var database = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>().Options, company);
        var writer = new UntouchedWriter(); var collector = new UntouchedCollector();
        var store = new LogisticsWeighingDifferenceStore(database, company, collector, TimeProvider.System, supplyWriter ? writer : null);
        using var ambient = supplyWriter ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
        var called = false;
        var error = await Assert.ThrowsAsync<LogisticsStoreException>(() => store.MutateAsync(
            new("SENTETIK-actor", "SENTETIK.permission", [], "SENTETIK-correlation", null, "SENTETIK-key", "Web"),
            new string('a', 64), LogisticsWeighingDifferenceCommandsV1.Approve, Guid.NewGuid(),
            (_, _) => throw new InvalidOperationException("Must not mutate"),
            _ => { called = true; return Task.CompletedTask; }));
        Assert.Equal(LogisticsWeighingDifferenceCodesV1.Unavailable, error.Code);
        Assert.False(called); Assert.False(writer.Discarded); Assert.False(collector.Called);
    }

    private sealed class UntouchedCollector : IAuditEventCollector
    {
        public bool Called;
        public void Enqueue(AuditEventDraft auditEvent) { Called = true; throw new InvalidOperationException("Must not enqueue"); }
    }
    private sealed class UntouchedWriter : IAuditTransactionWriterV1
    {
        public bool Discarded; public bool HasPendingDrafts => false;
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Must not flush");
        public void DiscardPending() => Discarded = true;
    }
}
