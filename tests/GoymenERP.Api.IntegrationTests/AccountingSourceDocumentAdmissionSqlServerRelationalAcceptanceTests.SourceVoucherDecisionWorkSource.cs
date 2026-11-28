using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingSourceDocumentAdmissionSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Source_voucher_work_source_matches_the_native_pending_formatter_and_keeps_dynamic_period_rule_out_of_original_receipt()
    {
        await using var run=await Run.StartAsync(true);var admission=await run.AdmitAsync(CompanyA,Request(CompanyA));var id=admission.VoucherDraftId!.Value;
        await using var owner=new VoucherApprovalScope(run);var context=new WorkInboxSourceContextV1(CompanyA,owner.User.UserId,"SENTETIK-work",null);
        var source=new AccountingSourceVoucherWorkInboxSourceV1(owner.Db,new(owner.Company,owner.User,owner.Authority),owner.Service);
        var before=await VoucherSafetyCounts(run);var page=await source.ListPendingAsync(context,10,default);var listed=Assert.Single(page.Items!);
        var snapshot=Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(context,id,default));
        Assert.Equal(JsonSerializer.Serialize(listed),JsonSerializer.Serialize(snapshot.Item));Assert.Equal("1",snapshot.SourceVersion);Assert.True(snapshot.Pending);
        var proof=Assert.IsType<WorkSourceVoucherProofV1>(snapshot.SourceVoucherProof);Assert.False(proof.PeriodOpen);Assert.Equal(2,snapshot.Item.Rules.Count);
        var detail=(await owner.Service.ReadDecisionAsync(id,_=>Task.CompletedTask)).Resource;Assert.NotNull(detail);
        Assert.True(AccountingSourceVoucherDecisionBindingV1.MatchesDecisionProof(snapshot.Item,proof));
        Assert.True(AccountingSourceVoucherDecisionBindingV1.MatchesDecisionItem(snapshot.Item,detail));Assert.Equal(before,await VoucherSafetyCounts(run));
        await OpenVoucherPeriod(run);
        var ready=Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(context,id,default));Assert.Single(ready.Item.Rules);
        Assert.True(ready.SourceVoucherProof!.PeriodOpen);Assert.Equal(proof.SourceFactsDigest,ready.SourceVoucherProof.SourceFactsDigest);
        var approved=await owner.Approve(id,"SENTETIK-work-original");var receipt=Assert.IsType<AccountingSourceVoucherApprovalReceiptV1>(approved.Resource?.ApprovalReceipt);
        // This old two-rule item proves no historical period state: only its primary immutable facts bind the original receipt.
        Assert.True(AccountingSourceVoucherDecisionBindingV1.MatchesOriginalDecisionItem(snapshot.Item,approved.Resource,receipt));
        await CancelVoucherSource(run,Cancellation());var cancelled=Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(context,id,default));
        Assert.False(cancelled.Pending);Assert.True(cancelled.SourceVoucherProof!.SourceCancelled);Assert.Equal("CancellationReversalPending",cancelled.SourceVoucherProof.Status);
        var after=await VoucherSafetyCounts(run);var replay=await owner.Approve(id,"SENTETIK-work-original");Assert.True(replay.IsReplay);
        Assert.True(AccountingSourceVoucherDecisionBindingV1.MatchesOriginalDecisionItem(cancelled.Item,replay.Resource,replay.Resource?.ApprovalReceipt));
        Assert.Equal(after,await VoucherSafetyCounts(run));owner.AssertClean();
    }
}
