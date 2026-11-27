using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCreditLimitSqlServerRelationalAcceptanceTests
{
    // Controlled 1205 injection from the existing SqlClient helper, real migrated SQL/Audit/owned rollback.
    // Ordered native winner publication is not a simultaneous SQL deadlock or Identity/delegation E2E claim.
    [SqlServerFact]
    public async Task Credit_limit_1205_before_or_after_Save_aborts_then_reads_one_original_key_without_retrying_mutation()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        foreach (var phase in new[] { "direct-save", "wrapped-save", "audit-before-save", "post-save" })
        foreach (var publish in new[] { false, true })
        {
            var id = await LimitSafetyPrepare(run); var key = "limit-deadlock-" + id;
            var probe = new LimitSafetySaveProbe(id);
            var fault = new LimitDecisionDeadlockSave(id, phase);
            await using var scope = new LimitSafetyScope(run, probe: probe, extra: fault);
            var boundaries = new HashSet<Guid>(); var staged = new HashSet<Guid>();
            PartyCreditLimitCommandResult? winner = null; var injected = false;
            scope.Permissions.AfterRead = async () =>
            {
                if (scope.Database.Database.CurrentTransaction is not { } tx) return;
                boundaries.Add(tx.TransactionId);
                if (scope.Database.ChangeTracker.Entries<PartyCreditLimitRow>().Any(row => row.Entity.Id == id && row.State == EntityState.Modified))
                    staged.Add(tx.TransactionId);
                if (!injected && boundaries.Count == 1 && (phase == "post-save" && probe.Saved
                    || phase == "audit-before-save" && staged.Count == 1 && !scope.Writer.HasPendingDrafts && probe.Attempts == 0))
                {
                    injected = true;
                    throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                }
                if (boundaries.Count != 2 || winner is not null || !publish) return;
                Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
                await using var actual = new LimitSafetyScope(run);
                winner = await actual.Decide(id, approve, key);
                Assert.Equal(CommandOutcome.Succeeded, winner.Outcome); Assert.False(winner.IsReplay);
            };
            var result = await scope.Decide(id, approve, key);
            Assert.True(injected || fault.Fired); Assert.Equal(2, boundaries.Count); Assert.Single(staged);
            Assert.Equal(phase == "audit-before-save" ? 0 : 1, probe.Attempts);
            scope.AssertClean();
            if (publish)
            {
                Assert.NotNull(winner); Assert.True(result.IsReplay); Assert.Equal(winner.Resource, result.Resource);
                Assert.Equal(winner.OriginalCorrelationId, result.OriginalCorrelationId); Assert.NotNull(result.DecisionReceipt);
            }
            else
            {
                Assert.Equal(CommandOutcome.VersionConflict, result.Outcome); Assert.False(result.IsReplay);
                Assert.Equal("concurrent_change", Assert.Single(result.Issues).Code);
            }
            await LimitSafetyDurable(run, id, publish ? 2 : 1, publish ? approve ? 2 : 3 : 1, publish ? 1 : 0, publish ? 1 : 0);
            if (!publish)
            {
                // A nonpersisted conflict leaves the original key usable; this is a new explicit caller retry.
                fault.Disabled = true;
                var retried = await scope.Decide(id, approve, key);
                Assert.Equal(CommandOutcome.Succeeded, retried.Outcome); Assert.False(retried.IsReplay);
                await LimitSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
            }
        }
        // A second 1205 in the read-only pass must escape, not enter a third transaction or write a conflict receipt.
        var repeatedId = await LimitSafetyPrepare(run);
        await using (var repeated = new LimitSafetyScope(run))
        {
            var boundaries = new HashSet<Guid>();
            repeated.Permissions.AfterRead = () =>
            {
                if (repeated.Database.Database.CurrentTransaction is { } tx)
                { boundaries.Add(tx.TransactionId); throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); }
                return Task.CompletedTask;
            };
            var error = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => repeated.Decide(repeatedId, true, "limit-deadlock-twice"));
            Assert.Equal(1205, error.Number); Assert.Equal(2, boundaries.Count); repeated.AssertClean();
            await LimitSafetyDurable(run, repeatedId, 1, 1, 0, 0);
        }
        var cleanupId = await LimitSafetyPrepare(run); var rollback = new LimitSafetyRollbackFault();
        await using (var cleanup = new LimitSafetyScope(run, extra: rollback))
        {
            var boundaries = new HashSet<Guid>();
            cleanup.Permissions.AfterRead = () =>
            {
                if (cleanup.Database.Database.CurrentTransaction is { } tx) boundaries.Add(tx.TransactionId);
                if (cleanup.Writer.HasPendingDrafts) throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                return Task.CompletedTask;
            };
            await Assert.ThrowsAsync<AggregateException>(() => cleanup.Decide(cleanupId, true, "limit-deadlock-cleanup-fault"));
            Assert.Single(boundaries); Assert.Equal(1, rollback.Attempts); cleanup.AssertClean();
            await LimitSafetyDurable(run, cleanupId, 1, 1, 0, 0);
        }
    }

    private sealed class LimitDecisionDeadlockSave(Guid id, string phase) : SaveChangesInterceptor
    {
        public bool Fired, Disabled;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (!Disabled && (phase is "direct-save" or "wrapped-save") && data.Context is FinanceDbContext db
                && db.ChangeTracker.Entries<PartyCreditLimitCommandRow>().Any(row => row.State == EntityState.Added && row.Entity.PartyCreditLimitId == id))
            {
                Fired = true; var sql = SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                if (phase == "wrapped-save") throw new DbUpdateException("SENTETIK approval deadlock save wrapper", sql);
                throw sql;
            }
            return ValueTask.FromResult(result);
        }
    }
}
