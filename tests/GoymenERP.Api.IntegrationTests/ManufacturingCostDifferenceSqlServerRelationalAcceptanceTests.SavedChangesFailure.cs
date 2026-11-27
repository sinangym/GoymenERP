using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingCostDifferenceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Cost_difference_domain_failure_after_first_or_final_accepted_SQL_save_aborts_and_keeps_original_key_retry_once()
    {
        foreach (var finalSave in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var id = await SafetyPrepare(run); var before = await SafetyCounts(run);
            await using var scope = new CostApprovalScope(run);
            using var fault = new CostAcceptedSaveDomainFault(scope, id, finalSave);
            var refused = await scope.Approve(id, "accepted-save-domain");
            Assert.Equal(AccountingApiStatus.Unprocessable, refused.Status); Assert.Equal("cost_difference_posting_refused", refused.Code);
            Assert.Null(refused.Resource); Assert.False(refused.IsReplay); Assert.True(fault.Fired);
            Assert.True(fault.CleanAtFailure); Assert.False(fault.PendingAuditAtFailure);
            Assert.Equal(finalSave, fault.CommandAtFailure); Assert.Equal(finalSave ? 2 : 1, fault.CompletedSaves);
            Assert.Equal(1, scope.Writer.Discards); scope.AssertClean(); Assert.Equal(before, await SafetyCounts(run));
            await using (var read = run.Scope())
            {
                var pending = await read.Store.FindAsync(id); Assert.Equal("Prepared", pending?.Status); Assert.Equal(2L, pending?.Version);
            }
            var accepted = await scope.Approve(id, "accepted-save-domain"); var completed = fault.CompletedSaves;
            var replay = await scope.Approve(id, "accepted-save-domain");
            Assert.Equal(AccountingApiStatus.Ok, accepted.Status); Assert.Equal("Posted", accepted.Resource?.Status);
            Assert.Equal(3L, accepted.Resource?.Version); Assert.NotNull(accepted.Resource?.ApprovalReceipt);
            Assert.True(replay.IsReplay); Assert.Equal(accepted.Resource?.JournalId, replay.Resource?.JournalId);
            Assert.Equal(accepted.Resource?.ApprovalReceipt, replay.Resource?.ApprovalReceipt); Assert.Equal(scope.CorrelationId, replay.OriginalCorrelationId);
            Assert.Equal((finalSave ? 2 : 1) + 2, completed); Assert.Equal(completed, fault.CompletedSaves); scope.AssertClean();
            Assert.Equal(before with { Journals = before.Journals + 1, Deliveries = before.Deliveries + 1,
                Commands = before.Commands + 1, Audits = before.Audits + 2 }, await SafetyCounts(run));
        }
    }

    [SqlServerFact]
    public async Task Cost_difference_accepted_save_domain_failure_does_not_mask_rollback_or_dispose_fault_as_native_422()
    {
        foreach (var finalSave in new[] { false, true })
        foreach (var dispose in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var id = await SafetyPrepare(run); var cleanup = new CostApprovalCleanup(dispose);
            await using var scope = new CostApprovalScope(run, extra: cleanup);
            using var fault = new CostAcceptedSaveDomainFault(scope, id, finalSave);
            var failure = await Assert.ThrowsAsync<AggregateException>(() => scope.Approve(id, "accepted-save-cleanup"));
            Assert.Contains("cleanup failed", failure.Message, StringComparison.Ordinal); Assert.True(cleanup.Failed); Assert.True(fault.Fired);
            Assert.True(fault.CleanAtFailure); Assert.False(fault.PendingAuditAtFailure); Assert.Equal(finalSave, fault.CommandAtFailure);
            Assert.Equal(finalSave ? 2 : 1, fault.CompletedSaves); Assert.Equal(1, scope.Writer.Discards);
            Assert.False(scope.Db.ChangeTracker.HasChanges()); Assert.False(scope.Writer.HasPendingDrafts);
            // Cleanup failure is propagated: no clean rollback, business answer or original-key winner claim is made.
        }
    }

    // Controlled EF completion fault on actual SQL; neither a synthetic store nor a natural concurrency reproduction.
    private sealed class CostAcceptedSaveDomainFault : IDisposable
    {
        private readonly CostApprovalScope scope; private readonly Guid id; private readonly bool finalSave;
        public bool Fired, CleanAtFailure, PendingAuditAtFailure, CommandAtFailure; public int CompletedSaves;
        public CostAcceptedSaveDomainFault(CostApprovalScope scope, Guid id, bool finalSave)
        { this.scope = scope; this.id = id; this.finalSave = finalSave; scope.Db.SavedChanges += AfterAcceptedSave; }
        private void AfterAcceptedSave(object? sender, SavedChangesEventArgs args)
        {
            CompletedSaves++;
            var proposal = scope.Db.ChangeTracker.Entries<ManufacturingCostDifferenceRow>().Single(entry => entry.Entity.Id == id).Entity;
            if (Fired || proposal.Status != (finalSave ? 3 : 2)
                || !scope.Db.ChangeTracker.Entries<JournalRow>().Any(entry => entry.Entity.SourceId == id)) return;
            Fired = true; CleanAtFailure = !scope.Db.ChangeTracker.HasChanges(); PendingAuditAtFailure = scope.Writer.HasPendingDrafts;
            CommandAtFailure = scope.Db.ChangeTracker.Entries<AccountingCommandRow>().Any(entry => entry.Entity.CommandName == ManufacturingCostDifferenceCommandNamesV1.Approve);
            throw new AccountingDomainException("SENTETIK-accepted-save-domain", "SENTETIK controlled domain refusal after EF accepted SQL writes");
        }
        public void Dispose() => scope.Db.SavedChanges -= AfterAcceptedSave;
    }
}
