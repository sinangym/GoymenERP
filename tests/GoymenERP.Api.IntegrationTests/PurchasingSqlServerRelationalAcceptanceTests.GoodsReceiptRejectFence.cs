using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.GoodsReceipts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Native_receipt_reject_refuses_authority_loss_after_label_reads_before_staging()
    {
        await using var run = await Run.StartAsync(); var seed = await BatchRejectSeedAsync(run);
        var before = await BatchRejectCountsAsync(run, seed.BatchId);
        await using var writer = BatchRejectScopeFor(run, CompanyA);
        var checks = 0;
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => writer.Store.ExecuteRejectionGuardedAsync(
            token => SaveBatchRejectAsync(run, writer.Store, seed, "batch-last-read", token), _ =>
                ++checks == 2 ? throw new PurchasingServiceException(403, "forbidden") : Task.CompletedTask));
        Assert.Equal((403, 2), (denied.Status, checks)); // Entry, then completed row/label restore before any decision mutation.
        Assert.Empty(writer.Database.ChangeTracker.Entries()); Assert.False(writer.AuditWriter.HasPendingDrafts);
        Assert.Equal(before, await BatchRejectCountsAsync(run, seed.BatchId));
    }

    [SqlServerFact]
    public async Task Native_receipt_reject_company_drift_after_save_rolls_back_the_origin_company_record()
    {
        await using var run = await Run.StartAsync(); var seed = await BatchRejectSeedAsync(run);
        var before = await BatchRejectCountsAsync(run, seed.BatchId);
        BatchRejectScope? writer = null;
        var saved = new RequestDecisionSavedInterceptor(() => writer!.Company.Set(CompanyB));
        await using (writer = BatchRejectScopeFor(run, CompanyA, saved))
        {
            var denied = await Assert.ThrowsAsync<PurchasingStoreException>(() => writer.Store.ExecuteRejectionGuardedAsync(
                token => SaveBatchRejectAsync(run, writer.Store, seed, "batch-company-drift", token), _ => Task.CompletedTask));
            Assert.Equal(PurchasingStoreCodes.CompanyContextMismatch, denied.Code);
            Assert.Empty(writer.Database.ChangeTracker.Entries()); Assert.False(writer.AuditWriter.HasPendingDrafts);
        }
        Assert.Equal(before, await BatchRejectCountsAsync(run, seed.BatchId));
    }

    [SqlServerFact]
    public async Task Native_receipt_reject_post_save_delegation_fence_rolls_back_labels_attempt_and_audit_then_same_key_retries()
    {
        await using var run = await Run.StartAsync();
        var seed = await BatchRejectSeedAsync(run);
        var before = await BatchRejectCountsAsync(run, seed.BatchId);
        var allowed = true;
        var execution = new WorkDecisionExecutionFenceV1();
        // Synthetic eligibility callback; SQL below proves the native owner transaction, not real delegation assignment.
        using var lease = execution.Enter(_ => Task.FromResult(allowed));
        var authority = new WorkDecisionAuthorityV1(new RequestDecisionAuthority(), execution);
        async Task Fence(CancellationToken token)
        {
            var scope = await authority.RevalidatePermissionScopeAsync(PurchasingPermissionManifestV1.GoodsReceiptsApproveVariance, CompanyA, token);
            if (!scope.IsGranted) throw new PurchasingServiceException(403, "forbidden");
        }
        var saved = new RequestDecisionSavedInterceptor(() => allowed = false);
        await using (var writer = BatchRejectScopeFor(run, CompanyA, saved))
        {
            var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => writer.Store.ExecuteRejectionGuardedAsync(
                token => SaveBatchRejectAsync(run, writer.Store, seed, "batch-guarded", token), Fence));
            Assert.Equal(403, denied.Status); Assert.Equal(1, saved.Calls);
            Assert.Empty(writer.Database.ChangeTracker.Entries()); Assert.False(writer.AuditWriter.HasPendingDrafts);
        }
        Assert.Equal(before, await BatchRejectCountsAsync(run, seed.BatchId));
        allowed = true;
        await using (var retry = BatchRejectScopeFor(run, CompanyA))
        {
            var answer = await retry.Store.ExecuteRejectionGuardedAsync(
                token => SaveBatchRejectAsync(run, retry.Store, seed, "batch-guarded", token), Fence);
            Assert.Equal((2L, PurchaseGoodsReceiptBatchStatus.Rejected), (answer.Version, answer.Batch.Status));
        }
        Assert.Equal((2L, before.Attempts + 1, before.Audits + 1, 1), await BatchRejectCountsAsync(run, seed.BatchId));
        await using var foreign = BatchRejectScopeFor(run, CompanyB);
        Assert.Null(await foreign.Store.FindAsync(seed.PurchaseOrderId, seed.BatchId));
        Assert.Null(await foreign.Store.FindReplayAsync(run.Envelope("batch-guarded", actor: OrderApprover), Hash("batch-guarded"), PurchaseGoodsReceiptBatchCommandsV1.RejectV1));
    }

    [SqlServerFact]
    public async Task Native_receipt_reject_fence_after_actual_audit_append_rolls_back_and_cleans_owned_state()
    {
        await using var run = await Run.StartAsync(); var seed = await BatchRejectSeedAsync(run);
        var before = await BatchRejectCountsAsync(run, seed.BatchId);
        var allowed = true; var appends = 0;
        // No Audit interceptor: the real Audit owner appends the queued event in the rejection's SQL transaction.
        await using var writer = BatchRejectScopeFor(run, CompanyA, afterAudit: () => { appends++; allowed = false; });
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => writer.Store.ExecuteRejectionGuardedAsync(
            token => SaveBatchRejectAsync(run, writer.Store, seed, "batch-audit-fence", token),
            _ => allowed ? Task.CompletedTask : throw new PurchasingServiceException(403, "forbidden")));
        Assert.Equal(403, denied.Status); Assert.Equal(1, appends);
        Assert.Empty(writer.Database.ChangeTracker.Entries()); Assert.False(writer.AuditWriter.HasPendingDrafts);
        Assert.Equal(before, await BatchRejectCountsAsync(run, seed.BatchId));
    }

    [SqlServerFact]
    public async Task Native_receipt_reject_replay_keeps_original_correlation_and_refuses_permission_loss_after_its_final_read()
    {
        await using var run = await Run.StartAsync(); var seed = await BatchRejectSeedAsync(run);
        await using (var writer = BatchRejectScopeFor(run, CompanyA))
            await writer.Store.ExecuteRejectionGuardedAsync(token => SaveBatchRejectAsync(run, writer.Store, seed, "batch-replay", token), _ => Task.CompletedTask);
        var before = await BatchRejectCountsAsync(run, seed.BatchId);
        await using var reader = BatchRejectScopeFor(run, CompanyA);
        var checks = 0;
        var replay = await reader.Store.ExecuteRejectionGuardedAsync(async token =>
        {
            var receipt = await reader.Store.FindReplayAsync(run.Envelope("batch-replay", "SENTETIK-later", OrderApprover),
                Hash("batch-replay"), PurchaseGoodsReceiptBatchCommandsV1.RejectV1, token);
            Assert.NotNull(await reader.Store.FindAsync(seed.PurchaseOrderId, receipt!.BatchId!.Value, token));
            return receipt;
        }, _ => { checks++; return Task.CompletedTask; });
        Assert.Equal(run.Correlation, replay!.OriginalCorrelationId); Assert.Equal(3, checks);
        var allowed = true;
        await Assert.ThrowsAsync<PurchasingServiceException>(() => reader.Store.ExecuteRejectionGuardedAsync(async token =>
        {
            var receipt = await reader.Store.FindReplayAsync(run.Envelope("batch-replay", actor: OrderApprover), Hash("batch-replay"), PurchaseGoodsReceiptBatchCommandsV1.RejectV1, token);
            await reader.Store.FindAsync(seed.PurchaseOrderId, receipt!.BatchId!.Value, token);
            allowed = false; return receipt;
        }, _ => allowed ? Task.CompletedTask : throw new PurchasingServiceException(403, "forbidden")));
        Assert.Empty(reader.Database.ChangeTracker.Entries());
        Assert.Equal(before, await BatchRejectCountsAsync(run, seed.BatchId));
    }

    [SqlServerFact]
    public async Task Native_receipt_reject_entry_preserves_dirty_pending_ambient_and_borrowed_caller_state_before_callbacks()
    {
        await using var run = await Run.StartAsync(); var seed = await BatchRejectSeedAsync(run);
        await using (var writer = BatchRejectScopeFor(run, CompanyA))
            await writer.Store.ExecuteRejectionGuardedAsync(token => SaveBatchRejectAsync(run, writer.Store, seed, "batch-recorded", token), _ => Task.CompletedTask);
        var before = await BatchRejectCountsAsync(run, seed.BatchId);
        foreach (var mode in new[] { "dirty", "pending", "ambient", "borrowed" })
        {
            await using var caller = BatchRejectScopeFor(run, CompanyA);
            var tracked = await caller.Database.GoodsReceiptBatches.SingleAsync(row => row.BatchId == seed.BatchId);
            var original = tracked.DecisionReason;
            if (mode == "dirty") tracked.DecisionReason = "SENTETIK caller edit";
            if (mode == "pending") caller.Collector.Enqueue(OrderFencePendingAudit(run, seed.BatchId));
            using var ambient = mode == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
            await using var borrowed = mode == "borrowed" ? await caller.Database.Database.BeginTransactionAsync() : null;
            var callbacks = 0;
            // Entry guards precede even new-key and recorded-key replay reads.
            foreach (var key in new[] { "batch-new", "batch-recorded" })
                await Assert.ThrowsAsync<InvalidOperationException>(() => caller.Store.ExecuteRejectionGuardedAsync(async token =>
                { callbacks++; return await caller.Store.FindReplayAsync(run.Envelope(key, actor: OrderApprover), Hash(key), PurchaseGoodsReceiptBatchCommandsV1.RejectV1, token); },
                    _ => { callbacks++; return Task.CompletedTask; }));
            Assert.Equal(0, callbacks);
            Assert.Same(tracked, Assert.Single(caller.Database.ChangeTracker.Entries()).Entity);
            Assert.Equal(mode == "dirty" ? EntityState.Modified : EntityState.Unchanged, caller.Database.Entry(tracked).State);
            Assert.Equal(mode == "dirty" ? "SENTETIK caller edit" : original, tracked.DecisionReason);
            Assert.Equal(original, caller.Database.Entry(tracked).Property(row => row.DecisionReason).OriginalValue);
            Assert.Equal(mode == "pending", caller.AuditWriter.HasPendingDrafts);
            if (borrowed is not null) { Assert.Same(borrowed, caller.Database.Database.CurrentTransaction); await borrowed.RollbackAsync(); }
            if (ambient is not null) Assert.Equal(System.Transactions.TransactionStatus.Active, System.Transactions.Transaction.Current!.TransactionInformation.Status);
            caller.AuditWriter.DiscardPending(); // Only the caller cleans its own rejected entry state.
        }
        Assert.Equal(before, await BatchRejectCountsAsync(run, seed.BatchId));
    }

    [SqlServerFact]
    public async Task Native_receipt_reject_owned_abort_discards_its_post_save_audit_and_permission_drift_cannot_commit_it()
    {
        await using var run = await Run.StartAsync(); var seed = await BatchRejectSeedAsync(run);
        var before = await BatchRejectCountsAsync(run, seed.BatchId);
        BatchRejectScope? writer = null;
        var saved = new RequestDecisionSavedInterceptor(() => writer!.Collector.Enqueue(OrderFencePendingAudit(run, seed.BatchId)));
        await using (writer = BatchRejectScopeFor(run, CompanyA, saved))
        {
            await Assert.ThrowsAsync<PurchasingServiceException>(() => writer.Store.ExecuteRejectionGuardedAsync(
                token => SaveBatchRejectAsync(run, writer.Store, seed, "batch-own-draft", token), _ =>
                {
                    if (saved.Calls > 0) { Assert.True(writer.AuditWriter.HasPendingDrafts); throw new PurchasingServiceException(403, "forbidden"); }
                    return Task.CompletedTask;
                }));
            Assert.False(writer.AuditWriter.HasPendingDrafts); Assert.Empty(writer.Database.ChangeTracker.Entries());
        }
        Assert.Equal(before, await BatchRejectCountsAsync(run, seed.BatchId));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=N'purchasing.synthetic-caller.v1'", ("@id", seed.BatchId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Native_receipt_reject_missing_audit_writer_is_unavailable_without_running_or_clearing_the_caller()
    {
        await using var run = await Run.StartAsync(); var seed = await BatchRejectSeedAsync(run);
        await using var caller = BatchRejectScopeFor(run, CompanyA);
        var tracked = await caller.Database.GoodsReceiptBatches.SingleAsync(row => row.BatchId == seed.BatchId);
        var legacy = new PurchaseGoodsReceiptBatchStore(caller.Database, caller.Company, caller.Collector, TimeProvider.System);
        var calls = 0;
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => legacy.ExecuteRejectionGuardedAsync(
            _ => { calls++; return Task.FromResult(1); }, _ => { calls++; return Task.CompletedTask; }));
        Assert.Equal((503, PurchaseGoodsReceiptBatchCodesV1.Unavailable), (denied.Status, denied.Code)); Assert.Equal(0, calls);
        Assert.Same(tracked, Assert.Single(caller.Database.ChangeTracker.Entries()).Entity);
    }

    private static async Task<PurchaseGoodsReceiptBatchDraft> BatchRejectSeedAsync(Run run)
    {
        var owners = Owners.For(CompanyA); var order = Order(owners, null, null, 10m);
        await using (var writer = run.Store(CompanyA))
            await writer.Store.StoreOrderAsync(run.Envelope($"batch-order-{order.Id:N}"), Hash("batch-order"), owners.Account, Day, () => order, VersionsOf(order));
        var draft = new PurchaseGoodsReceiptBatchDraft(Guid.NewGuid(), order.Id, order.Lines[0].Id, Product, "7", KgUnit,
            Guid.NewGuid(), T0, PurchaseGoodsReceiptBatchStatus.PendingApproval, 10m, 11m, GoodsReceiptKgVarianceOutcome.ToleranceNotSet, null, false,
            [new(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), $"SENTETIK-{Guid.NewGuid():N}", GoodsReceiptLabelCodeSource.Entered, null, null, 11m, null, null)]);
        await using var seed = BatchRejectScopeFor(run, CompanyA, attachAuditInterceptor: true);
        await seed.Store.SaveNewAsync(draft, run.Envelope($"batch-seed-{draft.BatchId:N}"), Hash("batch-seed"), PurchaseGoodsReceiptBatchCommandsV1.SubmitV1);
        return draft;
    }

    private static Task<StoredPurchaseGoodsReceiptBatch> SaveBatchRejectAsync(Run run, PurchaseGoodsReceiptBatchStore store,
        PurchaseGoodsReceiptBatchDraft seed, string key, CancellationToken token)
    {
        var decision = new PurchaseGoodsReceiptBatch(seed.BatchId, PurchaseGoodsReceiptBatchStatus.PendingApproval, 1, run.Actor);
        decision.Reject(1, OrderApprover, "SENTETIK gerekçe");
        return store.SaveDecisionAsync(seed.PurchaseOrderId, decision, 1, run.Envelope(key, actor: OrderApprover), Hash(key), PurchaseGoodsReceiptBatchCommandsV1.RejectV1, token);
    }

    private static async Task<(long Version, int Attempts, int Audits, int Released)> BatchRejectCountsAsync(Run run, Guid id) =>
        (await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseGoodsReceiptBatches WHERE BatchId=@id", ("@id", id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseGoodsReceiptBatchAttempts WHERE BatchId=@id", ("@id", id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Purchasing.GoodsReceiptBatch' AND EntityId=@id", ("@id", id.ToString("D"))),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseGoodsReceiptBatchLabels WHERE BatchId=@id AND Released=1", ("@id", id)));

    private static BatchRejectScope BatchRejectScopeFor(Run run, Guid companyId, IInterceptor? extra = null, Action? afterAudit = null, bool attachAuditInterceptor = false)
    {
        var scope = run.Factory.Services.CreateAsyncScope();
        var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(companyId);
        var collector = scope.ServiceProvider.GetRequiredService<AuditEventCollector>();
        var nativeWriter = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        IAuditTransactionWriterV1 writer = afterAudit is null ? nativeWriter : new ServiceDecisionAuditWriter(nativeWriter, afterAudit);
        var options = new DbContextOptionsBuilder<PurchasingDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema));
        if (attachAuditInterceptor) options.AddInterceptors(new AuditSaveChangesInterceptor(collector, company));
        if (extra is not null) options.AddInterceptors(extra);
        var context = new PurchasingDbContext(options.Options, company);
        return new(scope, context, company, collector, writer, new(context, company, collector, TimeProvider.System, writer));
    }

    private sealed class BatchRejectScope(AsyncServiceScope scope, PurchasingDbContext database, ICompanyContext company,
        IAuditEventCollector collector, IAuditTransactionWriterV1 writer, PurchaseGoodsReceiptBatchStore store) : IAsyncDisposable
    {
        public PurchasingDbContext Database => database;
        public ICompanyContext Company => company;
        public IAuditEventCollector Collector => collector;
        public IAuditTransactionWriterV1 AuditWriter => writer;
        public PurchaseGoodsReceiptBatchStore Store => store;
        public async ValueTask DisposeAsync() { await database.DisposeAsync(); await scope.DisposeAsync(); }
    }
}
