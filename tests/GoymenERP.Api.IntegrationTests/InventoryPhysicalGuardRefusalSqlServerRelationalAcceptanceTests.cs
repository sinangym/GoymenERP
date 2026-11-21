using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Stock_count_guard_loser_returns_409_rechecks_authority_and_rolls_back_before_retry_and_replay()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-COUNT-GUARD-LOSER", 100m);
        await ApproveCountOpeningCostAsync(run, source.ProductId, "12.5");
        var request = new RecordInventoryStockCountV1(Guid.NewGuid(), source.LabelId,
            source.LabelVersion.ToString(), "97.5", At.AddHours(2), "Sentetik guard çakışması.");
        await using (var recorder = run.Store(CompanyA))
            await recorder.Store.RecordStockCountAsync(request, Metadata(run), static _ => Task.CompletedTask);
        var baseline = await CountEffectsAsync(run, source.LabelId);
        var guardVersion = await PhysicalGuardVersionAsync(run);
        var approver = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D"), IdempotencyKey = $"guard-{Guid.NewGuid():N}" };
        var approval = new DecideInventoryStockCountV1(InventoryStockCountDecisionKindV1.Approve, "Tartım doğrulandı.");

        foreach (var revokeAfterGuardLoss in new[] { false, true })
        {
            await using var scope = run.Store(CompanyA);
            using var loser = new PhysicalGuardSqlConcurrencyLoser(scope.DbContext.Database.GetDbConnection());
            var authorityAfterLoss = 0;
            var error = await Assert.ThrowsAsync<InventoryStockCountException>(() => scope.Store.DecideStockCountAsync(
                request.CountId, approval, approver, _ =>
                {
                    if (loser.Injected)
                    {
                        authorityAfterLoss++;
                        if (revokeAfterGuardLoss)
                            throw new InventoryStockCountException(403, InventoryStockCountCodesV1.Forbidden);
                    }
                    return Task.CompletedTask;
                }));
            Assert.True(loser.Injected);
            Assert.Equal(1, authorityAfterLoss);
            Assert.Equal(revokeAfterGuardLoss ? (403, InventoryStockCountCodesV1.Forbidden)
                : (409, InventoryStockCountCodesV1.ConcurrentChange), (error.Status, error.Code));
            if (!revokeAfterGuardLoss)
                Assert.IsType<DbUpdateConcurrencyException>(Assert.IsType<InventoryStoreException>(error.InnerException).InnerException);
            Assert.Equal(baseline, await CountEffectsAsync(run, source.LabelId));
            Assert.Equal(guardVersion, await PhysicalGuardVersionAsync(run));
        }

        await using (var scope = run.Store(CompanyA))
        {
            Assert.False((await scope.Store.DecideStockCountAsync(request.CountId, approval, approver,
                static _ => Task.CompletedTask)).IsReplay);
            var accepted = await CountEffectsAsync(run, source.LabelId);
            Assert.Equal(baseline.AdjustmentMovements + 1, accepted.AdjustmentMovements);
            Assert.True((await scope.Store.DecideStockCountAsync(request.CountId, approval, approver,
                static _ => Task.CompletedTask)).IsReplay);
            Assert.Equal(accepted, await CountEffectsAsync(run, source.LabelId));
        }
    }

    [SqlServerFact]
    public async Task Stock_scrap_guard_loser_returns_409_rechecks_authority_and_rolls_back_before_retry_and_replay()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-SCRAP-GUARD-LOSER", 100m);
        var request = new RecordInventoryStockScrapV1(Guid.NewGuid(), source.LabelId,
            source.LabelVersion.ToString(), "12.5", At.AddHours(2), "Sentetik guard çakışması.");
        await using (var recorder = run.Store(CompanyA))
            await recorder.Store.RecordStockScrapAsync(request, Metadata(run), static _ => Task.CompletedTask);
        var baseline = await ScrapEffectsAsync(run, source.LabelId);
        var guardVersion = await PhysicalGuardVersionAsync(run);
        var approver = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D"), IdempotencyKey = $"guard-{Guid.NewGuid():N}" };
        var approval = new DecideInventoryStockScrapV1(InventoryStockScrapDecisionKindV1.Approve, "Hurda doğrulandı.");

        foreach (var revokeAfterGuardLoss in new[] { false, true })
        {
            await using var scope = run.Store(CompanyA);
            using var loser = new PhysicalGuardSqlConcurrencyLoser(scope.DbContext.Database.GetDbConnection());
            var authorityAfterLoss = 0;
            var error = await Assert.ThrowsAsync<InventoryStockScrapException>(() => scope.Store.DecideStockScrapAsync(
                request.ScrapId, approval, approver, _ =>
                {
                    if (loser.Injected)
                    {
                        authorityAfterLoss++;
                        if (revokeAfterGuardLoss)
                            throw new InventoryStockScrapException(403, InventoryStockScrapCodesV1.Forbidden);
                    }
                    return Task.CompletedTask;
                }));
            Assert.True(loser.Injected);
            Assert.Equal(1, authorityAfterLoss);
            Assert.Equal(revokeAfterGuardLoss ? (403, InventoryStockScrapCodesV1.Forbidden)
                : (409, InventoryStockScrapCodesV1.ConcurrentChange), (error.Status, error.Code));
            if (!revokeAfterGuardLoss)
                Assert.IsType<DbUpdateConcurrencyException>(Assert.IsType<InventoryStoreException>(error.InnerException).InnerException);
            Assert.Equal(baseline, await ScrapEffectsAsync(run, source.LabelId));
            Assert.Equal(guardVersion, await PhysicalGuardVersionAsync(run));
        }

        await using (var scope = run.Store(CompanyA))
        {
            Assert.False((await scope.Store.DecideStockScrapAsync(request.ScrapId, approval, approver,
                static _ => Task.CompletedTask)).IsReplay);
            var accepted = await ScrapEffectsAsync(run, source.LabelId);
            Assert.Equal(baseline.ScrapMovements + 1, accepted.ScrapMovements);
            Assert.True((await scope.Store.DecideStockScrapAsync(request.ScrapId, approval, approver,
                static _ => Task.CompletedTask)).IsReplay);
            Assert.Equal(accepted, await ScrapEffectsAsync(run, source.LabelId));
        }
    }

    private static async Task<long> PhysicalGuardVersionAsync(Run run)
    {
        await using var context = run.Context(CompanyA);
        return await context.NegativeStockValidationGuards.AsNoTracking().Select(row => row.Version).SingleAsync();
    }

    // The guard creates its own borrowed context, so owner-context interceptors do not reach it.
    // Change only its optimistic WHERE version: SQL really returns zero affected rows and EF raises
    // DbUpdateConcurrencyException. No exception is fabricated and no production hook or SQL is added.
    private sealed class PhysicalGuardSqlConcurrencyLoser :
        IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly DbConnection ownerConnection;
        private readonly ConcurrentBag<IDisposable> subscriptions = [];
        private readonly IDisposable allListeners;
        private int injected;

        public PhysicalGuardSqlConcurrencyLoser(DbConnection ownerConnection)
        {
            this.ownerConnection = ownerConnection;
            allListeners = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public bool Injected => Volatile.Read(ref injected) != 0;

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.EntityFrameworkCore") subscriptions.Add(listener.Subscribe(this));
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key != CoreEventId.SaveChangesStarting.Name || value.Value is not DbContextEventData data
                || data.Context is not InventoryDbContext context
                || !ReferenceEquals(context.Database.GetDbConnection(), ownerConnection)) return;
            var guard = context.ChangeTracker.Entries().SingleOrDefault(entry =>
                entry.Metadata.GetTableName() == "NegativeStockValidationGuards");
            if (guard is null || guard.State != EntityState.Modified
                || Interlocked.CompareExchange(ref injected, 1, 0) != 0) return;
            guard.Property("Version").OriginalValue = checked((long)guard.Property("Version").CurrentValue! + 1L);
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose()
        {
            allListeners.Dispose();
            foreach (var subscription in subscriptions) subscription.Dispose();
        }

    }
}
