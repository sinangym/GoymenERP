using System.Data.Common;
using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using GoymenERP.Modules.Finance.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceExpensePaymentSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Book_value_exact_proposal_read_uses_only_approval_authority_retained_source_and_original_native_decision_receipt()
    {
        await using var run = await Run.StartAsync(); var transfer = await run.RecordBookValueExchangeAsync();
        CashExchangeBookValueViewV1 pending;
        await using (var maker = run.OpenBookValue("SENTETIK-book-maker")) pending = (await maker.Service.ProposeAsync(maker.Context, Key("book-purpose-propose"), BookRequest(transfer), default)).Resource!;
        var authority = new BookAuthority(); authority.BeforeRead = permission => Assert.Equal(CashExchangeBookValuePermissionsV1.Approve, permission);
        await using var checker = run.OpenBookValue("SENTETIK-book-checker", authority: authority); var callbacks = 0;
        var detail = await checker.Service.ReadDecisionAsync(checker.Context, pending.Id, _ => { callbacks++; return Task.CompletedTask; }, default);
        Assert.Equal(ReadOutcome.Found, detail.Outcome); Assert.Equal(6, callbacks); Assert.NotNull(detail.Resource!.DecisionProof);
        Assert.Equal(transfer, detail.Resource.Transfer); Assert.Equal(pending, detail.Resource.Proposal);
        Assert.Equal(pending.TransferSourceHash, detail.Resource.DecisionProof.TransferSourceHash);
        var approved = await checker.Service.DecideAsync(checker.Context, Key("book-purpose-approve"), new(pending.Id, "1", true, null), default);
        Assert.Equal(CommandOutcome.Succeeded, approved.Outcome); Assert.NotNull(approved.DecisionReceipt);
        Assert.Equal((pending.Id, transfer.Id, "1", "2", checker.Context.ActorId), (approved.DecisionReceipt.ProposalId, approved.DecisionReceipt.TransferId, approved.DecisionReceipt.ExpectedVersion, approved.DecisionReceipt.ResultVersion, approved.DecisionReceipt.ActorId));
        var replay = await checker.Service.DecideAsync(checker.Context with { CorrelationId = "SENTETIK-later" }, Key("book-purpose-approve"), new(pending.Id, "1", true, null), default);
        Assert.True(replay.IsReplay); Assert.Equal(approved.DecisionReceipt, replay.DecisionReceipt); Assert.Equal(approved.OriginalCorrelationId, replay.OriginalCorrelationId);
        var stored = await run.ScalarAsync<string>("SELECT ResultJson FROM finance.CashExchangeBookValueCommands WHERE CompanyId=@company AND Command=@command AND KeyHash=@key", ("@company", CompanyA), ("@command", CashExchangeBookValueServiceV1.DecideCommand), ("@key", FinanceCommandKeys.Hash(Key("book-purpose-approve").IdempotencyKey)));
        Assert.Equal(stored, JsonSerializer.Serialize(approved, FinanceJson.CreateOptions()));
        using var wire = JsonDocument.Parse(stored); Assert.Equal(5, wire.RootElement.EnumerateObject().Count()); Assert.False(wire.RootElement.TryGetProperty("decisionReceipt", out _));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashTransfers"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueDecisions"));
    }

    [SqlServerFact]
    public async Task Book_value_human_read_refuses_maker_foreign_company_and_consumer_cancellation_without_writes()
    {
        await using var run = await Run.StartAsync(); var transfer = await run.RecordBookValueExchangeAsync(); CashExchangeBookValueViewV1 pending;
        await using (var maker = run.OpenBookValue("SENTETIK-book-maker"))
        {
            pending = (await maker.Service.ProposeAsync(maker.Context, Key("book-read-propose"), BookRequest(transfer), default)).Resource!;
            Assert.Equal(ReadOutcome.Denied, (await maker.Service.ReadDecisionAsync(maker.Context, pending.Id, _ => Task.CompletedTask, default)).Outcome);
        }
        await using (var foreign = run.OpenBookValue("SENTETIK-book-checker", CompanyB))
            Assert.Equal(ReadOutcome.NotFound, (await foreign.Service.ReadDecisionAsync(foreign.Context, pending.Id, _ => Task.CompletedTask, default)).Outcome);
        await using var checker = run.OpenBookValue("SENTETIK-book-checker");
        for (var stopAt = 1; stopAt <= 6; stopAt++)
        {
            using var cts = new CancellationTokenSource(); var calls = 0;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checker.Service.ReadDecisionAsync(checker.Context, pending.Id,
                _ => { if (++calls == stopAt) cts.Cancel(); return Task.CompletedTask; }, cts.Token)); Assert.Equal(stopAt, calls);
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueDecisions"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueCommands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages WHERE EventType=N'finance.cash-exchange.book-value-approved.v1'"));
    }

    [SqlServerFact]
    public async Task Book_value_final_consumer_normal_completion_after_authority_revocation_returns_Denied_without_human_facts()
    {
        await using var run = await Run.StartAsync(); var transfer = await run.RecordBookValueExchangeAsync(); CashExchangeBookValueViewV1 pending;
        await using (var maker = run.OpenBookValue("SENTETIK-book-maker")) pending = (await maker.Service.ProposeAsync(maker.Context, Key("book-final-fence-p"), BookRequest(transfer), default)).Resource!;
        var authority = new BookAuthority(); await using var checker = run.OpenBookValue("SENTETIK-book-checker", authority: authority); var calls = 0;
        var result = await checker.Service.ReadDecisionAsync(checker.Context, pending.Id,
            _ => { if (++calls == 6) authority.Granted = false; return Task.CompletedTask; }, default);
        Assert.Equal(6, calls); Assert.False(authority.Granted); Assert.Equal(ReadOutcome.Denied, result.Outcome); Assert.Null(result.Resource);
        Assert.False(checker.Db.ChangeTracker.HasChanges()); Assert.Null(checker.Db.Database.CurrentTransaction);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueDecisions"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueCommands"));
    }

    [SqlServerFact]
    public async Task Book_value_receipt_before_after_commands_and_source_read_revocation_roll_back_actual_decision_audit_outbox_then_retry_once()
    {
        // Existing native Save: postSave(1), audit-scope(2), postAudit(3), receipt-before/afterCommands/afterSource(4/5/6).
        foreach (var approve in new[] { false, true })
        foreach (var stopAt in new[] { 4, 5, 6 })
        {
            await using var run = await Run.StartAsync(); var transfer = await run.RecordBookValueExchangeAsync(); CashExchangeBookValueViewV1 pending;
            await using (var maker = run.OpenBookValue("SENTETIK-book-maker")) pending = (await maker.Service.ProposeAsync(maker.Context, Key("book-read-fence-p"), BookRequest(transfer), default)).Resource!;
            var authority = new BookAuthority(); var saved = false; var calls = 0; var revokedAt = 0;
            authority.BeforeRead = _ => { if (saved && ++calls == stopAt) { revokedAt = calls; authority.Granted = false; } };
            await using (var checker = run.OpenBookValue("SENTETIK-book-checker", authority: authority, interceptors: [new BookSavedLoss(() => saved = true)]))
            {
                var refused = await checker.Service.DecideAsync(checker.Context, Key("book-receipt-fence"), new(pending.Id, "1", approve, approve ? null : "SENTETIK ret"), default);
                Assert.Equal(CommandOutcome.Denied, refused.Outcome); Assert.Null(refused.Resource); Assert.Null(refused.DecisionReceipt); Assert.True(saved); Assert.Equal(stopAt, revokedAt); Assert.True(calls >= stopAt);
                Assert.False(checker.Db.ChangeTracker.HasChanges()); Assert.Null(checker.Db.Database.CurrentTransaction);
            }
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueDecisions"));
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueCommands"));
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Finance.CashExchangeBookValue'"));
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages WHERE EventType=N'finance.cash-exchange.book-value-approved.v1'"));
            await using var retry = run.OpenBookValue("SENTETIK-book-checker"); var request = new DecideCashExchangeBookValueV1(pending.Id, "1", approve, approve ? null : "SENTETIK ret");
            var first = await retry.Service.DecideAsync(retry.Context, Key("book-receipt-fence"), request, default); Assert.Equal(CommandOutcome.Succeeded, first.Outcome); Assert.NotNull(first.DecisionReceipt);
            var replay = await retry.Service.DecideAsync(retry.Context, Key("book-receipt-fence"), request, default); Assert.True(replay.IsReplay); Assert.Equal(first.DecisionReceipt, replay.DecisionReceipt);
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueDecisions")); Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueCommands"));
            Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Finance.CashExchangeBookValue'"));
            Assert.Equal(approve ? 1 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages WHERE EventType=N'finance.cash-exchange.book-value-approved.v1'"));
        }
    }

    [SqlServerFact]
    public async Task Book_value_postcommit_authority_loss_suppresses_response_without_claiming_the_decision_was_rolled_back()
    {
        await using var run = await Run.StartAsync(); var transfer = await run.RecordBookValueExchangeAsync(); CashExchangeBookValueViewV1 pending;
        await using (var maker = run.OpenBookValue("SENTETIK-book-maker")) pending = (await maker.Service.ProposeAsync(maker.Context, Key("book-release-p"), BookRequest(transfer), default)).Resource!;
        var authority = new BookAuthority(); var commit = new BookDecisionCommitted(() => authority.Granted = false);
        await using (var checker = run.OpenBookValue("SENTETIK-book-checker", authority: authority, interceptors: [commit]))
        {
            Assert.Equal(CommandOutcome.Denied, (await checker.Service.DecideAsync(checker.Context, Key("book-release"), new(pending.Id, "1", true, null), default)).Outcome); Assert.Equal(1, commit.Count);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueDecisions"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueCommands"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Finance.CashExchangeBookValue'"));
        await using var restored = run.OpenBookValue("SENTETIK-book-checker"); var replay = await restored.Service.DecideAsync(restored.Context, Key("book-release"), new(pending.Id, "1", true, null), default);
        Assert.True(replay.IsReplay); Assert.NotNull(replay.DecisionReceipt); Assert.Equal(run.Correlation, replay.OriginalCorrelationId);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueDecisions"));
    }
    [SqlServerFact]
    public async Task Book_value_decision_preserves_caller_dirty_and_pending_audit_on_both_sides_of_the_first_authority_await()
    {
        await using var run = await Run.StartAsync(); var transfer = await run.RecordBookValueExchangeAsync(); CashExchangeBookValueViewV1 pending;
        await using (var maker = run.OpenBookValue("SENTETIK-book-maker")) pending = (await maker.Service.ProposeAsync(maker.Context, Key("book-caller-p"), BookRequest(transfer), default)).Resource!;
        foreach (var auditPending in new[] { false, true })
        foreach (var duringAwait in new[] { false, true })
        {
            var authority = new BookAuthority(); await using var checker = run.OpenBookValue("SENTETIK-book-checker", authority: authority);
            var collector = checker.Scope.ServiceProvider.GetRequiredService<IAuditEventCollector>();
            var writer = checker.Scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var row = new CashExchangeBookValueProposalRow { Id = Guid.NewGuid(), CompanyId = CompanyA }; var added = false;
            void AddCaller()
            {
                if (added) return; added = true;
                if (auditPending) collector.Enqueue(new AuditEventDraft(CompanyA, CashExchangeBookValueServiceV1.DecideCommand, "Finance.CashExchangeBookValue", pending.Id.ToString("D"),
                    "Decide", 1, 2, AuditActorTypes.User, checker.Context.ActorId, CashExchangeBookValuePermissionsV1.Approve, [], "{}", "{}", "SENTETIK caller work", null,
                    AuditAiInvolvement.None, null, "Web", null, null, checker.Context.CorrelationId, null, "SENTETIK-caller-key"));
                else checker.Db.CashExchangeBookValueProposals.Add(row);
            }
            if (duringAwait) authority.BeforeRead = _ => AddCaller(); else AddCaller();
            var refusal = await checker.Service.DecideAsync(checker.Context, Key("book-caller"), new(pending.Id, "1", true, null), default);
            Assert.Equal(CommandOutcome.Unavailable, refusal.Outcome); Assert.True(added);
            Assert.Equal(auditPending, writer.HasPendingDrafts); Assert.Equal(!auditPending, checker.Db.ChangeTracker.HasChanges());
            if (!auditPending) Assert.Same(row, Assert.Single(checker.Db.ChangeTracker.Entries()).Entity);
            Assert.Null(checker.Db.Database.CurrentTransaction);
            // Test-owned caller work is discarded only after proving the command preserved it.
            checker.Db.ChangeTracker.Clear(); writer.DiscardPending();
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueDecisions"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueCommands"));
    }

    private sealed class BookDecisionCommitted(Action action) : DbTransactionInterceptor
    { public int Count; public override Task TransactionCommittedAsync(DbTransaction tx, TransactionEndEventData data, CancellationToken ct = default) { Count++; action(); return Task.CompletedTask; } }
}
