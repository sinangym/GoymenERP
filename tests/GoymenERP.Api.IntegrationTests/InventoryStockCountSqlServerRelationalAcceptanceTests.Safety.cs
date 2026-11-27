using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Stock_count_record_and_decision_entry_refusals_preserve_caller_transaction_changes_and_audit_even_for_replay()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-COUNT-SAFETY-ENTRY", 100m);
        var request = Count(source.LabelId, source.LabelVersion, "101");
        var record = Metadata(run);
        var checker = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };
        var decision = new DecideInventoryStockCountV1(InventoryStockCountDecisionKindV1.Approve, "İkinci kişi doğruladı.");
        await using (var seed = run.Store(CompanyA))
        {
            await seed.Store.RecordStockCountAsync(request, record, static _ => Task.CompletedTask);
            await seed.Store.DecideStockCountAsync(request.CountId, decision, checker, static _ => Task.CompletedTask);
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
            var error = await Assert.ThrowsAsync<InventoryStockCountException>(() => isDecision
                ? store.DecideStockCountAsync(replay ? request.CountId : Guid.NewGuid(), decision, checker, Fence)
                : store.RecordStockCountAsync(replay ? request : request with { CountId = Guid.NewGuid() }, record, Fence));
            Assert.Equal((503, InventoryStockCountCodesV1.Unavailable), (error.Status, error.Code));
            Assert.False(called); Assert.Same(borrowed, scope.DbContext.Database.CurrentTransaction);
            Assert.Equal(boundary == "dirty", scope.DbContext.ChangeTracker.HasChanges());
            if (dirty is not null) Assert.Same(dirty, Assert.Single(scope.DbContext.ChangeTracker.Entries()).Entity);
            Assert.Equal(boundary is "audit" or "missing-writer", scope.AuditWriter.HasPendingDrafts);
            if (boundary is "audit" or "missing-writer") scope.AuditWriter.DiscardPending();
        }
    }

    [SqlServerFact]
    public async Task Stock_count_before_save_and_post_save_authority_or_company_loss_rolls_back_then_same_key_succeeds()
    {
        await using var run = await Run.StartAsync();
        foreach (var operation in new[] { "record", "approve", "reject" })
        foreach (var phase in new[] { "before-save", "after-save", "company-after-save" })
        {
            var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-COUNT-SAFETY-" + Guid.NewGuid().ToString("N"), 100m);
            var request = Count(source.LabelId, source.LabelVersion, "101");
            if (operation != "record")
                await using (var seed = run.Store(CompanyA))
                    await seed.Store.RecordStockCountAsync(request, Metadata(run), static _ => Task.CompletedTask);
            var before = await CountEffectsAsync(run, source.LabelId);
            var probe = new StockCountSafetySaveProbe(request.CountId);
            await using var scope = run.Store(CompanyA, probe);
            var metadata = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };
            var deny = true;
            Task Fence(CancellationToken _)
            {
                if (deny && (phase == "before-save" ? scope.AuditWriter.HasPendingDrafts : probe.Saved))
                {
                    if (phase == "company-after-save") scope.CompanyContext.Set(CompanyB);
                    else throw new InventoryStockCountException(403, InventoryStockCountCodesV1.Forbidden);
                }
                return Task.CompletedTask;
            }
            Task<InventoryStockCountResultV1> Invoke() => operation == "record"
                ? scope.Store.RecordStockCountAsync(request, metadata, Fence)
                : scope.Store.DecideStockCountAsync(request.CountId,
                    new(operation == "approve" ? InventoryStockCountDecisionKindV1.Approve : InventoryStockCountDecisionKindV1.Reject, "Sayım kararı doğrulandı."), metadata, Fence);
            var error = await Assert.ThrowsAsync<InventoryStockCountException>(() => Invoke());
            Assert.Equal((403, InventoryStockCountCodesV1.Forbidden), (error.Status, error.Code));
            Assert.Equal(phase != "before-save", probe.Saved);
            Assert.Empty(scope.DbContext.ChangeTracker.Entries()); Assert.False(scope.AuditWriter.HasPendingDrafts);
            Assert.Null(scope.DbContext.Database.CurrentTransaction);
            scope.CompanyContext.Set(CompanyA);
            Assert.Equal(before, await CountEffectsAsync(run, source.LabelId));
            deny = false;
            var result = await Invoke(); Assert.False(result.IsReplay);
            var committed = await CountEffectsAsync(run, source.LabelId);
            var replay = await Invoke(); Assert.True(replay.IsReplay);
            Assert.Equal(result.OriginalCorrelationId, replay.OriginalCorrelationId); Assert.Equal(result.Count, replay.Count);
            Assert.Equal(committed, await CountEffectsAsync(run, source.LabelId));
        }
    }

    [SqlServerFact]
    public async Task Stock_count_without_audit_interceptor_flushes_real_scoped_writer_in_owned_transaction_and_rolls_back_late_loss()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-COUNT-SAFETY-WRITER", 100m);
        var request = Count(source.LabelId, source.LabelVersion, "101");
        var record = Metadata(run);
        await using (var noInterceptor = StockCountSafetyStoreWithoutInterceptor(run))
        {
            await noInterceptor.Store.RecordStockCountAsync(request, record, static _ => Task.CompletedTask);
            Assert.False(noInterceptor.AuditWriter.HasPendingDrafts);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'inventory.stock-count.recorded.v1' AND EntityId=@p", ("@p", request.CountId.ToString("D"))));
        var before = await CountEffectsAsync(run, source.LabelId);
        var probe = new StockCountSafetySaveProbe(request.CountId);
        await using var scope = StockCountSafetyStoreWithoutInterceptor(run, probe);
        var checker = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };
        var decision = new DecideInventoryStockCountV1(InventoryStockCountDecisionKindV1.Approve, "Gerçek audit writer ile doğrulandı.");
        var deny = true; var observedFlush = false;
        Task Fence(CancellationToken _)
        {
            if (deny && probe.Saved && !scope.AuditWriter.HasPendingDrafts)
            { observedFlush = true; throw new InventoryStockCountException(403, InventoryStockCountCodesV1.Forbidden); }
            return Task.CompletedTask;
        }
        Assert.Equal(403, (await Assert.ThrowsAsync<InventoryStockCountException>(() => scope.Store.DecideStockCountAsync(request.CountId, decision, checker, Fence))).Status);
        Assert.True(observedFlush); Assert.Equal(before, await CountEffectsAsync(run, source.LabelId));
        Assert.Empty(scope.DbContext.ChangeTracker.Entries()); Assert.False(scope.AuditWriter.HasPendingDrafts);
        deny = false;
        await scope.Store.DecideStockCountAsync(request.CountId, decision, checker, Fence);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'inventory.stock-count.approved.v1' AND EntityId=@p", ("@p", request.CountId.ToString("D"))));
        Assert.False(scope.AuditWriter.HasPendingDrafts);
    }

    [SqlServerFact]
    public async Task Stock_count_save_race_aborts_before_one_bounded_winner_read_without_mutation_retry_or_conflict_receipt()
    {
        await using var run = await Run.StartAsync();
        foreach (var operation in new[] { "record", "approve", "reject" })
        {
            var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-COUNT-SAFETY-RACE-" + Guid.NewGuid().ToString("N"), 100m);
            var request = Count(source.LabelId, source.LabelVersion, "101");
            if (operation != "record")
                await using (var seed = run.Store(CompanyA))
                    await seed.Store.RecordStockCountAsync(request, Metadata(run), static _ => Task.CompletedTask);
            var baseline = await CountEffectsAsync(run, source.LabelId);
            var probe = new StockCountSafetySaveProbe(request.CountId) { Concurrency = true };
            await using var scope = run.Store(CompanyA, probe);
            var metadata = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };
            var decision = new DecideInventoryStockCountV1(operation == "approve" ? InventoryStockCountDecisionKindV1.Approve : InventoryStockCountDecisionKindV1.Reject, "Yarış kararı.");
            Task<InventoryStockCountResultV1> Invoke(InventoryStore store, Func<CancellationToken, Task> fence) => operation == "record"
                ? store.RecordStockCountAsync(request, metadata, fence)
                : store.DecideStockCountAsync(request.CountId, decision, metadata, fence);
            var transactions = new HashSet<Guid>();
            Task CountTransactions(CancellationToken _) { transactions.Add(scope.DbContext.Database.CurrentTransaction!.TransactionId); return Task.CompletedTask; }
            var absent = await Assert.ThrowsAsync<InventoryStockCountException>(() => Invoke(scope.Store, CountTransactions));
            Assert.Equal((409, InventoryStockCountCodesV1.ConcurrentChange), (absent.Status, absent.Code));
            Assert.Equal(2, transactions.Count); Assert.Equal(1, probe.SaveAttempts);
            Assert.Equal(baseline, await CountEffectsAsync(run, source.LabelId));
            Assert.Empty(scope.DbContext.ChangeTracker.Entries()); Assert.False(scope.AuditWriter.HasPendingDrafts);
            Guid? first = null; InventoryStockCountResultV1? winner = null;
            async Task PublishWinner(CancellationToken _)
            {
                var current = scope.DbContext.Database.CurrentTransaction!.TransactionId;
                first ??= current;
                if (current != first && winner is null)
                {
                    Assert.Empty(scope.DbContext.ChangeTracker.Entries()); Assert.False(scope.AuditWriter.HasPendingDrafts);
                    await using var actual = run.Store(CompanyA);
                    var winnerMetadata = metadata with { CorrelationId = "stock-count-winner-" + Guid.NewGuid().ToString("N") };
                    winner = operation == "record"
                        ? await actual.Store.RecordStockCountAsync(request, winnerMetadata, static _ => Task.CompletedTask)
                        : await actual.Store.DecideStockCountAsync(request.CountId, decision, winnerMetadata, static _ => Task.CompletedTask);
                    Assert.False(winner.IsReplay);
                }
            }
            var replay = await Invoke(scope.Store, PublishWinner);
            Assert.NotNull(winner); Assert.True(replay.IsReplay); Assert.Equal(winner.Count, replay.Count);
            Assert.Equal(winner.OriginalCorrelationId, replay.OriginalCorrelationId);
            Assert.NotEqual(metadata.CorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(2, probe.SaveAttempts); // one failed save per outer call; second transactions only read.
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands WHERE CommandName=@c AND TargetId=@p", ("@c", operation == "record" ? InventoryStore.RecordStockCountCommand : InventoryStore.DecideStockCountCommand), ("@p", request.CountId)));
        }
    }

    [SqlServerFact]
    public async Task Stock_count_old_key_replays_immutable_evidence_after_a_later_count_changes_label_version_but_still_requires_live_authority()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-COUNT-SAFETY-IMMUTABLE", 100m);
        var request = Count(source.LabelId, source.LabelVersion, "101");
        var recorder = Metadata(run);
        var checker = Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };
        var decision = new DecideInventoryStockCountV1(InventoryStockCountDecisionKindV1.Approve, "İlk sayım doğrulandı.");
        InventoryStockCountResultV1 originalRecord; InventoryStockCountResultV1 originalDecision;
        await using (var scope = run.Store(CompanyA))
        {
            originalRecord = await scope.Store.RecordStockCountAsync(request, recorder, static _ => Task.CompletedTask);
            originalDecision = await scope.Store.DecideStockCountAsync(request.CountId, decision, checker, static _ => Task.CompletedTask);
        }
        var later = Count(source.LabelId, await LabelVersionAsync(run, source.LabelId), "102");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.RecordStockCountAsync(later, Metadata(run), static _ => Task.CompletedTask);
            await scope.Store.DecideStockCountAsync(later.CountId,
                new(InventoryStockCountDecisionKindV1.Approve, "Sonraki sayım doğrulandı."),
                Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") }, static _ => Task.CompletedTask);
        }
        var committed = await CountEffectsAsync(run, source.LabelId);
        await using var replayScope = run.Store(CompanyA);
        var recordReplay = await replayScope.Store.RecordStockCountAsync(request, recorder, static _ => Task.CompletedTask);
        var decisionReplay = await replayScope.Store.DecideStockCountAsync(request.CountId, decision, checker, static _ => Task.CompletedTask);
        Assert.Equal(originalRecord with { IsReplay = true }, recordReplay);
        Assert.Equal(originalDecision with { IsReplay = true }, decisionReplay);
        Task Deny(CancellationToken _) => throw new InventoryStockCountException(403, InventoryStockCountCodesV1.Forbidden);
        Assert.Equal(403, (await Assert.ThrowsAsync<InventoryStockCountException>(() => replayScope.Store.RecordStockCountAsync(request, recorder, Deny))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<InventoryStockCountException>(() => replayScope.Store.DecideStockCountAsync(request.CountId, decision, checker, Deny))).Status);
        Assert.Equal(committed, await CountEffectsAsync(run, source.LabelId));
        Assert.Empty(replayScope.DbContext.ChangeTracker.Entries()); Assert.False(replayScope.AuditWriter.HasPendingDrafts);
    }

    private static StoreScope StockCountSafetyStoreWithoutInterceptor(Run run, params IInterceptor[] interceptors)
    {
        var auditScope = run.Factory.Services.CreateScope();
        var company = auditScope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var collector = auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>();
        var writer = auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        var db = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema))
            .AddInterceptors(interceptors).Options, company);
        var settings = new ApprovedSettingsPrerequisiteProvider(company, new FixedCurrentUser(run.Actor, CompanyA));
        return new(db, new InventoryStore(db, company, collector, new InventoryKilogramScale(settings), TimeProvider.System,
            lossDecisionAuditWriter: writer), auditScope);
    }

    private sealed class StockCountSafetySaveProbe(Guid count) : SaveChangesInterceptor
    {
        public bool Saved; public bool Concurrency; public int SaveAttempts;
        private bool Matches(DbContext? context) => context is InventoryDbContext db
            && (db.ChangeTracker.Entries<StockCountDecisionRow>().Any(row => row.Entity.CountId == count && row.State == EntityState.Added)
                || db.ChangeTracker.Entries<StockCountRow>().Any(row => row.Entity.Id == count && row.State == EntityState.Added));
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (Matches(data.Context)) { SaveAttempts++; if (Concurrency) throw new DbUpdateConcurrencyException("SENTETİK stock count save race."); }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (SaveAttempts > 0) Saved = true; return ValueTask.FromResult(result); }
    }
}

