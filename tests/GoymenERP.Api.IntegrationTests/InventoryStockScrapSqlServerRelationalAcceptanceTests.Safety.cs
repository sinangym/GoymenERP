using System.Data.Common;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Stock_scrap_record_and_decision_entry_refusals_preserve_caller_transaction_changes_and_audit_even_for_replay()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-SCRAP-SAFETY-ENTRY", 100m);
        var request = Scrap(source.LabelId, source.LabelVersion, "1");
        var record = Metadata(run);
        var checker = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };
        var decision = new DecideInventoryStockScrapV1(InventoryStockScrapDecisionKindV1.Approve, "Ä°kinci kiÅŸi doÄŸruladÄ±.");
        await using (var seed = run.Store(CompanyA))
        {
            await seed.Store.RecordStockScrapAsync(request, record, static _ => Task.CompletedTask);
            await seed.Store.DecideStockScrapAsync(request.ScrapId, decision, checker, static _ => Task.CompletedTask);
        }
        foreach (var isDecision in new[] { false, true })
        foreach (var replay in new[] { false, true })
        foreach (var boundary in new[] { "missing-writer", "borrowed", "dirty", "audit", "ambient" })
        {
            await using var scope = run.Store(CompanyA);
            var store = boundary == "missing-writer"
                ? new InventoryStore(scope.DbContext, scope.CompanyContext, scope.AuditCollector, new InventoryKilogramScale(null!), TimeProvider.System)
                : scope.Store;
            await using var borrowed = boundary == "borrowed" ? await scope.DbContext.Database.BeginTransactionAsync() : null;
            var dirty = boundary == "dirty" ? await scope.DbContext.Labels.SingleAsync(row => row.Id == source.LabelId) : null;
            if (dirty is not null) dirty.Version++;
            if (boundary is "audit" or "missing-writer") scope.AuditCollector.Enqueue(LossSafetyCallerDraft(run));
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            var called = false;
            Task Fence(CancellationToken _) { called = true; return Task.CompletedTask; }
            var error = await Assert.ThrowsAsync<InventoryStockScrapException>(() => isDecision
                ? store.DecideStockScrapAsync(replay ? request.ScrapId : Guid.NewGuid(), decision, checker, Fence)
                : store.RecordStockScrapAsync(replay ? request : request with { ScrapId = Guid.NewGuid() }, record, Fence));
            Assert.Equal((503, InventoryStockScrapCodesV1.Unavailable), (error.Status, error.Code));
            Assert.False(called); Assert.Same(borrowed, scope.DbContext.Database.CurrentTransaction);
            Assert.Equal(boundary == "dirty", scope.DbContext.ChangeTracker.HasChanges());
            if (dirty is not null) Assert.Same(dirty, Assert.Single(scope.DbContext.ChangeTracker.Entries()).Entity);
            Assert.Equal(boundary is "audit" or "missing-writer", scope.AuditWriter.HasPendingDrafts);
            if (boundary is "audit" or "missing-writer") scope.AuditWriter.DiscardPending();
        }
    }

    [SqlServerFact]
    public async Task Stock_scrap_before_save_and_post_save_authority_or_company_loss_rolls_back_then_same_key_succeeds()
    {
        await using var run = await Run.StartAsync();
        foreach (var operation in new[] { "record", "approve", "reject" })
        foreach (var phase in new[] { "before-save", "after-save", "company-after-save" })
        {
            var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-SCRAP-SAFETY-" + Guid.NewGuid().ToString("N"), 100m);
            var request = Scrap(source.LabelId, source.LabelVersion, "1");
            if (operation != "record")
                await using (var seed = run.Store(CompanyA))
                    await seed.Store.RecordStockScrapAsync(request, Metadata(run), static _ => Task.CompletedTask);
            var before = await ScrapEffectsAsync(run, source.LabelId);
            var probe = new StockScrapSafetySaveProbe(request.ScrapId);
            await using var scope = run.Store(CompanyA, probe);
            var metadata = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };
            var deny = true;
            Task Fence(CancellationToken _)
            {
                if (deny && (phase == "before-save" ? scope.AuditWriter.HasPendingDrafts : probe.Saved))
                {
                    if (phase == "company-after-save") scope.CompanyContext.Set(CompanyB);
                    else throw new InventoryStockScrapException(403, InventoryStockScrapCodesV1.Forbidden);
                }
                return Task.CompletedTask;
            }
            Task<InventoryStockScrapResultV1> Invoke() => operation == "record"
                ? scope.Store.RecordStockScrapAsync(request, metadata, Fence)
                : scope.Store.DecideStockScrapAsync(request.ScrapId,
                    new(operation == "approve" ? InventoryStockScrapDecisionKindV1.Approve : InventoryStockScrapDecisionKindV1.Reject, "Hurda kararÄ± doÄŸrulandÄ±."), metadata, Fence);
            var error = await Assert.ThrowsAsync<InventoryStockScrapException>(() => Invoke());
            Assert.Equal((403, InventoryStockScrapCodesV1.Forbidden), (error.Status, error.Code));
            Assert.Equal(phase != "before-save", probe.Saved);
            Assert.Empty(scope.DbContext.ChangeTracker.Entries()); Assert.False(scope.AuditWriter.HasPendingDrafts);
            Assert.Null(scope.DbContext.Database.CurrentTransaction);
            scope.CompanyContext.Set(CompanyA);
            Assert.Equal(before, await ScrapEffectsAsync(run, source.LabelId));
            deny = false;
            var result = await Invoke(); Assert.False(result.IsReplay);
            var committed = await ScrapEffectsAsync(run, source.LabelId);
            var replay = await Invoke(); Assert.True(replay.IsReplay);
            Assert.Equal(result.OriginalCorrelationId, replay.OriginalCorrelationId); Assert.Equal(result.Scrap, replay.Scrap);
            Assert.Equal(committed, await ScrapEffectsAsync(run, source.LabelId));
        }
    }

    [SqlServerFact]
    public async Task Stock_scrap_without_audit_interceptor_flushes_real_scoped_writer_in_owned_transaction_and_rolls_back_late_loss()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-SCRAP-SAFETY-WRITER", 100m);
        var request = Scrap(source.LabelId, source.LabelVersion, "1");
        var record = Metadata(run);
        await using (var noInterceptor = StockCountSafetyCreateStore(run, false, null))
        {
            await noInterceptor.Store.RecordStockScrapAsync(request, record, static _ => Task.CompletedTask);
            Assert.False(noInterceptor.AuditWriter.HasPendingDrafts);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'inventory.stock-scrap.recorded.v1' AND EntityId=@p", ("@p", request.ScrapId.ToString("D"))));
        var before = await ScrapEffectsAsync(run, source.LabelId);
        var probe = new StockScrapSafetySaveProbe(request.ScrapId);
        await using var scope = StockCountSafetyCreateStore(run, false, null, probe);
        var checker = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };
        var decision = new DecideInventoryStockScrapV1(InventoryStockScrapDecisionKindV1.Approve, "GerÃ§ek audit writer ile doÄŸrulandÄ±.");
        var deny = true; var observedFlush = false;
        Task Fence(CancellationToken _)
        {
            if (deny && probe.Saved && !scope.AuditWriter.HasPendingDrafts)
            { observedFlush = true; throw new InventoryStockScrapException(403, InventoryStockScrapCodesV1.Forbidden); }
            return Task.CompletedTask;
        }
        Assert.Equal(403, (await Assert.ThrowsAsync<InventoryStockScrapException>(() => scope.Store.DecideStockScrapAsync(request.ScrapId, decision, checker, Fence))).Status);
        Assert.True(observedFlush); Assert.Equal(before, await ScrapEffectsAsync(run, source.LabelId));
        Assert.Empty(scope.DbContext.ChangeTracker.Entries()); Assert.False(scope.AuditWriter.HasPendingDrafts);
        deny = false;
        await scope.Store.DecideStockScrapAsync(request.ScrapId, decision, checker, Fence);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'inventory.stock-scrap.approved.v1' AND EntityId=@p", ("@p", request.ScrapId.ToString("D"))));
        Assert.False(scope.AuditWriter.HasPendingDrafts);
    }

    [SqlServerFact]
    public async Task Stock_scrap_save_race_aborts_before_one_bounded_winner_read_without_mutation_retry_or_conflict_receipt()
    {
        await using var run = await Run.StartAsync();
        foreach (var operation in new[] { "record", "approve", "reject" })
        {
            var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-SCRAP-SAFETY-RACE-" + Guid.NewGuid().ToString("N"), 100m);
            var request = Scrap(source.LabelId, source.LabelVersion, "1");
            if (operation != "record")
                await using (var seed = run.Store(CompanyA))
                    await seed.Store.RecordStockScrapAsync(request, Metadata(run), static _ => Task.CompletedTask);
            var baseline = await ScrapEffectsAsync(run, source.LabelId);
            var probe = new StockScrapSafetySaveProbe(request.ScrapId) { Concurrency = true };
            await using var scope = run.Store(CompanyA, probe);
            var metadata = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };
            var decision = new DecideInventoryStockScrapV1(operation == "approve" ? InventoryStockScrapDecisionKindV1.Approve : InventoryStockScrapDecisionKindV1.Reject, "YarÄ±ÅŸ kararÄ±.");
            Task<InventoryStockScrapResultV1> Invoke(InventoryStore store, Func<CancellationToken, Task> fence) => operation == "record"
                ? store.RecordStockScrapAsync(request, metadata, fence)
                : store.DecideStockScrapAsync(request.ScrapId, decision, metadata, fence);
            var transactions = new HashSet<Guid>();
            Task CountTransactions(CancellationToken _) { transactions.Add(scope.DbContext.Database.CurrentTransaction!.TransactionId); return Task.CompletedTask; }
            var absent = await Assert.ThrowsAsync<InventoryStockScrapException>(() => Invoke(scope.Store, CountTransactions));
            Assert.Equal((409, InventoryStockScrapCodesV1.ConcurrentChange), (absent.Status, absent.Code));
            Assert.Equal(2, transactions.Count); Assert.Equal(1, probe.SaveAttempts);
            Assert.Equal(baseline, await ScrapEffectsAsync(run, source.LabelId));
            Assert.Empty(scope.DbContext.ChangeTracker.Entries()); Assert.False(scope.AuditWriter.HasPendingDrafts);
            Guid? first = null; InventoryStockScrapResultV1? winner = null;
            async Task PublishWinner(CancellationToken _)
            {
                var current = scope.DbContext.Database.CurrentTransaction!.TransactionId;
                first ??= current;
                if (current != first && winner is null)
                {
                    Assert.Empty(scope.DbContext.ChangeTracker.Entries()); Assert.False(scope.AuditWriter.HasPendingDrafts);
                    await using var actual = run.Store(CompanyA);
                    var winnerMetadata = metadata with { CorrelationId = "stock-scrap-winner-" + Guid.NewGuid().ToString("N") };
                    winner = operation == "record"
                        ? await actual.Store.RecordStockScrapAsync(request, winnerMetadata, static _ => Task.CompletedTask)
                        : await actual.Store.DecideStockScrapAsync(request.ScrapId, decision, winnerMetadata, static _ => Task.CompletedTask);
                    Assert.False(winner.IsReplay);
                }
            }
            var replay = await Invoke(scope.Store, PublishWinner);
            Assert.NotNull(winner); Assert.True(replay.IsReplay); Assert.Equal(winner.Scrap, replay.Scrap);
            Assert.Equal(winner.OriginalCorrelationId, replay.OriginalCorrelationId);
            Assert.NotEqual(metadata.CorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(2, probe.SaveAttempts); // one failed save per outer call; second transactions only read.
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands WHERE CommandName=@c AND TargetId=@p", ("@c", operation == "record" ? InventoryStore.RecordStockScrapCommand : InventoryStore.DecideStockScrapCommand), ("@p", request.ScrapId)));
        }
    }

    [SqlServerFact]
    public async Task Stock_scrap_old_key_replays_immutable_evidence_after_a_later_scrap_changes_label_version_but_still_requires_live_authority()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-SCRAP-SAFETY-IMMUTABLE", 100m);
        var request = Scrap(source.LabelId, source.LabelVersion, "1");
        var recorder = Metadata(run);
        var checker = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };
        var decision = new DecideInventoryStockScrapV1(InventoryStockScrapDecisionKindV1.Approve, "Ä°lk hurda doÄŸrulandÄ±.");
        InventoryStockScrapResultV1 originalRecord; InventoryStockScrapResultV1 originalDecision;
        await using (var scope = run.Store(CompanyA))
        {
            originalRecord = await scope.Store.RecordStockScrapAsync(request, recorder, static _ => Task.CompletedTask);
            originalDecision = await scope.Store.DecideStockScrapAsync(request.ScrapId, decision, checker, static _ => Task.CompletedTask);
        }
        var later = Scrap(source.LabelId, await LabelVersionAsync(run, source.LabelId), "2");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockScrapAsync(later, Metadata(run), static _ => Task.CompletedTask);
            await scope.Store.DecideStockScrapAsync(later.ScrapId,
                new(InventoryStockScrapDecisionKindV1.Approve, "Sonraki hurda doÄŸrulandÄ±."),
                Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") }, static _ => Task.CompletedTask);
        }
        var committed = await ScrapEffectsAsync(run, source.LabelId);
        await using var replayScope = run.Store(CompanyA);
        var recordReplay = await replayScope.Store.RecordStockScrapAsync(request, recorder, static _ => Task.CompletedTask);
        var decisionReplay = await replayScope.Store.DecideStockScrapAsync(request.ScrapId, decision, checker, static _ => Task.CompletedTask);
        Assert.Equal(originalRecord with { IsReplay = true }, recordReplay);
        Assert.Equal(originalDecision with { IsReplay = true }, decisionReplay);
        Task Deny(CancellationToken _) => throw new InventoryStockScrapException(403, InventoryStockScrapCodesV1.Forbidden);
        Assert.Equal(403, (await Assert.ThrowsAsync<InventoryStockScrapException>(() => replayScope.Store.RecordStockScrapAsync(request, recorder, Deny))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<InventoryStockScrapException>(() => replayScope.Store.DecideStockScrapAsync(request.ScrapId, decision, checker, Deny))).Status);
        Assert.Equal(committed, await ScrapEffectsAsync(run, source.LabelId));
        Assert.Empty(replayScope.DbContext.ChangeTracker.Entries()); Assert.False(replayScope.AuditWriter.HasPendingDrafts);
    }

    [SqlServerFact]
    public async Task Stock_scrap_rollback_or_dispose_failure_still_discards_owned_tracker_and_audit_without_starting_winner_replay()
    {
        await using var run = await Run.StartAsync();
        foreach (var operation in new[] { "record", "approve", "reject" })
        foreach (var failure in new[] { "rollback", "dispose", "both" })
        {
            var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-SCRAP-CLEANUP-" + Guid.NewGuid().ToString("N"), 100m);
            var request = Scrap(source.LabelId, source.LabelVersion, "1");
            if (operation != "record")
                await using (var seed = run.Store(CompanyA))
                    await seed.Store.RecordStockScrapAsync(request, Metadata(run), static _ => Task.CompletedTask);
            var baseline = await ScrapEffectsAsync(run, source.LabelId);
            var fault = new InventoryOwnedCleanupFaultProbe(failure != "dispose", failure != "rollback");
            var saveProbe = new StockScrapSafetySaveProbe(request.ScrapId) { Concurrency = true };
            await using var scope = StockCountSafetyCreateStore(run, true, fault, fault, saveProbe);
            var metadata = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };
            Task Fence(CancellationToken _)
            {
                if (scope.AuditWriter.HasPendingDrafts)
                {
                    Assert.NotEmpty(scope.DbContext.ChangeTracker.Entries());
                    fault.Armed = true;
                    // Arm cleanup faults without throwing inside the fresh-authority callback.
                    // The actual SavingChanges probe fails after the native audit flush.
                }
                return Task.CompletedTask;
            }
            var error = await Assert.ThrowsAsync<InventoryOwnedCleanupFaultException>(() => operation == "record"
                ? scope.Store.RecordStockScrapAsync(request, metadata, Fence)
                : scope.Store.DecideStockScrapAsync(request.ScrapId,
                    new(operation == "approve" ? InventoryStockScrapDecisionKindV1.Approve : InventoryStockScrapDecisionKindV1.Reject, "Hurda kararÄ±."), metadata, Fence));
            Assert.Equal(failure == "rollback" ? "rollback" : "dispose", error.Stage);
            Assert.Equal(1, saveProbe.SaveAttempts);
            Assert.Equal(1, fault.Starts); Assert.Equal(1, fault.RollbackAttempts);
            Assert.Equal(failure != "dispose", fault.RollbackFailed);
            Assert.Equal(failure != "rollback", fault.DisposeFailed);
            Assert.Empty(scope.DbContext.ChangeTracker.Entries()); Assert.False(scope.AuditWriter.HasPendingDrafts);
            Assert.Equal(baseline, await ScrapEffectsAsync(run, source.LabelId));

            // EF may retain its disposed transaction handle when disposal diagnostics itself fails.
            // A retained handle is refused without touching caller state; otherwise this same scope can retry.
            var retained = scope.DbContext.Database.CurrentTransaction;
            Task<InventoryStockScrapResultV1> Retry(Func<CancellationToken, Task> fence) => operation == "record"
                ? scope.Store.RecordStockScrapAsync(request, metadata, fence)
                : scope.Store.DecideStockScrapAsync(request.ScrapId,
                    new(operation == "approve" ? InventoryStockScrapDecisionKindV1.Approve : InventoryStockScrapDecisionKindV1.Reject, "Hurda kararÄ±."), metadata, fence);
            if (retained is not null)
            {
                var called = false;
                var refusal = await Assert.ThrowsAsync<InventoryStockScrapException>(() => Retry(_ => { called = true; return Task.CompletedTask; }));
                Assert.Equal((503, InventoryStockScrapCodesV1.Unavailable), (refusal.Status, refusal.Code));
                Assert.False(called); Assert.Same(retained, scope.DbContext.Database.CurrentTransaction);
                Assert.Equal(1, fault.Starts);
            }
            else
            {
                saveProbe.Concurrency = false;
                Assert.False((await Retry(static _ => Task.CompletedTask)).IsReplay);
                Assert.Equal(2, fault.Starts);
            }
            Assert.DoesNotContain(scope.DbContext.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
            Assert.False(scope.AuditWriter.HasPendingDrafts);
        }
    }

    [SqlServerFact]
    public async Task Stock_scrap_reentrant_record_and_decision_refuse_without_touching_the_outer_owned_attempt()
    {
        await using var run = await Run.StartAsync();
        foreach (var decision in new[] { false, true })
        {
            var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-SCRAP-REENTRY-" + Guid.NewGuid().ToString("N"), 100m);
            var request = Scrap(source.LabelId, source.LabelVersion, "1");
            if (decision)
                await using (var seed = run.Store(CompanyA))
                    await seed.Store.RecordStockScrapAsync(request, Metadata(run), static _ => Task.CompletedTask);
            await using var scope = run.Store(CompanyA);
            var metadata = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };
            var checkedEntry = false;
            async Task Fence(CancellationToken token)
            {
                if (checkedEntry) return;
                checkedEntry = true;
                var owned = scope.DbContext.Database.CurrentTransaction;
                Assert.NotNull(owned);
                var entries = scope.DbContext.ChangeTracker.Entries().Count();
                var pending = scope.AuditWriter.HasPendingDrafts;
                var called = false;
                Task Nested(CancellationToken _) { called = true; return Task.CompletedTask; }
                var error = await Assert.ThrowsAsync<InventoryStockScrapException>(() => decision
                    ? scope.Store.DecideStockScrapAsync(request.ScrapId, new(InventoryStockScrapDecisionKindV1.Reject, "Kontrol"), metadata, Nested, token)
                    : scope.Store.RecordStockScrapAsync(request, metadata, Nested, token));
                Assert.Equal((503, InventoryStockScrapCodesV1.Unavailable), (error.Status, error.Code));
                Assert.False(called); Assert.Same(owned, scope.DbContext.Database.CurrentTransaction);
                Assert.Equal(entries, scope.DbContext.ChangeTracker.Entries().Count());
                Assert.Equal(pending, scope.AuditWriter.HasPendingDrafts);
            }
            var result = decision
                ? await scope.Store.DecideStockScrapAsync(request.ScrapId, new(InventoryStockScrapDecisionKindV1.Reject, "BaÄŸÄ±msÄ±z kontrol"), metadata, Fence)
                : await scope.Store.RecordStockScrapAsync(request, metadata, Fence);
            Assert.True(checkedEntry); Assert.False(result.IsReplay);
            Assert.False(scope.AuditWriter.HasPendingDrafts);
        }
    }

    private sealed class StockScrapSafetySaveProbe(Guid count) : SaveChangesInterceptor
    {
        public bool Saved; public bool Concurrency; public int SaveAttempts;
        private bool Matches(DbContext? context) => context is InventoryDbContext db
            && (db.ChangeTracker.Entries<StockScrapDecisionRow>().Any(row => row.Entity.ScrapId == count && row.State == EntityState.Added)
                || db.ChangeTracker.Entries<StockScrapRow>().Any(row => row.Entity.Id == count && row.State == EntityState.Added));
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (Matches(data.Context)) { SaveAttempts++; if (Concurrency) throw new DbUpdateConcurrencyException("SENTETÄ°K stock scrap save race."); }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (SaveAttempts > 0) Saved = true; return ValueTask.FromResult(result); }
    }
}

