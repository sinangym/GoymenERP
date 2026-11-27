using Microsoft.EntityFrameworkCore.Storage;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCommandApiSqlServerRelationalAcceptanceTests
{
    // Existing controlled SqlClient 1205 helper, real SQL transactions and same scoped production Audit collector/writer.
    // This is an ordered replay/rollback source, not a real simultaneous deadlock or Identity/HTTP acceptance claim.
    [SqlServerFact]
    public async Task Manual_bank_approval_1205_before_or_after_Save_aborts_then_reads_one_original_key_without_retrying_mutation()
    {
        await using var run = await ManualApprovalRun();
        foreach (var phase in new[] { "direct-save", "wrapped-save", "audit-before-save", "completed-provider", "post-save" })
        foreach (var publish in new[] { false, true })
        {
            var id = await ManualApprovalDraft(run); var key = "manual-deadlock-" + id;
            var probe = new ManualApprovalSaveProbe(id); var fault = new ManualApprovalDeadlockSave(id, phase);
            await using var scope = new ManualApprovalScope(run, probe: probe, extra: fault);
            var boundaries = new HashSet<Guid>(); var staged = new HashSet<Guid>();
            ManualBankPaymentCommandResult? winner = null; var injected = false;
            scope.Permissions.AfterRead = async () =>
            {
                if (scope.Database.Database.CurrentTransaction is not { } tx) return;
                boundaries.Add(tx.TransactionId);
                if (scope.Database.ChangeTracker.Entries<ManualBankPaymentRow>().Any(row => row.Entity.Id == id && row.State == EntityState.Modified))
                    staged.Add(tx.TransactionId);
                if (!injected && boundaries.Count == 1 && (phase == "post-save" && probe.Saved
                    || (phase is "audit-before-save" or "completed-provider") && staged.Count == 1 && !scope.Writer.HasPendingDrafts && probe.Attempts == 0))
                {
                    injected = true;
                    if (phase == "completed-provider")
                    {
                        // Complete the actual provider transaction, leaving its EF owner handle for cleanup.
                        // The following 1205 is controlled; this is not a simultaneous deadlock reproduction.
                        var provider = Assert.IsType<Microsoft.Data.SqlClient.SqlTransaction>(tx.GetDbTransaction());
                        await provider.RollbackAsync(); Assert.Null(provider.Connection);
                    }
                    throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                }
                if (boundaries.Count != 2 || winner is not null || !publish) return;
                Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
                await using var actual = new ManualApprovalScope(run);
                winner = await actual.Approve(id, key);
                Assert.Equal(CommandOutcome.Succeeded, winner.Outcome); Assert.False(winner.IsReplay);
            };
            var result = await scope.Approve(id, key);
            Assert.True(injected || fault.Fired); Assert.Equal(2, boundaries.Count); Assert.Single(staged);
            Assert.Equal((phase is "audit-before-save" or "completed-provider") ? 0 : 1, probe.Attempts);
            Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts); Assert.Null(scope.Database.Database.CurrentTransaction);
            if (publish)
            {
                Assert.NotNull(winner); Assert.True(result.IsReplay); Assert.Equal(winner.Resource, result.Resource);
                Assert.Equal(winner.OriginalCorrelationId, result.OriginalCorrelationId);
            }
            else
            {
                Assert.Equal(CommandOutcome.VersionConflict, result.Outcome); Assert.False(result.IsReplay);
                Assert.Equal("concurrent_change", Assert.Single(result.Issues).Code);
            }
            Assert.Equal(publish ? 1 : 0, await ManualApprovalCommands(run, id));
            Assert.Equal(publish ? 1 : 0, await ManualApprovalAudits(run, id));
            Assert.Equal(publish ? 2L : 1L, await run.ScalarAsync<long>("SELECT Version FROM finance.ManualBankPayments WHERE Id=@id", ("@id", id)));
            if (!publish)
            {
                fault.Disabled = true;
                var retried = await scope.Approve(id, key);
                Assert.Equal(CommandOutcome.Succeeded, retried.Outcome); Assert.False(retried.IsReplay);
                Assert.Equal(1, await ManualApprovalCommands(run, id)); Assert.Equal(1, await ManualApprovalAudits(run, id));
            }
        }
        // A second read-only-pass deadlock is unavailable, with no third transaction or synthetic terminal receipt.
        var repeatedId = await ManualApprovalDraft(run);
        await using (var repeated = new ManualApprovalScope(run))
        {
            var boundaries = new HashSet<Guid>();
            repeated.Permissions.AfterRead = () =>
            {
                if (repeated.Database.Database.CurrentTransaction is { } tx)
                { boundaries.Add(tx.TransactionId); throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); }
                return Task.CompletedTask;
            };
            var result = await repeated.Approve(repeatedId, "manual-deadlock-twice");
            Assert.Equal(CommandOutcome.Unavailable, result.Outcome); Assert.Equal(2, boundaries.Count);
            Assert.Empty(repeated.Database.ChangeTracker.Entries()); Assert.False(repeated.Writer.HasPendingDrafts); Assert.Null(repeated.Database.Database.CurrentTransaction);
            Assert.Equal(0, await ManualApprovalCommands(run, repeatedId)); Assert.Equal(0, await ManualApprovalAudits(run, repeatedId));
        }
        var cleanupId = await ManualApprovalDraft(run); var rollback = new ManualApprovalRollbackFailure();
        await using (var cleanup = new ManualApprovalScope(run, extra: rollback))
        {
            var boundaries = new HashSet<Guid>();
            cleanup.Permissions.AfterRead = () =>
            {
                if (cleanup.Database.Database.CurrentTransaction is { } tx) boundaries.Add(tx.TransactionId);
                if (cleanup.Writer.HasPendingDrafts) throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                return Task.CompletedTask;
            };
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => cleanup.Approve(cleanupId, "manual-deadlock-cleanup-fault"));
            Assert.Equal("SENTETIK manual approval rollback fault", error.Message);
            Assert.Single(boundaries); Assert.Equal(1, rollback.Attempts);
            Assert.Empty(cleanup.Database.ChangeTracker.Entries()); Assert.False(cleanup.Writer.HasPendingDrafts); Assert.Null(cleanup.Database.Database.CurrentTransaction);
            Assert.Equal(0, await ManualApprovalCommands(run, cleanupId)); Assert.Equal(0, await ManualApprovalAudits(run, cleanupId));
        }
    }

    private sealed class ManualApprovalDeadlockSave(Guid id, string phase) : SaveChangesInterceptor
    {
        public bool Fired, Disabled;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (!Disabled && (phase is "direct-save" or "wrapped-save") && data.Context is FinanceDbContext db
                && db.ChangeTracker.Entries<FinanceCommandRow>().Any(row => row.State == EntityState.Added && row.Entity.ManualBankPaymentId == id))
            {
                Fired = true; var sql = SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                if (phase == "wrapped-save") throw new DbUpdateException("SENTETIK approval deadlock save wrapper", sql);
                throw sql;
            }
            return ValueTask.FromResult(result);
        }
    }
}
