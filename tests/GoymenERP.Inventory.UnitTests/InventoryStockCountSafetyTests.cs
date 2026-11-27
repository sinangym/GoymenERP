using System.Data;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryStockCountSafetyTests
{
    [Theory]
    [InlineData(false, "missing-writer")]
    [InlineData(true, "missing-writer")]
    [InlineData(false, "ambient")]
    [InlineData(true, "ambient")]
    [InlineData(false, "dirty")]
    [InlineData(true, "dirty")]
    [InlineData(false, "audit")]
    [InlineData(true, "audit")]
    public async Task Entry_refusal_preserves_caller_changes_and_audit_without_invoking_fresh_callback(bool decision, string boundary)
    {
        var company = new CompanyContext(); company.Set(CompanyA);
        await using var db = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        var writer = new Writer { HasPendingDrafts = boundary is "audit" or "missing-writer" };
        var store = new InventoryStore(db, company, new Collector(), new InventoryKilogramScale(null!), TimeProvider.System,
            lossDecisionAuditWriter: boundary == "missing-writer" ? null : writer);
        var row = new StockCountRow { Id = Guid.NewGuid(), CompanyId = CompanyA };
        if (boundary == "dirty") db.StockCounts.Add(row);
        using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
        var calls = 0;
        Task Fence(CancellationToken _) { calls++; return Task.CompletedTask; }
        var metadata = new InventoryCommandMetadata("checker", "synthetic.test", [], "corr", null, "key", "Web");
        var error = await Assert.ThrowsAsync<InventoryStockCountException>(() => decision
            ? store.DecideStockCountAsync(row.Id, new(InventoryStockCountDecisionKindV1.Approve, "Uygun"), metadata, Fence)
            : store.RecordStockCountAsync(new(row.Id, Label1, "1", "9", At, "Sayım"), metadata, Fence));
        Assert.Equal((503, InventoryStockCountCodesV1.Unavailable), (error.Status, error.Code));
        Assert.Equal(0, calls); Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards);
        Assert.Equal(boundary is "audit" or "missing-writer", writer.HasPendingDrafts);
        Assert.Equal(boundary == "dirty", db.ChangeTracker.HasChanges());
        if (boundary == "dirty") Assert.Same(row, Assert.Single(db.ChangeTracker.Entries<StockCountRow>()).Entity);
    }

    private sealed class Collector : IAuditEventCollector { public void Enqueue(AuditEventDraft draft) => throw new InvalidOperationException("Entry must not enqueue audit."); }
    private sealed class Writer : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts { get; set; }
        public int Writes; public int Discards;
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default)
        { Writes++; throw new InvalidOperationException("Entry must not flush audit."); }
        public void DiscardPending() { Discards++; HasPendingDrafts = false; }
    }
}