public sealed class InventoryStockScrapSafetyBoundaryTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Configured_nonrelational_entry_is_refused_without_consuming_caller_drafts(bool decision, bool pending)
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        await using var db = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase("SENTETÄ°K-scrap-entry-" + Guid.NewGuid()).Options, company);
        var writer = new StockScrapBoundaryWriter(pending);
        var store = new InventoryStore(db, company, new AuditEventCollector(), new InventoryKilogramScale(null!), TimeProvider.System,
            lossDecisionAuditWriter: writer);
        var called = false;
        Task Fence(CancellationToken _) { called = true; return Task.CompletedTask; }
        var metadata = new InventoryCommandMetadata("checker", "synthetic.test", [], "corr", null, "key", "Web");
        var error = await Assert.ThrowsAsync<InventoryStockScrapException>(() => decision
            ? store.DecideStockScrapAsync(Guid.NewGuid(), new(InventoryStockScrapDecisionKindV1.Reject, "DÃ¼zeltme gerekiyor."), metadata, Fence)
            : store.RecordStockScrapAsync(new(Guid.NewGuid(), Guid.NewGuid(), "1", "1", DateTimeOffset.UtcNow, "Hurda"), metadata, Fence));
        Assert.Equal((503, InventoryStockScrapCodesV1.Unavailable), (error.Status, error.Code));
        Assert.False(called); Assert.False(writer.Discarded); Assert.Equal(pending, writer.HasPendingDrafts);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private sealed class StockScrapBoundaryWriter(bool pending) : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts => pending;
        public bool Discarded;
        public Task WritePendingAsync(System.Data.IDbTransaction transaction, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Entry must not flush caller drafts.");
        public void DiscardPending() => Discarded = true;
    }
}
