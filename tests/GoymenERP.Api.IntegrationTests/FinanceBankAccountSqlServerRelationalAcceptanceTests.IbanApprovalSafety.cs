using System.Transactions;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceBankAccountSqlServerRelationalAcceptanceTests
{
    private const string IbanSafetyValue = "TR060006100519786457841327";
    private const string IbanSafetyApprovedEvent = "finance.bank-account.iban-change-approved.v1";

    [SqlServerFact]
    public async Task Iban_approval_late_permission_actor_or_delegation_loss_rolls_back_value_receipt_and_actual_audit_then_retries_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var fault in new[] { "before-audit", "after-audit", "after-save", "precommit", "actor", "delegation" })
        {
            var id = await IbanSafetyPending(run); var probe = new BankSafetySaveProbe(id);
            await using var scope = new BankSafetyScope(run, probe: probe);
            var deny = true; var observed = false; var afterSaveReads = 0;
            scope.Permissions.BeforeRead = () =>
            {
                var staged = scope.Database.ChangeTracker.Entries<BankAccountRow>().Any(row => row.Entity.Id == id && row.Entity.Version == 4);
                if (probe.Saved) afterSaveReads++;
                var phase = fault == "before-audit" ? staged && scope.Writer.HasPendingDrafts
                    : fault == "after-audit" ? staged && !scope.Writer.HasPendingDrafts
                    : fault == "precommit" ? afterSaveReads >= 3 : probe.Saved;
                if (!deny || !phase) return PermissionDecision.Allowed;
                observed = true;
                if (fault == "actor") { scope.User.Actor = "SENTETIK-changed-checker"; return PermissionDecision.Allowed; }
                // Delegation denial is a controlled owner permission-port response, not a complete WP3 Identity E2E.
                return PermissionDecision.Denied;
            };
            var refused = await IbanSafetyApprove(scope, id, "iban-late-" + fault);
            Assert.True(observed); Assert.Equal(CommandOutcome.Denied, refused.Outcome);
            Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
            Assert.Null(scope.Database.Database.CurrentTransaction);
            await AssertIbanSafetyDurable(run, id, version: 3, commandCount: 0, auditCount: 0);
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.BankAccounts WHERE Id=@id AND Iban IS NULL AND PendingIban=@iban",
                ("@id", id), ("@iban", IbanSafetyValue)));
            deny = false; scope.User.Actor = Approver;
            var saved = await IbanSafetyApprove(scope, id, "iban-late-" + fault);
            Assert.Equal(CommandOutcome.Succeeded, saved.Outcome); Assert.False(saved.IsReplay);
            var replay = await IbanSafetyApprove(scope, id, "iban-late-" + fault);
            Assert.True(replay.IsReplay); Assert.Equal(saved.Resource, replay.Resource);
            Assert.Equal(saved.OriginalCorrelationId, replay.OriginalCorrelationId);
            Assert.False(scope.Writer.HasPendingDrafts);
            await AssertIbanSafetyDurable(run, id, version: 4, commandCount: 1, auditCount: 1);
            Assert.Equal(IbanSafetyValue, await run.ScalarAsync<string>("SELECT Iban FROM finance.BankAccounts WHERE Id=@id", ("@id", id)));
        }
    }

    [SqlServerFact]
    public async Task Iban_approval_entry_refusal_preserves_caller_state_even_when_an_original_receipt_exists()
    {
        await using var run = await Run.StartAsync(); var id = await IbanSafetyPending(run);
        await using (var first = new BankSafetyScope(run)) Assert.Equal(CommandOutcome.Succeeded, (await IbanSafetyApprove(first, id, "iban-entry-original")).Outcome);
        foreach (var boundary in new[] { "missing-writer", "borrowed", "dirty", "audit", "ambient" })
        foreach (var replay in new[] { false, true })
        {
            await using var scope = new BankSafetyScope(run, missingWriter: boundary == "missing-writer");
            await using var borrowed = boundary == "borrowed" ? await scope.Database.Database.BeginTransactionAsync() : null;
            var row = boundary == "dirty" ? await scope.Database.BankAccounts.SingleAsync(item => item.Id == id) : null;
            if (row is not null) row.DisplayName = "SENTETIK caller IBAN work";
            if (boundary == "audit") scope.Collector.Enqueue(BankSafetyDraft(run, id));
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            var ambientBefore = Transaction.Current;
            var result = await IbanSafetyApprove(scope, id, replay ? "iban-entry-original" : "iban-entry-new");
            Assert.Equal(CommandOutcome.Unavailable, result.Outcome); Assert.Same(ambientBefore, Transaction.Current);
            Assert.Same(borrowed, scope.Database.Database.CurrentTransaction);
            Assert.Equal(boundary == "dirty", scope.Database.ChangeTracker.HasChanges());
            Assert.Equal(boundary == "audit", scope.Writer.HasPendingDrafts);
            if (row is not null) Assert.Same(row, Assert.Single(scope.Database.ChangeTracker.Entries()).Entity);
            scope.Writer.DiscardPending();
        }
        await AssertIbanSafetyDurable(run, id, version: 4, commandCount: 1, auditCount: 1);
    }

    [SqlServerFact]
    public async Task Iban_approval_failed_save_disposes_owned_work_before_one_native_winner_read_without_mutation_retry()
    {
        await using var run = await Run.StartAsync();
        foreach (var publish in new[] { false, true })
        {
            var id = await IbanSafetyPending(run); var probe = new BankSafetySaveProbe(id) { FailSave = true };
            await using var scope = new BankSafetyScope(run, probe: probe);
            var boundaries = new HashSet<Guid>(); BankAccountCommandResult? winner = null;
            scope.Permissions.AfterRead = async () =>
            {
                if (scope.Database.Database.CurrentTransaction is not { } transaction) return;
                boundaries.Add(transaction.TransactionId);
                if (boundaries.Count != 2 || winner is not null || !publish) return;
                Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts); Assert.Equal(1, probe.Attempts);
                await using var actual = new BankSafetyScope(run);
                winner = await IbanSafetyApprove(actual, id, "iban-race-original");
                Assert.Equal(CommandOutcome.Succeeded, winner.Outcome); Assert.False(winner.IsReplay);
            };
            // A controlled SavingChanges concurrency fault and an ordered native winner; not a simultaneous race claim.
            var result = await IbanSafetyApprove(scope, id, "iban-race-original");
            Assert.Equal(2, boundaries.Count); Assert.Equal(1, probe.Attempts);
            Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
            Assert.Null(scope.Database.Database.CurrentTransaction);
            if (publish)
            { Assert.NotNull(winner); Assert.True(result.IsReplay); Assert.Equal(winner.Resource, result.Resource); Assert.Equal(winner.OriginalCorrelationId, result.OriginalCorrelationId); }
            else
            { Assert.Equal(CommandOutcome.VersionConflict, result.Outcome); Assert.Equal("concurrent_change", Assert.Single(result.Issues).Code); }
            await AssertIbanSafetyDurable(run, id, publish ? 4 : 3, publish ? 1 : 0, publish ? 1 : 0);
        }
    }

    [SqlServerFact]
    public async Task Iban_approval_uses_the_change_maker_not_the_card_maker_and_retains_original_receipt_after_later_change()
    {
        await using var run = await Run.StartAsync(); var id = await IbanSafetyPending(run, changeMaker: Approver);
        await using var scope = new BankSafetyScope(run);
        var self = await IbanSafetyApprove(scope, id, "iban-self");
        Assert.Equal(CommandOutcome.Denied, self.Outcome); Assert.Equal("finance_approver_is_proposer", Assert.Single(self.Issues).Code);
        scope.User.Actor = Proposer; // The original card maker did not prepare this IBAN change and can approve it (D4).
        var original = await IbanSafetyApprove(scope, id, "iban-original");
        Assert.Equal(CommandOutcome.Succeeded, original.Outcome); Assert.Equal("4", original.Resource!.Version);
        await using (var later = run.Open(CompanyA, Approver))
            Assert.Equal(CommandOutcome.Succeeded, (await later.Accounts.ProposeIbanChangeAsync(later.Context, Key("iban-later"),
                new(id, "4", "TR330006100519786457841326", "SENTETIK later request"), default)).Outcome);
        var replay = await IbanSafetyApprove(scope, id, "iban-original");
        Assert.True(replay.IsReplay); Assert.Equal(original.Resource, replay.Resource); Assert.Equal(original.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(5L, await run.ScalarAsync<long>("SELECT Version FROM finance.BankAccounts WHERE Id=@id", ("@id", id)));
        scope.Permissions.BeforeRead = () => PermissionDecision.Denied;
        Assert.Equal(CommandOutcome.Denied, (await IbanSafetyApprove(scope, id, "iban-original")).Outcome);
        await AssertIbanSafetyDurable(run, id, version: 5, commandCount: 1, auditCount: 1);
    }

    [SqlServerFact]
    public async Task Iban_approval_terminal_receipt_is_owned_and_rollback_failure_cleans_actual_pending_work_without_replay()
    {
        await using var run = await Run.StartAsync(); var id = await IbanSafetyPending(run);
        await using (var scope = new BankSafetyScope(run))
        {
            var first = await IbanSafetyApprove(scope, id, "iban-terminal", "99");
            Assert.Equal(CommandOutcome.VersionConflict, first.Outcome); Assert.False(first.IsReplay);
            Assert.Equal(CommandOutcome.Succeeded, (await IbanSafetyApprove(scope, id, "iban-success")).Outcome);
            var replay = await IbanSafetyApprove(scope, id, "iban-terminal", "99");
            Assert.True(replay.IsReplay); Assert.Equal(first.Outcome, replay.Outcome); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
            await AssertIbanSafetyDurable(run, id, version: 4, commandCount: 2, auditCount: 1);
        }
        var failedId = await IbanSafetyPending(run); var rollback = new BankSafetyRollbackFailure();
        await using var failing = new BankSafetyScope(run, extra: rollback); var observed = false;
        failing.Permissions.BeforeRead = () =>
        { if (failing.Writer.HasPendingDrafts) { observed = true; return PermissionDecision.Denied; } return PermissionDecision.Allowed; };
        await Assert.ThrowsAsync<InvalidOperationException>(() => IbanSafetyApprove(failing, failedId, "iban-rollback-fails"));
        Assert.True(observed); Assert.Equal(1, rollback.Attempts); Assert.Empty(failing.Database.ChangeTracker.Entries());
        Assert.False(failing.Writer.HasPendingDrafts); Assert.Null(failing.Database.Database.CurrentTransaction);
        await AssertIbanSafetyDurable(run, failedId, version: 3, commandCount: 0, auditCount: 0);
    }

    private static Task<BankAccountCommandResult> IbanSafetyApprove(BankSafetyScope scope, Guid id, string key, string version = "3") =>
        scope.Service.ApproveIbanChangeAsync(new(CompanyA, scope.User.Actor, "SENTETIK-iban-" + key, null), Key(key), new(id, version), default);

    private static async Task<Guid> IbanSafetyPending(Run run, string changeMaker = Proposer)
    {
        var id = await BankSafetyProposal(run);
        await using (var approval = new BankSafetyScope(run)) Assert.Equal(CommandOutcome.Succeeded, (await approval.Approve(id, "iban-card-" + id)).Outcome);
        await using var proposer = run.Open(CompanyA, changeMaker);
        var pending = await proposer.Accounts.ProposeIbanChangeAsync(proposer.Context, Key("iban-request-" + id),
            new(id, "2", IbanSafetyValue, "SENTETIK immutable IBAN reason"), default);
        Assert.Equal(CommandOutcome.Succeeded, pending.Outcome); Assert.Equal("3", pending.Resource!.Version);
        return id;
    }

    private static async Task AssertIbanSafetyDurable(Run run, Guid id, long version, int commandCount, int auditCount)
    {
        Assert.Equal(version, await run.ScalarAsync<long>("SELECT Version FROM finance.BankAccounts WHERE Id=@id", ("@id", id)));
        Assert.Equal(commandCount, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands WHERE CommandName=@command AND BankAccountId=@id",
            ("@command", FinanceBankAccountService.ApproveIbanChangeCommand), ("@id", id)));
        Assert.Equal(auditCount, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=@event AND EntityId=@id",
            ("@event", IbanSafetyApprovedEvent), ("@id", id.ToString("D"))));
    }
}
