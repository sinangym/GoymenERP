using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingCostDifferenceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Cost_difference_purpose_read_preserves_full_recorded_facts_without_live_plan_and_refuses_maker_or_final_consumer_loss()
    {
        await using var run = await Run.StartAsync(); var id = await SafetyPrepare(run); var counts = await SafetyCounts(run);
        await using var scope = new CostApprovalScope(run); scope.CurrentPlan.Missing = true;
        var answer = await scope.Service.ReadDecisionAsync(id, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Ok, answer.Status); var detail = Assert.IsType<ManufacturingCostDifferenceDecisionDetailV1>(answer.Resource);
        Assert.Equal("Prepared", detail.Proposal.Status); Assert.Equal(2L, detail.Proposal.Version); Assert.Equal(Maker, detail.Proposal.PreparedBy);
        var output = Assert.Single(detail.Proposal.Outputs); Assert.Equal("600", output.ConsumedTlDifference); Assert.Equal("2000", output.OriginalTlAmount);
        Assert.NotNull(detail.Proposal.AccountPlan); Assert.Equal(0, scope.CurrentPlan.Reads); Assert.Equal(0, scope.Currencies.Reads);
        Assert.Equal(20, JsonSerializer.SerializeToElement(detail.Proposal, new JsonSerializerOptions(JsonSerializerDefaults.Web)).EnumerateObject().Count());
        scope.User.Actor = Maker; Assert.Equal(AccountingApiStatus.Denied, (await scope.Service.ReadDecisionAsync(id, _ => Task.CompletedTask)).Status);
        scope.User.Actor = Checker;
        foreach (var cancel in new[] { false, true })
        {
            using var cts = new CancellationTokenSource(); var callbacks = 0; var grant = true; scope.Authority.Read = () => grant;
            Task Consumer(CancellationToken _) { if (++callbacks == 4) { if (cancel) cts.Cancel(); else grant = false; } return Task.CompletedTask; }
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Service.ReadDecisionAsync(id, Consumer, cts.Token));
            else { var denied = await scope.Service.ReadDecisionAsync(id, Consumer); Assert.Equal(AccountingApiStatus.Denied, denied.Status); Assert.Null(denied.Resource); }
            Assert.Equal(4, callbacks);
        }
        scope.Authority.Read = null; scope.Company.Set(TestCompanyIds.CompanyB); scope.User.Member = false;
        Assert.Equal(AccountingApiStatus.Denied, (await scope.Service.ReadDecisionAsync(id, _ => Task.CompletedTask)).Status);
        Assert.Equal(counts, await SafetyCounts(run)); scope.AssertClean();
    }

    [SqlServerFact]
    public async Task Cost_difference_private_receipt_is_bound_to_actual_original_command_and_journal_after_live_plan_changes()
    {
        await using var run = await Run.StartAsync(); var id = await SafetyPrepare(run); await using var scope = new CostApprovalScope(run);
        var preview = await scope.Service.ReadDecisionAsync(id, _ => Task.CompletedTask);
        var first = await scope.Approve(id, "decision-receipt"); var counts = await SafetyCounts(run);
        var receipt = Assert.IsType<ManufacturingCostDifferenceApprovalReceiptV1>(first.Resource?.ApprovalReceipt);
        Assert.Equal(preview.Resource?.SourceFactsDigest, receipt.SourceFactsDigest); Assert.Equal(2L, receipt.ExpectedVersion); Assert.Equal(3L, receipt.ResultVersion);
        Assert.Equal(first.Resource?.JournalId, receipt.JournalId); Assert.Equal(Checker, receipt.ActorId); Assert.Equal(Maker, receipt.PreparedBy);
        Assert.Equal(scope.CorrelationId, receipt.OriginalCorrelationId);
        Assert.Equal(ManufacturingCostDifferenceServiceV1.CostDifferenceOriginalResultDigest(first), receipt.OriginalResultDigest);
        scope.CurrentPlan.Missing = true; var reads = scope.CurrentPlan.Reads; var currencies = scope.Currencies.Reads;
        var replay = await scope.Approve(id, "decision-receipt"); var terminal = await scope.Service.ReadDecisionAsync(id, _ => Task.CompletedTask);
        Assert.True(replay.IsReplay); Assert.Equal(receipt, replay.Resource?.ApprovalReceipt); Assert.Equal(first.Resource?.JournalId, replay.Resource?.JournalId);
        Assert.Equal(receipt.SourceFactsDigest, terminal.Resource?.SourceFactsDigest); Assert.Equal("Posted", terminal.Resource?.Proposal.Status);
        Assert.NotEqual(preview.Resource?.PreviewDigest, terminal.Resource?.PreviewDigest); Assert.Equal(reads, scope.CurrentPlan.Reads); Assert.Equal(currencies, scope.Currencies.Reads);
        Assert.DoesNotContain("approvalReceipt", JsonSerializer.Serialize(first, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(counts, await SafetyCounts(run)); scope.AssertClean();
    }

    [SqlServerFact]
    public async Task Cost_difference_each_new_saved_receipt_read_fence_rolls_back_journal_command_audit_and_leaves_original_key_retry_once()
    {
        // No lineage: journal Save1/Receive2/store3/decision preSave4/postAudit5/decision Save6/store7/service8;
        // receipt before9/command10/source-before11/source-row12/source-journal13/receipt-final14 precede owner15/precommit16.
        foreach (var phase in new[] { 9, 10, 11, 12, 13, 14 })
        {
            await using var run = await Run.StartAsync(); var id = await SafetyPrepare(run); var before = await SafetyCounts(run);
            var save = new CostApprovalSave(id); await using var scope = new CostApprovalScope(run, save); var savedFences = 0; var grant = true;
            scope.Authority.Read = () => { if (save.Saved && ++savedFences == phase) grant = false; return grant; };
            var denied = await scope.Approve(id, "receipt-read-fence"); Assert.Equal(AccountingApiStatus.Denied, denied.Status); Assert.Null(denied.Resource);
            Assert.Equal(phase, savedFences); Assert.Equal(2, save.Saves); Assert.Equal(before, await SafetyCounts(run)); scope.AssertClean();
            grant = true; scope.Authority.Read = null;
            var first = await scope.Approve(id, "receipt-read-fence"); var replay = await scope.Approve(id, "receipt-read-fence");
            Assert.Equal(AccountingApiStatus.Ok, first.Status); Assert.NotNull(first.Resource?.ApprovalReceipt); Assert.True(replay.IsReplay);
            Assert.Equal(first.Resource?.ApprovalReceipt, replay.Resource?.ApprovalReceipt);
            Assert.Equal(before with { Journals = before.Journals + 1, Deliveries = before.Deliveries + 1, Commands = before.Commands + 1, Audits = before.Audits + 2 }, await SafetyCounts(run)); scope.AssertClean();
        }
    }
}