public sealed class InventoryStockCountSafetyBoundaryTests
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
            .UseInMemoryDatabase("SENTETİK-count-entry-" + Guid.NewGuid()).Options, company);
        var writer = new StockCountBoundaryWriter(pending);
        var store = new InventoryStore(db, company, new AuditEventCollector(), new InventoryKilogramScale(null!), TimeProvider.System,
            lossDecisionAuditWriter: writer);
        var called = false;
        Task Fence(CancellationToken _) { called = true; return Task.CompletedTask; }
        var metadata = new InventoryCommandMetadata("checker", "synthetic.test", [], "corr", null, "key", "Web");
        var error = await Assert.ThrowsAsync<InventoryStockCountException>(() => decision
            ? store.DecideStockCountAsync(Guid.NewGuid(), new(InventoryStockCountDecisionKindV1.Reject, "Düzeltme gerekiyor."), metadata, Fence)
            : store.RecordStockCountAsync(new(Guid.NewGuid(), Guid.NewGuid(), "1", "1", DateTimeOffset.UtcNow, "Sayım"), metadata, Fence));
        Assert.Equal((503, InventoryStockCountCodesV1.Unavailable), (error.Status, error.Code));
        Assert.False(called); Assert.False(writer.Discarded); Assert.Equal(pending, writer.HasPendingDrafts);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private sealed class StockCountBoundaryWriter(bool pending) : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts => pending;
        public bool Discarded;
        public Task WritePendingAsync(System.Data.IDbTransaction transaction, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Entry must not flush caller drafts.");
        public void DiscardPending() => Discarded = true;
    }
}
