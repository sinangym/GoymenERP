using System.Transactions;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    // Real native loss requests, decisions, stock movement and audit; authority refusal is controlled.
    [SqlServerFact]
    public Task Loss_approval_post_save_revocation_rolls_back_the_actual_writeoff_receipt_and_audit() => AssertLossSafetyRollback(true);
    [SqlServerFact]
    public Task Loss_rejection_post_save_revocation_rolls_back_the_actual_decision_receipt_and_audit() => AssertLossSafetyRollback(false);

    private static async Task AssertLossSafetyRollback(bool approve)
    {
        await using var run = await Run.StartAsync();
        var (plan, line) = await DispatchedLossTransferAsync(run, "SYN-LOSS-FINAL-FENCE");
        await LossRequestAsync(run, plan.TransferId, line, new("2", At.AddHours(2), LossRenewalExplanation), Metadata(run));
        var metadata = LossChecker(run);
        var body = LossSafetyBody(approve);
        var probe = new LossSafetySaveProbe(plan.TransferId);
        await using (var scope = run.Store(CompanyA, probe))
        {
            var refusal = await Assert.ThrowsAsync<InventoryTransferException>(() => scope.Store.DecideLossAsync(plan.TransferId, line, body, metadata,
                _ => probe.Saved ? Task.FromException(new InventoryTransferException(403, InventoryTransferCodesV1.Forbidden)) : Task.CompletedTask));
            Assert.Equal(403, refusal.Status); Assert.True(probe.Saved);
            Assert.Empty(scope.DbContext.ChangeTracker.Entries()); Assert.False(scope.AuditWriter.HasPendingDrafts);
            Assert.Null(scope.DbContext.Database.CurrentTransaction);
        }
        await AssertLossSafetyAbsent(run, plan.TransferId);
        var decided = await LossDecisionAsync(run, plan.TransferId, line, body, metadata);
        Assert.Equal(approve ? "Approved" : "Rejected", Assert.Single(decided.Facts.Losses).Status);
        Assert.Equal(approve ? "3" : "2", decided.Transfer.Version);
        var replay = await LossDecisionAsync(run, plan.TransferId, line, body, metadata);
        Assert.True(replay.IsReplay);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(decided),
            System.Text.Json.JsonSerializer.Serialize(replay with { IsReplay = false }));
    }

    [SqlServerFact]
    public async Task Loss_pre_save_revocation_discards_real_audit_drafts_and_same_context_retry_has_no_audit_ghost()
    {
        await using var run = await Run.StartAsync();
        var (plan, line) = await DispatchedLossTransferAsync(run, "SYN-LOSS-STAGED-FENCE");
        await LossRequestAsync(run, plan.TransferId, line, new("2", At.AddHours(2), LossRenewalExplanation), Metadata(run));
        await using var scope = run.Store(CompanyA); var metadata = LossChecker(run); var body = LossSafetyBody(true);
        await Assert.ThrowsAsync<InventoryTransferException>(() => scope.Store.DecideLossAsync(plan.TransferId, line, body, metadata,
            _ => scope.AuditWriter.HasPendingDrafts ? Task.FromException(new InventoryTransferException(403, InventoryTransferCodesV1.Forbidden)) : Task.CompletedTask));
        Assert.False(scope.AuditWriter.HasPendingDrafts); Assert.Empty(scope.DbContext.ChangeTracker.Entries());
        await AssertLossSafetyAbsent(run, plan.TransferId);
        var retry = await scope.Store.DecideLossAsync(plan.TransferId, line, body, metadata, Authorized);
        Assert.False(retry.IsReplay); Assert.False(scope.AuditWriter.HasPendingDrafts);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'inventory.transfer.loss-decided.v1' AND EntityId=@p", ("@p", plan.TransferId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Loss_save_concurrency_without_a_winner_uses_two_clean_boundaries_and_never_retries_the_mutation_or_writes_a_conflict_receipt()
    {
        await using var run = await Run.StartAsync();
        var (plan, line) = await DispatchedLossTransferAsync(run, "SYN-LOSS-SAVE-RACE");
        await LossRequestAsync(run, plan.TransferId, line, new("2", At.AddHours(2), LossRenewalExplanation), Metadata(run));
        var probe = new LossSafetySaveProbe(plan.TransferId) { Concurrency = true };
        await using var scope = run.Store(CompanyA, probe); var scopes = new HashSet<Guid>();
        Task Fence(CancellationToken _) { scopes.Add(scope.DbContext.Database.CurrentTransaction!.TransactionId); return Task.CompletedTask; }
        var refusal = await Assert.ThrowsAsync<InventoryTransferException>(() => scope.Store.DecideLossAsync(plan.TransferId, line,
            LossSafetyBody(true), LossChecker(run), Fence));
        Assert.Equal((409, InventoryTransferCodesV1.ConcurrentChange), (refusal.Status, refusal.Code));
        Assert.Equal(1, probe.SaveAttempts); Assert.Equal(2, scopes.Count);
        Assert.False(scope.AuditWriter.HasPendingDrafts); Assert.Empty(scope.DbContext.ChangeTracker.Entries());
        await AssertLossSafetyAbsent(run, plan.TransferId);
    }

    [SqlServerFact]
    public async Task Loss_save_race_reads_a_real_winning_native_receipt_only_after_the_failed_owner_has_rolled_back()
    {
        await using var run = await Run.StartAsync();
        var (plan, line) = await DispatchedLossTransferAsync(run, "SYN-LOSS-NATIVE-WINNER");
        await LossRequestAsync(run, plan.TransferId, line, new("2", At.AddHours(2), LossRenewalExplanation), Metadata(run));
        var probe = new LossSafetySaveProbe(plan.TransferId) { Concurrency = true };
        await using var scope = run.Store(CompanyA, probe);
        var metadata = LossChecker(run); var body = LossSafetyBody(true);
        Guid? firstTransaction = null; var winnerRecorded = false;
        async Task Fence(CancellationToken _)
        {
            var current = scope.DbContext.Database.CurrentTransaction!.TransactionId;
            firstTransaction ??= current;
            if (current != firstTransaction && !winnerRecorded)
            {
                // Controlled scheduling at the new read-only scope's first fence, after loser cleanup.
                // The winner is a real native command on another real context, not a seeded command row.
                Assert.Empty(scope.DbContext.ChangeTracker.Entries()); Assert.False(scope.AuditWriter.HasPendingDrafts);
                winnerRecorded = true;
                var winner = await LossDecisionAsync(run, plan.TransferId, line, body, metadata);
                Assert.False(winner.IsReplay);
            }
        }
        var answer = await scope.Store.DecideLossAsync(plan.TransferId, line, body, metadata, Fence);
        Assert.True(winnerRecorded); Assert.True(answer.IsReplay); Assert.Equal(1, probe.SaveAttempts);
        Assert.Equal("Approved", Assert.Single(answer.Facts.Losses).Status);
        Assert.False(scope.AuditWriter.HasPendingDrafts);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferLossDecisions WHERE TransferId=@p", ("@p", plan.TransferId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'inventory.transfer.loss-decided.v1' AND EntityId=@p", ("@p", plan.TransferId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Loss_matching_native_key_replay_is_refused_after_read_when_the_fresh_authority_is_lost()
    {
        await using var run = await Run.StartAsync();
        var (plan, line) = await DispatchedLossTransferAsync(run, "SYN-LOSS-REPLAY-FENCE");
        await LossRequestAsync(run, plan.TransferId, line, new("2", At.AddHours(2), LossRenewalExplanation), Metadata(run));
        var metadata = LossChecker(run); var body = LossSafetyBody(true);
        await LossDecisionAsync(run, plan.TransferId, line, body, metadata);
        await using var scope = run.Store(CompanyA); var fences = 0;
        await Assert.ThrowsAsync<InventoryTransferException>(() => scope.Store.ReplayDecideLossAsync(plan.TransferId, line, body, metadata,
            _ => ++fences == 3 ? Task.FromException(new InventoryTransferException(403, InventoryTransferCodesV1.Forbidden)) : Task.CompletedTask));
        Assert.Equal(3, fences); Assert.False(scope.AuditWriter.HasPendingDrafts);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferLossDecisions WHERE TransferId=@p", ("@p", plan.TransferId)));
    }

    [SqlServerFact]
    public async Task Loss_decision_and_replay_entry_refusals_preserve_actual_caller_transactions_changes_and_pending_audit()
    {
        await using var run = await Run.StartAsync();
        var (plan, line) = await DispatchedLossTransferAsync(run, "SYN-LOSS-ENTRY-FENCE");
        foreach (var replay in new[] { false, true })
        foreach (var boundary in new[] { "missing-writer", "borrowed", "dirty", "audit", "ambient" })
        {
            await using var scope = run.Store(CompanyA);
            var owner = boundary == "missing-writer"
                ? new InventoryStore(scope.DbContext, scope.CompanyContext, scope.AuditCollector, new InventoryKilogramScale(null!), TimeProvider.System)
                : scope.Store;
            await using var borrowed = boundary == "borrowed" ? await scope.DbContext.Database.BeginTransactionAsync() : null;
            var row = boundary == "dirty" ? await scope.DbContext.Transfers.SingleAsync(x => x.Id == plan.TransferId) : null;
            if (row is not null) row.UpdatedAtUtc = At.AddDays(1);
            if (boundary is "audit" or "missing-writer") scope.AuditCollector.Enqueue(LossSafetyCallerDraft(run));
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            var callerAmbient = Transaction.Current; var called = false;
            Task Fence(CancellationToken _) { called = true; return Task.CompletedTask; }
            async Task Invoke()
            {
                if (replay) await owner.ReplayDecideLossAsync(plan.TransferId, line, LossSafetyBody(true), LossChecker(run), Fence);
                else await owner.DecideLossAsync(plan.TransferId, line, LossSafetyBody(true), LossChecker(run), Fence);
            }
            if (boundary == "missing-writer") Assert.Equal(503, (await Assert.ThrowsAsync<InventoryTransferException>(Invoke)).Status);
            else await Assert.ThrowsAsync<InvalidOperationException>(Invoke);
            Assert.False(called);
            if (borrowed is not null) Assert.Same(borrowed, scope.DbContext.Database.CurrentTransaction);
            if (row is not null) Assert.Equal(EntityState.Modified, scope.DbContext.Entry(row).State);
            if (boundary is "audit" or "missing-writer") { Assert.True(scope.AuditWriter.HasPendingDrafts); scope.AuditWriter.DiscardPending(); }
            if (boundary == "ambient") Assert.Same(callerAmbient, Transaction.Current);
        }
    }

    [SqlServerFact]
    public async Task Loss_existing_real_audit_writer_flushes_unattached_drafts_in_the_owned_transaction_before_commit()
    {
        await using var run = await Run.StartAsync();
        var (plan, line) = await DispatchedLossTransferAsync(run, "SYN-LOSS-AUDIT-FALLBACK");
        await LossRequestAsync(run, plan.TransferId, line, new("2", At.AddHours(2), LossRenewalExplanation), Metadata(run));
        await using var scope = run.Store(CompanyA); await using var raw = run.Context(CompanyA);
        var owner = new InventoryStore(raw, scope.CompanyContext, scope.AuditCollector, new InventoryKilogramScale(null!), TimeProvider.System,
            lossDecisionAuditWriter: scope.AuditWriter);
        var result = await owner.DecideLossAsync(plan.TransferId, line, LossSafetyBody(false), LossChecker(run), Authorized);
        Assert.Equal("Rejected", Assert.Single(result.Facts.Losses).Status); Assert.False(scope.AuditWriter.HasPendingDrafts);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'inventory.transfer.loss-decided.v1' AND EntityId=@p", ("@p", plan.TransferId.ToString("D"))));
    }

    private static DecideInventoryTransferLossV1 LossSafetyBody(bool approve) => new("2",
        approve ? InventoryTransferDecisionKindV1.Approve : InventoryTransferDecisionKindV1.Reject,
        At.AddHours(3), "SYNTHETIC native loss decision", 1);
    private static async Task AssertLossSafetyAbsent(Run run, Guid transferId)
    {
        await using var db = run.Context(CompanyA);
        Assert.Equal(2L, (await db.Transfers.SingleAsync(x => x.Id == transferId)).Version);
        Assert.Empty(await db.TransferLossDecisions.Where(x => x.TransferId == transferId).ToListAsync());
        Assert.Empty(await db.Commands.Where(x => x.CommandName == InventoryStore.DecideTransferLossCommand).ToListAsync());
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'inventory.transfer.loss-decided.v1' AND EntityId=@p", ("@p", transferId.ToString("D"))));
    }
    private static AuditEventDraft LossSafetyCallerDraft(Run run) => new(CompanyA, "inventory.transfer.loss-decided.v1",
        "Inventory.Transfer", Guid.NewGuid().ToString("D"), "DecideLoss", 2, 3, AuditActorTypes.User, run.Actor.ToString("D"),
        "synthetic.inventory.test-write", ["SYNTHETIC-INVENTORY-TEST"], "{}", "{}", "Caller-owned draft", null,
        AuditAiInvolvement.None, null, "Web", null, null, run.Correlation, null, "caller-owned");
    private sealed class LossSafetySaveProbe(Guid transferId) : SaveChangesInterceptor
    {
        public bool Saved; public bool Concurrency; public int SaveAttempts;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is InventoryDbContext db && db.ChangeTracker.Entries<InventoryTransferLossDecisionRow>().Any(x => x.Entity.TransferId == transferId))
            { SaveAttempts++; if (Concurrency) throw new DbUpdateConcurrencyException("Synthetic first-save concurrency."); }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (data.Context is InventoryDbContext db && db.ChangeTracker.Entries<InventoryTransferLossDecisionRow>().Any(x => x.Entity.TransferId == transferId)) Saved = true;
          return ValueTask.FromResult(result); }
    }
}
