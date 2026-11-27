using System.Data.Common;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceOfficialInvoiceImportApprovalSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Public_approval_late_authority_loss_rolls_back_audit_receipt_delivery_and_retries_same_key()
    {
        foreach (var phase in new[] { "before-audit", "after-audit", "after-save", "pre-commit" })
        foreach (var interceptor in new[] { true, false })
        {
            await using var run = await Run.StartAsync(); await UploadForSafety(run);
            var probe = new ApprovalSafetySaveProbe(); var deny = true; var savedFences = 0; var seenAudit = false; Scope? scope = null;
            var authority = new ApprovalSafetyAuthority(() =>
            {
                var staged = scope!.Database.ChangeTracker.Entries<FinanceOfficialInvoiceImportApprovalRow>()
                    .Any(entry => entry.State == EntityState.Added);
                if (staged && scope.Writer.HasPendingDrafts) seenAudit = true;
                if (probe.Saved) savedFences++;
                var reached = phase switch
                {
                    "before-audit" => staged && scope.Writer.HasPendingDrafts,
                    "after-audit" => seenAudit && staged && !scope.Writer.HasPendingDrafts,
                    "after-save" => probe.Saved,
                    _ => probe.Saved && savedFences >= 3,
                };
                return Task.FromResult(deny && reached ? PermissionDecision.Denied : PermissionDecision.Allowed);
            });
            await using var checker = scope = run.Service(Company, "SENTETIK-checker", authority, probe, interceptor);
            var failed = await checker.Service.ApproveAsync(checker.Context, new("approve-key"), new(Batch, "1"), default);
            Assert.Equal(CommandOutcome.Denied, failed.Outcome); Assert.Null(failed.Resource);
            AssertApprovalClean(checker); await AssertOnlyUpload(run);
            deny = false;
            var accepted = await checker.Service.ApproveAsync(checker.Context, new("approve-key"), new(Batch, "1"), default);
            Assert.Equal(CommandOutcome.Succeeded, accepted.Outcome); Assert.False(accepted.IsReplay);
            Assert.Equal("2", accepted.Resource!.Version); AssertApprovalClean(checker);
            await AssertOneApproval(run);
            var replay = await checker.Service.ApproveAsync(checker.Context with { CorrelationId = "later-correlation" },
                new("approve-key"), new(Batch, "1"), default);
            Assert.True(replay.IsReplay); Assert.Equal(accepted.Resource, replay.Resource);
            Assert.Equal(accepted.OriginalCorrelationId, replay.OriginalCorrelationId); await AssertOneApproval(run);
        }
    }

    [SqlServerFact]
    public async Task Public_approval_preserves_borrowed_transaction_dirty_tracker_and_pending_audit()
    {
        await using var run = await Run.StartAsync(); await UploadForSafety(run);
        foreach (var boundary in new[] { "borrowed", "dirty", "audit" })
        {
            await using var checker = run.Service(Company, "SENTETIK-checker", new AllowAuthority());
            await using var borrowed = boundary == "borrowed" ? await checker.Database.Database.BeginTransactionAsync() : null;
            var caller = new FinanceOfficialInvoiceImportApprovalRow { Id = Guid.NewGuid(), CompanyId = Company, BatchId = Batch };
            if (boundary == "dirty") checker.Database.Add(caller);
            // The existing upload command can stage a real collector draft without introducing a second audit store.
            if (boundary == "audit")
                checker.Collector.Enqueue(new(Company, "SENTETIK.caller.v1", "SENTETIK", Batch.ToString("D"), "Caller", null, 1,
                    "User", "SENTETIK-checker", FinanceOfficialInvoiceImportPermissionNamesV1.Approve, [], "{}", "{}",
                    "SENTETIK caller", null, GoymenERP.Modules.Audit.Application.Contracts.AuditAiInvolvement.None,
                    null, "Integration", null, null, checker.Context.CorrelationId, null, "caller-key"));
            var pending = checker.Writer.HasPendingDrafts;
            var result = await checker.Service.ApproveAsync(checker.Context, new("approve-key"), new(Batch, "1"), default);
            Assert.Equal(CommandOutcome.Unavailable, result.Outcome); Assert.Null(result.Resource);
            Assert.Same(borrowed, checker.Database.Database.CurrentTransaction);
            Assert.Equal(pending, checker.Writer.HasPendingDrafts);
            Assert.Equal(boundary == "dirty", checker.Database.ChangeTracker.HasChanges());
            if (boundary == "dirty") Assert.Same(caller, Assert.Single(checker.Database.ChangeTracker.Entries()).Entity);
            if (borrowed is not null) await borrowed.RollbackAsync();
        }
        await AssertOnlyUpload(run);
    }

    [SqlServerFact]
    public async Task Public_approval_cancellation_after_save_rolls_back_and_allows_same_scope_same_key_retry()
    {
        await using var run = await Run.StartAsync(); await UploadForSafety(run);
        using var cancellation = new CancellationTokenSource();
        var probe = new ApprovalSafetySaveProbe { AfterSave = cancellation.Cancel };
        await using var checker = run.Service(Company, "SENTETIK-checker", new AllowAuthority(), probe);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checker.Service.ApproveAsync(checker.Context,
            new("approve-key"), new(Batch, "1"), cancellation.Token));
        AssertApprovalClean(checker); await AssertOnlyUpload(run);
        probe.AfterSave = null;
        var accepted = await checker.Service.ApproveAsync(checker.Context, new("approve-key"), new(Batch, "1"), default);
        Assert.Equal(CommandOutcome.Succeeded, accepted.Outcome); Assert.False(accepted.IsReplay); await AssertOneApproval(run);
    }

    [SqlServerFact]
    public async Task Public_approval_failed_save_without_winner_never_reexecutes_mutation_or_records_transient_refusal()
        => await ApprovalSaveRaceSource(false);

    [SqlServerFact]
    public async Task Public_approval_failed_save_reads_a_genuine_original_key_winner_only_after_owned_abort()
        => await ApprovalSaveRaceSource(true);

    private static async Task ApprovalSaveRaceSource(bool winner)
    {
        await using var run = await Run.StartAsync(); await UploadForSafety(run);
        var probe = new ApprovalSafetySaveProbe { FailSave = true }; Scope? loser = null;
        FinanceOfficialInvoiceImportApprovalResultV1? original = null;
        var resumed = false;
        var authority = new ApprovalSafetyAuthority(async () =>
        {
            if (probe.Attempts > 0 && !resumed)
            {
                resumed = true;
                AssertApprovalClean(loser!); // The winner must commit after rollback/dispose, before replay-only reads.
                if (winner)
                {
                    await using var other = run.Service(Company, "SENTETIK-checker", new AllowAuthority());
                    original = await other.Service.ApproveAsync(other.Context, new("approve-key"), new(Batch, "1"), default);
                    Assert.Equal(CommandOutcome.Succeeded, original.Outcome);
                }
            }
            return PermissionDecision.Allowed;
        });
        await using var checker = loser = run.Service(Company, "SENTETIK-checker", authority, probe);
        var result = await checker.Service.ApproveAsync(checker.Context, new("approve-key"), new(Batch, "1"), default);
        Assert.True(resumed); Assert.Equal(1, probe.Attempts); AssertApprovalClean(checker);
        if (winner)
        {
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome); Assert.True(result.IsReplay);
            Assert.Equal(original!.Resource, result.Resource); Assert.Equal(original.OriginalCorrelationId, result.OriginalCorrelationId);
            await AssertOneApproval(run);
        }
        else { Assert.Equal(CommandOutcome.VersionConflict, result.Outcome); Assert.False(result.IsReplay); await AssertOnlyUpload(run); }
    }

    [SqlServerFact]
    public async Task Real_sql_deadlock_before_save_aborts_then_reads_only_the_original_key_once()
    {
        // Both native commands hold the batch's serializable read lock before requesting XLOCK.
        // SELECT changes no data; SQL Server produces the real 1205. The second iteration models EF's wrapper
        // around that same real exception, without constructing SQL exceptions or another persistence fixture.
        foreach (var wrapped in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); await UploadForSafety(run);
            var bothRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var winnerCommitted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = 0; var calls = new int[2]; var deadlocks = new bool[2]; var serverAborted = new bool[2]; var resumed = new bool[2];
            Scope?[] scopes = new Scope?[2];
            var transactions = new[] { new HashSet<Guid>(), new HashSet<Guid>() };
            var probes = new[] { new ApprovalSafetySaveProbe(), new ApprovalSafetySaveProbe() };
            var authorities = new IFinanceOfficialInvoiceImportApprovalAuthorityV1[2];
            for (var index = 0; index < 2; index++)
            {
                var slot = index;
                authorities[slot] = new ApprovalSafetyAuthority(async () =>
                {
                    var scope = scopes[slot]!; var call = ++calls[slot];
                    if (deadlocks[slot] && !resumed[slot])
                    {
                        resumed[slot] = true; AssertApprovalClean(scope);
                        await winnerCommitted.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    }
                    if (scope.Database.Database.CurrentTransaction is { } transaction)
                        transactions[slot].Add(transaction.TransactionId);
                    // Entry, owned fence, core entry, prior read, then native batch/approval read.
                    if (call == 5)
                    {
                        if (Interlocked.Increment(ref ready) == 2) bothRead.TrySetResult(true);
                        await bothRead.Task.WaitAsync(TimeSpan.FromSeconds(30));
                        await using var command = scope.Database.Database.GetDbConnection().CreateCommand();
                        var sqlTransaction = Assert.IsType<SqlTransaction>(scope.Database.Database.CurrentTransaction!.GetDbTransaction());
                        command.Transaction = sqlTransaction;
                        command.CommandTimeout = 20;
                        command.CommandText = "SELECT Id FROM finance.OfficialInvoiceImportBatches WITH (XLOCK, ROWLOCK) WHERE CompanyId=@company AND Id=@id";
                        var company = command.CreateParameter(); company.ParameterName = "@company"; company.Value = Company; command.Parameters.Add(company);
                        var id = command.CreateParameter(); id.ParameterName = "@id"; id.Value = Batch; command.Parameters.Add(id);
                        try { Assert.Equal(Batch, Assert.IsType<Guid>(await command.ExecuteScalarAsync())); }
                        catch (SqlException error) when (error.Number == 1205)
                        {
                            deadlocks[slot] = true;
                            Assert.Null(sqlTransaction.Connection); // Actual SQL victim, not a synthetic 1205 on a live transaction.
                            serverAborted[slot] = true;
                            if (wrapped) throw new DbUpdateException("SENTETIK wrapper around the real SQL deadlock", error);
                            throw;
                        }
                    }
                    return PermissionDecision.Allowed;
                });
            }
            await using var first = scopes[0] = run.Service(Company, "SENTETIK-checker-a", authorities[0], probes[0]);
            await using var second = scopes[1] = run.Service(Company, "SENTETIK-checker-b", authorities[1], probes[1]);
            var results = await Task.WhenAll(Decide(0), Decide(1));
            Assert.Single(results, result => result.Outcome == CommandOutcome.Succeeded);
            Assert.Single(results, result => result.Outcome == CommandOutcome.VersionConflict);
            Assert.Single(deadlocks, value => value); Assert.Single(serverAborted, value => value); Assert.Single(resumed, value => value);
            Assert.Equal(1, probes.Sum(probe => probe.Attempts)); // Loser never reached Save or retried mutation.
            for (var index = 0; index < 2; index++)
            {
                Assert.Equal(deadlocks[index] ? 2 : 1, transactions[index].Count);
                Assert.False(results[index].IsReplay); AssertApprovalClean(scopes[index]!);
            }
            await AssertOneApproval(run);

            async Task<FinanceOfficialInvoiceImportApprovalResultV1> Decide(int slot)
            {
                var scope = scopes[slot]!;
                var result = await scope.Service.ApproveAsync(scope.Context, new("deadlock-" + slot), new(Batch, "1"), default);
                if (result.Outcome == CommandOutcome.Succeeded) winnerCommitted.TrySetResult(true);
                return result;
            }
        }
    }

    [SqlServerFact]
    public async Task Public_approval_cleanup_failure_clears_owned_tracker_and_audit_without_starting_winner_read()
    {
        await using var run = await Run.StartAsync(); await UploadForSafety(run);
        var probe = new ApprovalSafetySaveProbe { FailSave = true };
        var rollback = new ApprovalSafetyRollbackFault(); var afterFailedSave = 0;
        var authority = new ApprovalSafetyAuthority(() =>
        { if (probe.Attempts > 0) afterFailedSave++; return Task.FromResult(PermissionDecision.Allowed); });
        await using var checker = run.Service(Company, "SENTETIK-checker", authority, probe, extra: rollback);
        await Assert.ThrowsAsync<AggregateException>(() => checker.Service.ApproveAsync(checker.Context,
            new("approve-key"), new(Batch, "1"), default));
        Assert.Equal(1, probe.Attempts); Assert.Equal(1, rollback.Attempts); Assert.Equal(0, afterFailedSave);
        AssertApprovalClean(checker); await AssertOnlyUpload(run);
    }

    [SqlServerFact]
    public async Task Public_approval_release_loss_after_commit_does_not_claim_rollback_and_original_key_replays()
    {
        await using var run = await Run.StartAsync(); await UploadForSafety(run);
        var committed = new ApprovalSafetyCommitted(); var deny = true;
        var authority = new ApprovalSafetyAuthority(() => Task.FromResult(committed.Count > 0 && deny
            ? PermissionDecision.Denied : PermissionDecision.Allowed));
        await using var checker = run.Service(Company, "SENTETIK-checker", authority, committed);
        var withheld = await checker.Service.ApproveAsync(checker.Context, new("approve-key"), new(Batch, "1"), default);
        Assert.Equal(CommandOutcome.Denied, withheld.Outcome); Assert.Null(withheld.Resource);
        AssertApprovalClean(checker); await AssertOneApproval(run);
        deny = false;
        var replay = await checker.Service.ApproveAsync(checker.Context with { CorrelationId = "retry-after-release-loss" },
            new("approve-key"), new(Batch, "1"), default);
        Assert.True(replay.IsReplay); Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
        Assert.Equal(checker.Context.CorrelationId, replay.OriginalCorrelationId); await AssertOneApproval(run);
    }

    [SqlServerFact]
    public async Task Public_approval_reentrant_entry_cannot_join_or_clean_the_outer_owned_transaction()
    {
        await using var run = await Run.StartAsync(); await UploadForSafety(run);
        Scope? scope = null; var attempted = false;
        var authority = new ApprovalSafetyAuthority(async () =>
        {
            if (!attempted && scope!.Database.Database.CurrentTransaction is { } transaction)
            {
                attempted = true;
                var entries = scope.Database.ChangeTracker.Entries().Select(entry => entry.Entity).ToArray();
                var pending = scope.Writer.HasPendingDrafts;
                var nested = await scope.Service.ApproveAsync(scope.Context, new("nested-key"), new(Batch, "1"), default);
                Assert.Equal(CommandOutcome.Unavailable, nested.Outcome);
                Assert.Same(transaction, scope.Database.Database.CurrentTransaction);
                Assert.Equal(entries, scope.Database.ChangeTracker.Entries().Select(entry => entry.Entity));
                Assert.Equal(pending, scope.Writer.HasPendingDrafts);
            }
            return PermissionDecision.Allowed;
        });
        await using var checker = scope = run.Service(Company, "SENTETIK-checker", authority);
        var accepted = await checker.Service.ApproveAsync(checker.Context, new("approve-key"), new(Batch, "1"), default);
        Assert.True(attempted); Assert.Equal(CommandOutcome.Succeeded, accepted.Outcome);
        AssertApprovalClean(checker); await AssertOneApproval(run);
    }

    [SqlServerFact]
    public async Task Public_approval_original_receipt_survives_a_later_different_key_version_refusal()
    {
        await using var run = await Run.StartAsync(); await UploadForSafety(run);
        await using var checker = run.Service(Company, "SENTETIK-checker", new AllowAuthority());
        var first = await checker.Service.ApproveAsync(checker.Context, new("approve-key"), new(Batch, "1"), default);
        Assert.Equal(CommandOutcome.Succeeded, first.Outcome);
        var later = await checker.Service.ApproveAsync(checker.Context, new("different-key"), new(Batch, "1"), default);
        Assert.Equal(CommandOutcome.VersionConflict, later.Outcome); Assert.False(later.IsReplay);
        var replay = await checker.Service.ApproveAsync(checker.Context with { CorrelationId = "later-command-correlation" },
            new("approve-key"), new(Batch, "1"), default);
        Assert.True(replay.IsReplay); Assert.Equal(first.Resource, replay.Resource);
        Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
        AssertApprovalClean(checker); await AssertOneApproval(run);
    }

    private static async Task UploadForSafety(Run run)
    {
        await using var maker = run.Service(Company, "SENTETIK-maker", new AllowAuthority());
        Assert.Equal(CommandOutcome.Succeeded,
            (await maker.Service.UploadAsync(maker.Context, new("upload-key"), Request(), default)).Outcome);
    }
    private static void AssertApprovalClean(Scope scope)
    {
        Assert.Null(scope.Database.Database.CurrentTransaction); Assert.False(scope.Writer.HasPendingDrafts);
        Assert.DoesNotContain(scope.Database.ChangeTracker.Entries(), row => row.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }
    private static async Task AssertOnlyUpload(Run run)
    {
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportApprovals"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportCommands"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportWorkDeliveries"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.OfficialInvoiceImportBatch'"));
    }
    private static async Task AssertOneApproval(Run run)
    {
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportApprovals"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportCommands"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportWorkDeliveries"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.OfficialInvoiceImportBatch'"));
    }
    private sealed class ApprovalSafetyAuthority(Func<Task<PermissionDecision>> read) : IFinanceOfficialInvoiceImportApprovalAuthorityV1
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context,
            FinanceOfficialInvoiceImportAuthorityActionV1 action, Guid id, CancellationToken token) => read();
    }
    private sealed class ApprovalSafetySaveProbe : SaveChangesInterceptor
    {
        public bool FailSave; public int Attempts; public bool Saved; public Action? AfterSave;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is FinanceDbContext db && db.ChangeTracker.Entries<FinanceOfficialInvoiceImportApprovalRow>().Any(row => row.State == EntityState.Added))
            { Attempts++; if (FailSave) throw new DbUpdateConcurrencyException("SENTETIK approval save fault"); }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (Attempts > 0) { Saved = true; AfterSave?.Invoke(); } return ValueTask.FromResult(result); }
    }
    private sealed class ApprovalSafetyRollbackFault : DbTransactionInterceptor
    {
        public int Attempts;
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction tx, TransactionEventData data,
            InterceptionResult result, CancellationToken token = default)
        { Attempts++; throw new InvalidOperationException("SENTETIK rollback fault"); }
    }
    private sealed class ApprovalSafetyCommitted : DbTransactionInterceptor
    {
        public int Count;
        public override Task TransactionCommittedAsync(DbTransaction tx, TransactionEndEventData data, CancellationToken token = default)
        { Count++; return Task.CompletedTask; }
    }
}
