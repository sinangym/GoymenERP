using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceExpensePaymentSqlServerRelationalAcceptanceTests
{
    // Actual migrated SQL and scoped native Audit writer; Settings/treasury/permission ports remain controlled.
    [SqlServerFact]
    public async Task Native_expense_approval_receipt_retains_revised_draft_version_and_original_approved_facts_after_separate_record()
    {
        await using var run = await Run.StartAsync(); var id = await ApprovalSafetyPrepare(run);
        Guid bank;
        await using (var lookup = run.Open(CompanyA, Preparer))
            bank = (await lookup.Payments.DetailAuthorizedAsync(lookup.Context, id, default)).Resource!.Preparation.TreasuryAccountId;
        await using (var editor = run.Open(CompanyA, "SENTETIK-expense-editor"))
        {
            var revised = await editor.Payments.DraftSaveAsync(editor.Context, Key("expense-purpose-revise"), Draft(bank) with { PaymentId = id, ExpectedVersion = "1" }, default);
            Assert.Equal(CommandOutcome.Succeeded, revised.Outcome); Assert.Equal("2", revised.Resource!.Version);
        }
        await using var scope = new ExpenseApprovalScope(run); var reader = Assert.IsAssignableFrom<IExpensePaymentDecisionReadV1>(scope.Service);
        var context = new FinanceContext(CompanyA, Approver, scope.OriginalCorrelation, null);
        var pending = await reader.ReadDecisionAsync(context, id, _ => Task.CompletedTask, default);
        Assert.Equal(ReadOutcome.Found, pending.Outcome); Assert.Equal("2", pending.Resource!.Payment.Version);
        Assert.Contains("SENTETIK-expense-editor", pending.Resource.Revisers);
        var key = "expense-purpose-approved"; var first = await scope.Approve(id, key, "2");
        Assert.Equal(CommandOutcome.Succeeded, first.Outcome); var proof = Assert.IsType<ExpensePaymentDecisionReceiptV1>(first.DecisionReceipt);
        Assert.Equal((CompanyA, id, "2", "3", Approver, scope.OriginalCorrelation),
            (proof.CompanyId, proof.PaymentId, proof.ExpectedVersion, proof.ResultVersion, proof.ActorId, proof.OriginalCorrelationId));
        Assert.Equal(pending.Resource.DraftFactsDigest, proof.DraftFactsDigest);
        var command = await scope.Db.ExpensePaymentCommands.AsNoTracking().SingleAsync(x => x.CompanyId == CompanyA && x.PaymentId == id && x.CommandName == ExpensePaymentService.ApproveCommand);
        Assert.Equal(command.PayloadHash, proof.PayloadHash); Assert.Equal(command.RecordedAtUtc, proof.RecordedAtUtc);
        Assert.Equal(command.ResultJson, JsonSerializer.Serialize(first, FinanceJson.CreateOptions()));
        Assert.Equal(ExpensePaymentService.OriginalReceiptDigest(first), proof.OriginalReceiptDigest);
        using var json = JsonDocument.Parse(command.ResultJson); Assert.Equal(5, json.RootElement.EnumerateObject().Count());
        Assert.False(json.RootElement.TryGetProperty("decisionReceipt", out _)); Assert.False(first.Resource!.PaymentConfirmed);
        // An independent, explicitly confirmed native Record; Work Approve itself still cannot record or pay.
        await using (var recorder = run.Open(CompanyA, Recorder))
        {
            var recorded = await recorder.Payments.RecordAsync(recorder.Context, Key("expense-purpose-record"), new(id, "3", true), default);
            Assert.Equal(CommandOutcome.Succeeded, recorded.Outcome); Assert.Equal("4", recorded.Resource!.Version);
            Assert.True(recorded.Resource.PaymentConfirmed); Assert.NotNull(recorded.Resource.DocumentNumber);
        }
        var current = await reader.ReadDecisionAsync(context, id, _ => Task.CompletedTask, default);
        Assert.Equal(ReadOutcome.Found, current.Outcome); Assert.Equal(ExpensePaymentStatus.Recorded, current.Resource!.Payment.Status);
        Assert.Equal("4", current.Resource.Payment.Version); Assert.Equal(proof.DraftFactsDigest, current.Resource.DraftFactsDigest);
        scope.Correlation = "SENTETIK-later-retry";
        var replay = await scope.Approve(id, key, "2"); Assert.Equal(CommandOutcome.Succeeded, replay.Outcome); Assert.True(replay.IsReplay);
        Assert.Equal(first.Resource, replay.Resource); Assert.Equal(proof, replay.DecisionReceipt); Assert.Equal("3", replay.Resource!.Version);
        Assert.False(replay.Resource.PaymentConfirmed); Assert.Null(replay.Resource.DocumentNumber); scope.AssertClean();
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ExpensePaymentCommands WHERE CompanyId=@c AND PaymentId=@p AND CommandName=@n",
            ("@c", CompanyA), ("@p", id), ("@n", ExpensePaymentService.ApproveCommand)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId=@c AND EntityId=@p AND EventType=N'finance.expense-payment.approved.v1'", ("@c", CompanyA), ("@p", id.ToString("D"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEvents"));
    }

    [SqlServerFact]
    public async Task Native_expense_approval_receipt_read_fences_roll_back_all_native_effects_and_same_key_later_executes_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var receiptFence in new[] { 2, 3 })
        {
            var id = await ApprovalSafetyPrepare(run); var save = new ExpenseApprovalSave(id);
            await using var scope = new ExpenseApprovalScope(run, save: save); var savedFences = 0; var deny = true;
            // After native Save (1), immediately before Commands (2), immediately after Commands (3).
            scope.Permission.Check = () => !save.Saved || !deny || ++savedFences < receiptFence ? PermissionDecision.Allowed : PermissionDecision.Denied;
            var key = "expense-receipt-fence-" + id;
            var refused = await scope.Approve(id, key); Assert.Equal(CommandOutcome.Denied, refused.Outcome); Assert.Null(refused.DecisionReceipt);
            Assert.Equal(receiptFence, savedFences); Assert.True(save.Saved); scope.AssertClean(); await ApprovalSafetyDurable(run, id, false, 0);
            deny = false; save.Saved = false;
            var success = await scope.Approve(id, key); Assert.Equal(CommandOutcome.Succeeded, success.Outcome); Assert.NotNull(success.DecisionReceipt);
            scope.AssertClean(); await ApprovalSafetyDurable(run, id, true, 1);
        }
    }

    [SqlServerFact]
    public async Task Native_expense_purpose_read_refuses_maker_reviser_foreign_company_and_late_consumer_loss_without_new_owner_writes()
    {
        await using var run = await Run.StartAsync(); var id = await ApprovalSafetyPrepare(run);
        Guid bank;
        await using (var maker = run.Open(CompanyA, Preparer))
            bank = (await maker.Payments.DetailAuthorizedAsync(maker.Context, id, default)).Resource!.Preparation.TreasuryAccountId;
        const string editorActor = "SENTETIK-expense-reviser";
        await using (var editor = run.Open(CompanyA, editorActor))
            Assert.Equal(CommandOutcome.Succeeded, (await editor.Payments.DraftSaveAsync(editor.Context, Key("expense-purpose-editor"), Draft(bank) with { PaymentId = id, ExpectedVersion = "1" }, default)).Outcome);
        await using var scope = new ExpenseApprovalScope(run); var reader = Assert.IsAssignableFrom<IExpensePaymentDecisionReadV1>(scope.Service);
        var context = new FinanceContext(CompanyA, Approver, scope.OriginalCorrelation, null);
        var before = await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ExpensePaymentCommands");
        foreach (var actor in new[] { Preparer, editorActor })
        {
            scope.User.Actor = actor;
            Assert.Equal(ReadOutcome.Denied, (await reader.ReadDecisionAsync(context with { ActorId = actor }, id, _ => Task.CompletedTask, default)).Outcome);
        }
        scope.User.Actor = Approver;
        Assert.Equal(ReadOutcome.Denied, (await reader.ReadDecisionAsync(context with { CompanyId = CompanyB }, id, _ => Task.CompletedTask, default)).Outcome);
        var calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadDecisionAsync(context, id,
            _ => { if (++calls == 7) throw new InvalidOperationException("SENTETIK consumer revoked after treasury label"); return Task.CompletedTask; }, default));
        Assert.Equal(before, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ExpensePaymentCommands")); scope.AssertClean();
    }
}
