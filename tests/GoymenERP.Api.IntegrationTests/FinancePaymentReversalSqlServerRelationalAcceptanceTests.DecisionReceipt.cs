using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinancePaymentReversalSqlServerRelationalAcceptanceTests
{
    // Reuses migrated SQL, native proposal/decision commands and the SAME real scoped audit collector/writer.
    // The pre-existing upstream recorded-payment fixture and permission/delegation timings remain controlled.
    [SqlServerFact]
    public async Task Payment_reversal_owner_receipt_and_preview_bind_original_native_result_after_rejection_and_another_proposal()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var (payment, proposal) = await DecisionSafetyPrepare(run); await using var scope = new DecisionSafetyScope(run);
            var context = new FinanceContext(CompanyA, Checker, scope.Correlation, null);
            var reader = Assert.IsAssignableFrom<IPaymentReversalDecisionPreviewReadV1>(scope.Service);
            var callbackCount = 0;
            var pending = await reader.ReadDecisionPreviewAsync(context, proposal, _ => { callbackCount++; return Task.CompletedTask; }, default);
            Assert.Equal(ReadOutcome.Found, pending.Outcome); var human = Assert.IsType<PaymentReversalDecisionPreviewV1>(pending.Resource);
            Assert.Equal(4, callbackCount); Assert.Equal(ReversalProposalStatus.Pending, human.Proposal.Status);
            Assert.Equal(payment, human.Proposal.PaymentId); Assert.Equal("1250.50", human.Amount); Assert.Equal(CompanyA, human.CompanyId);
            var key = "receipt-" + proposal;
            var result = await scope.Decide(proposal, approve, key); Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            var proof = Assert.IsType<PaymentReversalDecisionReceiptV1>(result.DecisionReceipt);
            Assert.Equal(human.RecordedFactsDigest, proof.RecordedFactsDigest); Assert.Equal(proposal, proof.ProposalId);
            Assert.Equal(payment, proof.PaymentId); Assert.Equal("1", proof.ExpectedVersion); Assert.Equal("2", proof.ResultVersion);
            Assert.Equal(Checker, proof.ActorId); Assert.Equal(scope.OriginalCorrelation, proof.OriginalCorrelationId);
            var command = approve ? PaymentReversalProposalService.ApproveCommand : PaymentReversalProposalService.RejectCommand;
            var persisted = await scope.Database.Commands.AsNoTracking().SingleAsync(row => row.CommandName == command
                && row.IdempotencyKeyHash == FinanceCommandKeys.Hash(key));
            Assert.Equal(persisted.PayloadHash, proof.PayloadHash); Assert.Equal(persisted.RecordedAtUtc, proof.RecordedAtUtc);
            using var stored = JsonDocument.Parse(persisted.ResultJson);
            Assert.Equal(5, stored.RootElement.EnumerateObject().Count()); Assert.False(stored.RootElement.TryGetProperty("decisionReceipt", out _));
            var original = result.Resource;
            var terminal = await reader.ReadDecisionPreviewAsync(context, proposal, _ => Task.CompletedTask, default);
            Assert.Equal(ReadOutcome.Found, terminal.Outcome); Assert.Equal(original, terminal.Resource!.Proposal);
            Assert.Equal(human.RecordedFactsDigest, terminal.Resource!.RecordedFactsDigest);
            if (!approve)
            {
                var replacement = await run.ProposeAsync("SENTETIK-new-maker", "replacement-" + payment, payment);
                Assert.Equal(CommandOutcome.Succeeded, replacement.Outcome); Assert.NotEqual(proposal, replacement.Resource!.Id);
            }
            scope.Correlation = "original-replay-new-correlation";
            var replay = await scope.Decide(proposal, approve, key);
            Assert.Equal(CommandOutcome.Succeeded, replay.Outcome); Assert.True(replay.IsReplay);
            Assert.Equal(original, replay.Resource); Assert.Equal(proof, replay.DecisionReceipt);
            scope.AssertClean();
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands WHERE CompanyId=@c AND CommandName=@n AND IdempotencyKeyHash=@k",
                ("@c", CompanyA), ("@n", command), ("@k", FinanceCommandKeys.Hash(key))));
            Assert.Equal(approve ? 1 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEventReversals WHERE CompanyId=@c AND ProposalId=@p", ("@c", CompanyA), ("@p", proposal)));
        }
    }

    [SqlServerFact]
    public async Task Payment_reversal_receipt_read_fences_roll_back_every_native_effect_and_same_key_later_executes_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        foreach (var receiptFence in new[] { 2, 3 })
        {
            var (payment, proposal) = await DecisionSafetyPrepare(run); var save = new DecisionSafetySave(proposal);
            await using var scope = new DecisionSafetyScope(run, save: save); var savedFences = 0; var deny = true;
            scope.Permissions.Check = () => !save.Saved || !deny || ++savedFences < receiptFence
                ? PermissionDecision.Allowed : PermissionDecision.Denied;
            var key = "receipt-release-" + proposal;
            var refused = await scope.Decide(proposal, approve, key);
            Assert.Equal(CommandOutcome.Denied, refused.Outcome); Assert.Null(refused.DecisionReceipt);
            Assert.Equal(receiptFence, savedFences); Assert.True(save.Saved); scope.AssertClean();
            await DecisionSafetyDurable(run, payment, proposal, 1, ReversalProposalStatuses.Pending, 0, 0, 0);
            deny = false; save.Saved = false;
            var result = await scope.Decide(proposal, approve, key);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome); Assert.NotNull(result.DecisionReceipt); scope.AssertClean();
            await DecisionSafetyDurable(run, payment, proposal, 2,
                approve ? ReversalProposalStatuses.Approved : ReversalProposalStatuses.Rejected, 1, 1, approve ? 1 : 0);
        }
    }

    [SqlServerFact]
    public async Task Payment_reversal_purpose_human_read_is_approve_only_maker_excluding_and_refuses_late_consumer_or_company_loss_without_writes()
    {
        await using var run = await Run.StartAsync(); var (payment, proposal) = await DecisionSafetyPrepare(run);
        await using var scope = new DecisionSafetyScope(run); var reader = Assert.IsAssignableFrom<IPaymentReversalDecisionPreviewReadV1>(scope.Service);
        var context = new FinanceContext(CompanyA, Checker, scope.Correlation, null);
        scope.User.Actor = Maker;
        var maker = await reader.ReadDecisionPreviewAsync(context with { ActorId = Maker }, proposal, _ => Task.CompletedTask, default);
        Assert.Equal(ReadOutcome.Denied, maker.Outcome); Assert.Null(maker.Resource);
        scope.User.Actor = Checker; scope.Permissions.Check = () => PermissionDecision.Denied;
        Assert.Equal(ReadOutcome.Denied, (await reader.ReadDecisionPreviewAsync(context, proposal, _ => Task.CompletedTask, default)).Outcome);
        scope.Permissions.Check = () => PermissionDecision.Allowed;
        Assert.Equal(ReadOutcome.Denied, (await reader.ReadDecisionPreviewAsync(context with { CompanyId = TestCompanyIds.CompanyB }, proposal, _ => Task.CompletedTask, default)).Outcome);
        var calls = 0;
        var late = await reader.ReadDecisionPreviewAsync(context, proposal, _ => { if (++calls == 3) scope.Company.Set(TestCompanyIds.CompanyB); return Task.CompletedTask; }, default);
        Assert.Equal(ReadOutcome.Denied, late.Outcome); Assert.Null(late.Resource);
        scope.Company.Set(CompanyA); calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadDecisionPreviewAsync(context, proposal,
            _ => { if (++calls == 3) throw new InvalidOperationException("SENTETIK consumer delegation revoked"); return Task.CompletedTask; }, default));
        scope.AssertClean(); await DecisionSafetyDurable(run, payment, proposal, 1, ReversalProposalStatuses.Pending, 0, 0, 0);
    }
}
