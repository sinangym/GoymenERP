using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceOfficialSalesInvoiceSqlServerRelationalAcceptanceTests
{
    // Real migrated owner SQL and actual Audit writer, with the existing SqlClient fixture's induced 1205.
    // This controls the victim boundary; it is not a simultaneous SQL deadlock reproduction.
    [SqlServerFact]
    public async Task Injected_sql_deadlock_victim_aborts_before_one_readonly_original_receipt_pass_without_mutation_retry()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        foreach (var phase in new[] { "before-audit", "after-audit", "wrapped-save" })
        foreach (var publishWinner in new[] { false, true })
        {
            var id = await OfficialSafetyPrepare(run); var save = new OfficialSafetySaveProbe(id);
            var fault = new OfficialInjectedDeadlockSave(id, phase == "wrapped-save");
            await using var scope = new OfficialSafetyScope(run, probe: save, extra: fault);
            var injected = false; var seenPending = false; var reconciled = false;
            var boundaries = new HashSet<Guid>(); var key = "official-induced-1205-" + id;
            FinanceOfficialSalesInvoiceResultV1? winner = null;
            scope.Authority.BeforeRead = () =>
            {
                var staged = scope.Database.ChangeTracker.Entries<FinanceOfficialSalesInvoiceRow>()
                    .Any(row => row.Entity.Id == id && row.Entity.Version == 2);
                if (staged && scope.Writer.HasPendingDrafts) seenPending = true;
                var reached = phase == "before-audit" ? staged && scope.Writer.HasPendingDrafts
                    : phase == "after-audit" && staged && seenPending && !scope.Writer.HasPendingDrafts;
                if (!injected && reached) { injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); }
                return PermissionDecision.Allowed;
            };
            scope.Authority.AfterRead = async () =>
            {
                if (scope.Database.Database.CurrentTransaction is { } transaction) boundaries.Add(transaction.TransactionId);
                if (reconciled || !(injected || fault.Injected) || scope.Database.Database.CurrentTransaction is not null) return;
                reconciled = true; scope.AssertClean();
                if (!publishWinner) return;
                await using var actual = new OfficialSafetyScope(run);
                winner = await actual.Decide(id, approve, key);
                Assert.Equal(CommandOutcome.Succeeded, winner.Outcome); Assert.False(winner.IsReplay);
            };
            var result = await scope.Decide(id, approve, key);
            Assert.True(reconciled); Assert.Equal(2, boundaries.Count); scope.AssertClean();
            Assert.Equal(phase == "wrapped-save" ? 1 : 0, save.Attempts);
            if (phase == "after-audit") { Assert.True(seenPending); Assert.False(save.Saved); }
            if (publishWinner)
            {
                Assert.NotNull(winner); Assert.Equal(CommandOutcome.Succeeded, result.Outcome); Assert.True(result.IsReplay);
                Assert.Equal(System.Text.Json.JsonSerializer.Serialize(winner.Resource, FinanceJson.CreateOptions()),
                    System.Text.Json.JsonSerializer.Serialize(result.Resource, FinanceJson.CreateOptions()));
                Assert.Equal(winner.OriginalCorrelationId, result.OriginalCorrelationId);
                await OfficialSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
            }
            else
            {
                Assert.Equal(CommandOutcome.VersionConflict, result.Outcome); Assert.False(result.IsReplay);
                Assert.Equal("concurrent_change", Assert.Single(result.Issues).Code);
                await OfficialSafetyDurable(run, id, 1, 1, 0, 0);
                // Only a new explicit invocation may execute the original mutation after the readonly pass found no winner.
                Assert.Equal(CommandOutcome.Succeeded, (await scope.Decide(id, approve, key)).Outcome);
                await OfficialSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
            }
        }
    }

    [SqlServerFact]
    public async Task Injected_sql_deadlock_victim_in_the_readonly_pass_propagates_after_cleanup_without_a_third_pass()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await OfficialSafetyPrepare(run); var save = new OfficialSafetySaveProbe(id);
            await using var scope = new OfficialSafetyScope(run, probe: save);
            var boundaries = new HashSet<Guid>();
            scope.Authority.BeforeRead = () =>
            {
                if (scope.Database.Database.CurrentTransaction is { } tx && boundaries.Add(tx.TransactionId))
                    throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                return PermissionDecision.Allowed;
            };
            var error = await Assert.ThrowsAsync<SqlException>(() => scope.Decide(id, approve, "official-induced-readonly-" + id));
            Assert.Equal(1205, error.Number); Assert.Equal(2, boundaries.Count); Assert.Equal(0, save.Attempts);
            scope.AssertClean(); await OfficialSafetyDurable(run, id, 1, 1, 0, 0);
        }
    }

    [SqlServerFact]
    public async Task Injected_sql_deadlock_victim_after_commit_never_retries_or_claims_that_the_durable_decision_was_rolled_back()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await OfficialSafetyPrepare(run); var save = new OfficialSafetySaveProbe(id);
            var committed = new OfficialSafetyCommitted(); var denyRelease = true;
            await using var scope = new OfficialSafetyScope(run, probe: save, extra: committed);
            scope.Authority.BeforeRead = () => committed.Commits > 0 && denyRelease
                ? throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205) : PermissionDecision.Allowed;
            var key = "official-induced-release-" + id;
            Assert.Equal(1205, (await Assert.ThrowsAsync<SqlException>(() => scope.Decide(id, approve, key))).Number);
            Assert.Equal(1, save.Attempts); Assert.Equal(1, committed.Commits); scope.AssertClean();
            await OfficialSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
            denyRelease = false; scope.Correlation = "official-induced-release-retry";
            var replay = await scope.Decide(id, approve, key);
            Assert.Equal(CommandOutcome.Succeeded, replay.Outcome); Assert.True(replay.IsReplay);
            Assert.Equal(scope.OriginalCorrelation, replay.OriginalCorrelationId); Assert.Equal(1, save.Attempts);
            await OfficialSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
        }
    }

    [SqlServerFact]
    public async Task Injected_sql_deadlock_victim_with_cleanup_failure_never_enters_the_readonly_pass()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await OfficialSafetyPrepare(run); var rollback = new OfficialSafetyRollbackFault();
            await using var scope = new OfficialSafetyScope(run, extra: rollback);
            var boundaries = new HashSet<Guid>();
            scope.Authority.BeforeRead = () =>
            {
                if (scope.Database.Database.CurrentTransaction is { } tx) boundaries.Add(tx.TransactionId);
                if (scope.Writer.HasPendingDrafts) throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                return PermissionDecision.Allowed;
            };
            var error = await Assert.ThrowsAsync<AggregateException>(() => scope.Decide(id, approve, "official-induced-cleanup-" + id));
            Assert.Contains(error.InnerExceptions, item => item is SqlException { Number: 1205 });
            Assert.Single(boundaries); Assert.Equal(1, rollback.Attempts); scope.AssertClean();
            await OfficialSafetyDurable(run, id, 1, 1, 0, 0);
        }
    }

    [SqlServerFact]
    public async Task Injected_sql_deadlock_victim_after_provider_rollback_skips_second_rollback_and_reads_only_original_receipt()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        foreach (var wrapped in new[] { false, true })
        foreach (var publishWinner in new[] { false, true })
        {
            var id = await OfficialSafetyPrepare(run); var save = new OfficialSafetySaveProbe(id);
            var secondRollback = new OfficialSafetyRollbackFault();
            await using var scope = new OfficialSafetyScope(run, probe: save, extra: secondRollback);
            var seenPending = false; var providerCompleted = false; var reconciled = false;
            var key = "official-provider-abort-" + id; var boundaries = new HashSet<Guid>();
            FinanceOfficialSalesInvoiceResultV1? winner = null;
            scope.Authority.BeforeRead = () =>
            {
                if (scope.Writer.HasPendingDrafts) seenPending = true;
                return PermissionDecision.Allowed;
            };
            scope.Authority.AfterRead = async () =>
            {
                var transaction = scope.Database.Database.CurrentTransaction;
                if (transaction is not null) boundaries.Add(transaction.TransactionId);
                if (!providerCompleted && seenPending && !scope.Writer.HasPendingDrafts && transaction is not null)
                {
                    // Complete the real provider transaction, then inject the existing 1205 primitive; no real deadlock claim.
                    var sql = Assert.IsType<SqlTransaction>(transaction.GetDbTransaction());
                    await sql.RollbackAsync(); Assert.Null(sql.Connection); providerCompleted = true;
                    var victim = SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                    if (wrapped) throw new DbUpdateException("SENTETIK provider-aborted victim", victim);
                    throw victim;
                }
                if (!providerCompleted || reconciled || transaction is not null) return;
                reconciled = true; scope.AssertClean();
                if (!publishWinner) return;
                await using var actual = new OfficialSafetyScope(run);
                winner = await actual.Decide(id, approve, key);
                Assert.Equal(CommandOutcome.Succeeded, winner.Outcome); Assert.False(winner.IsReplay);
            };
            var result = await scope.Decide(id, approve, key);
            Assert.True(providerCompleted); Assert.True(reconciled); Assert.Equal(0, secondRollback.Attempts);
            Assert.Equal(2, boundaries.Count); Assert.Equal(0, save.Attempts); scope.AssertClean();
            if (publishWinner)
            {
                Assert.NotNull(winner); Assert.Equal(CommandOutcome.Succeeded, result.Outcome); Assert.True(result.IsReplay);
                Assert.Equal(System.Text.Json.JsonSerializer.Serialize(winner.Resource, FinanceJson.CreateOptions()),
                    System.Text.Json.JsonSerializer.Serialize(result.Resource, FinanceJson.CreateOptions()));
                Assert.Equal(winner.OriginalCorrelationId, result.OriginalCorrelationId);
                await OfficialSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
            }
            else
            {
                Assert.Equal(CommandOutcome.VersionConflict, result.Outcome); Assert.False(result.IsReplay);
                await OfficialSafetyDurable(run, id, 1, 1, 0, 0);
                Assert.Equal(CommandOutcome.Succeeded, (await scope.Decide(id, approve, key)).Outcome);
                await OfficialSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
            }
        }
    }

    private sealed class OfficialInjectedDeadlockSave(Guid id, bool enabled) : SaveChangesInterceptor
    {
        public bool Injected;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken token = default)
        {
            if (enabled && !Injected && data.Context is FinanceDbContext db
                && db.ChangeTracker.Entries<FinanceOfficialSalesInvoiceCommandRow>().Any(row => row.State == EntityState.Added && row.Entity.InvoiceId == id))
            {
                Injected = true;
                throw new DbUpdateException("SENTETIK injected provider deadlock victim", SqlConcurrencyWrapperPolicyTests.SqlFailure(1205));
            }
            return ValueTask.FromResult(result);
        }
    }
}
