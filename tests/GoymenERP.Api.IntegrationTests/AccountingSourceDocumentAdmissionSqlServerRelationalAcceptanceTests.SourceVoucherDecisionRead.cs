using GoymenERP.Modules.Accounting.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingSourceDocumentAdmissionSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Source_voucher_purpose_read_and_genuine_original_receipt_survive_current_source_cancellation_without_another_journal()
    {
        await using var run=await Run.StartAsync(true);await OpenVoucherPeriod(run);
        var admission=await run.AdmitAsync(CompanyA,Request(CompanyA));var id=admission.VoucherDraftId!.Value;
        await using var owner=new VoucherApprovalScope(run);var before=await VoucherSafetyCounts(run);
        var pending=await owner.Service.ReadDecisionAsync(id,_=>Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Ok,pending.Status);Assert.NotNull(pending.Resource);Assert.True(pending.Resource.PeriodOpen);
        Assert.Equal(Request(CompanyA).SourceActorId,pending.Resource.Voucher.SourceActorId);Assert.Equal(before,await VoucherSafetyCounts(run));
        var approved=await owner.Approve(id,"SENTETIK-purpose-original");var receipt=Assert.IsType<AccountingSourceVoucherApprovalReceiptV1>(approved.Resource?.ApprovalReceipt);
        Assert.Equal(1,receipt.ExpectedVersion);Assert.Equal(2,receipt.ResultVersion);Assert.Equal(pending.Resource.SourceFactsDigest,receipt.SourceFactsDigest);
        await CancelVoucherSource(run,Cancellation());var afterCancellation=await VoucherSafetyCounts(run);
        var current=await owner.Service.ReadDecisionAsync(id,_=>Task.CompletedTask);Assert.NotNull(current.Resource);Assert.True(current.Resource.SourceCancelled);
        Assert.Equal("CancellationReversalPending",current.Resource.Voucher.Status);Assert.Equal(receipt.SourceFactsDigest,current.Resource.SourceFactsDigest);
        Assert.NotEqual(pending.Resource.PreviewDigest,current.Resource.PreviewDigest);
        var replay=await owner.Approve(id,"SENTETIK-purpose-original");Assert.True(replay.IsReplay);Assert.Equal("Posted",replay.Resource?.Status);
        Assert.Equal(2L,replay.Resource?.Version);Assert.Equal(receipt,replay.Resource?.ApprovalReceipt);Assert.Equal(owner.CorrelationId,replay.OriginalCorrelationId);
        Assert.Equal(afterCancellation,await VoucherSafetyCounts(run));owner.AssertClean();
    }
    [SqlServerFact]
    public async Task Source_voucher_receipt_commands_and_last_owner_read_boundaries_refuse_completed_revocation_or_cancel_and_rollback_original_effects()
    {
        foreach(var boundary in new[]{"commands","journal"})foreach(var cancel in new[]{false,true})
        {
            await using var run=await Run.StartAsync(true);await OpenVoucherPeriod(run);var admission=await run.AdmitAsync(CompanyA,Request(CompanyA));
            var before=await VoucherSafetyCounts(run);using var cts=new CancellationTokenSource();var save=new VoucherApprovalSave();
            VoucherApprovalScope? active=null;var fired=false;var laterReads=0;
            var probe=new VoucherApprovalQuery(sql=>
            {
                if(fired){laterReads++;return;}
                if(save.VoucherSaved&&sql.Contains(boundary=="commands"?"[Commands]":"[Journals]",StringComparison.Ordinal))
                {fired=true;if(cancel)cts.Cancel();else active!.Authority.Granted=false;}
            });
            await using var owner=active=new VoucherApprovalScope(run,save:save,extra:probe);
            if(cancel)await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>owner.Approve(admission.VoucherDraftId!.Value,"SENTETIK-receipt-fence",cts.Token));
            else {var denied=await owner.Approve(admission.VoucherDraftId!.Value,"SENTETIK-receipt-fence");Assert.Equal(AccountingApiStatus.Denied,denied.Status);Assert.Null(denied.Resource);}
            Assert.True(save.VoucherSaved);Assert.True(fired);Assert.Equal(0,laterReads);Assert.Equal(1,owner.Writer.Discards);owner.AssertClean();Assert.Equal(before,await VoucherSafetyCounts(run));
            owner.Authority.Granted=true;var accepted=await owner.Approve(admission.VoucherDraftId!.Value,"SENTETIK-receipt-fence");
            Assert.Equal(AccountingApiStatus.Ok,accepted.Status);Assert.NotNull(accepted.Resource?.ApprovalReceipt);Assert.False(accepted.IsReplay);
            Assert.True((await owner.Approve(admission.VoucherDraftId.Value,"SENTETIK-receipt-fence")).IsReplay);
            Assert.Equal(before with{Journals=before.Journals+1,Lines=before.Lines+4,Deliveries=before.Deliveries+1,Commands=before.Commands+1,Audits=before.Audits+2},await VoucherSafetyCounts(run));
        }
    }
    [SqlServerFact]
    public async Task Source_voucher_purpose_inner_read_fence_prevents_the_next_query_after_normal_completing_revocation()
    {
        await using var run=await Run.StartAsync(true);await OpenVoucherPeriod(run);var admission=await run.AdmitAsync(CompanyA,Request(CompanyA));var before=await VoucherSafetyCounts(run);
        VoucherApprovalScope? active=null;var fired=false;var cancellationReads=0;
        var probe=new VoucherApprovalQuery(sql=>{if(sql.Contains("[SourceDocumentVoucherDraftLines]",StringComparison.Ordinal)){fired=true;active!.Authority.Granted=false;}
            if(sql.Contains("[SourceInvoiceCancellations]",StringComparison.Ordinal))cancellationReads++;});
        await using var owner=active=new VoucherApprovalScope(run,extra:probe);
        var denied=await owner.Service.ReadDecisionAsync(admission.VoucherDraftId!.Value,_=>Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Denied,denied.Status);Assert.Null(denied.Resource);Assert.True(fired);Assert.Equal(0,cancellationReads);
        Assert.Equal(0,owner.Writer.Writes);Assert.Equal(0,owner.Writer.Discards);Assert.Equal(before,await VoucherSafetyCounts(run));owner.AssertClean();
    }
}
