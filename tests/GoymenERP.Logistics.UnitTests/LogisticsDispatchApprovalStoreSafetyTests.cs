using System.Data;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Logistics.UnitTests;

public sealed class LogisticsDispatchApprovalStoreSafetyTests
{
    [Theory]
    [InlineData("replay", "missing-writer")] [InlineData("approve", "missing-writer")] [InlineData("refusal", "missing-writer")]
    [InlineData("replay", "ambient")] [InlineData("approve", "ambient")] [InlineData("refusal", "ambient")]
    [InlineData("replay", "audit")] [InlineData("approve", "audit")] [InlineData("refusal", "audit")]
    public async Task Refused_entry_preserves_caller_and_never_reads_or_calls_fresh_authority(string operation, string boundary)
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        await using var db = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseSqlServer("Server=localhost;Database=SENTETIK_never_connect;Integrated Security=true;TrustServerCertificate=true").Options, company);
        var collector = new UntouchedCollector(); var writer = new UntouchedWriter { HasPendingDrafts = boundary == "audit" };
        var store = new LogisticsStore(db, company, collector, TimeProvider.System, boundary == "missing-writer" ? null : writer);
        using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
        var called = false;
        Task Fence(CancellationToken _) { called = true; return Task.CompletedTask; }
        var envelope = new LogisticsWriteEnvelope("SENTETIK-checker", "logistics.dispatch-notes.approve", [], "SENTETIK-correlation", null, "SENTETIK-key", "Web");
        var error = await Assert.ThrowsAsync<LogisticsStoreException>(async () =>
        {
            if (operation == "replay") await store.FindDispatchNoteApprovalReplayAsync(envelope, new string('a', 64), Guid.NewGuid(), Guid.NewGuid(), 1, Fence);
            else if (operation == "approve") await store.ApproveDispatchNoteOwnedAsync(envelope, new string('a', 64), Guid.NewGuid(), Guid.NewGuid(), 1,
                new("SENTETIK.approve.v1", "Approve", "SENTETIK"), Fence);
            else await store.RecordDispatchNoteApprovalRefusalAsync(envelope, new string('a', 64), new(409, "SENTETIK", []), Fence);
        });
        Assert.Equal("logistics_dispatch_note_approval_unavailable", error.Code);
        Assert.False(called); Assert.False(writer.Discarded); Assert.False(collector.Called);
        Assert.Equal(boundary == "audit", writer.HasPendingDrafts); Assert.Empty(db.ChangeTracker.Entries());
    }
    private sealed class UntouchedCollector : IAuditEventCollector
    {
        public bool Called;
        public void Enqueue(AuditEventDraft draft) { Called = true; throw new InvalidOperationException("Must not enqueue"); }
    }
    private sealed class UntouchedWriter : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts { get; set; }
        public bool Discarded;
        public Task WritePendingAsync(IDbTransaction tx, CancellationToken token = default) => throw new InvalidOperationException("Must not flush");
        public void DiscardPending() => Discarded = true;
    }
}
