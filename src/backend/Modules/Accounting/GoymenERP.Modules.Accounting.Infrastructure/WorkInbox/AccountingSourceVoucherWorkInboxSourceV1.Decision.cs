using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

internal sealed partial class AccountingSourceVoucherWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context,Guid itemId,CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);await Fence(token);
        if(owner is null)throw new InvalidOperationException("Source voucher decision owner unavailable.");
        var answer=await owner.ReadDecisionAsync(itemId,Fence,token);await Fence(token);
        if(answer is null)throw new InvalidOperationException("Source voucher decision answer unavailable.");
        if(answer.Status==AccountingApiStatus.Denied&&answer.Resource is null)throw new UnauthorizedAccessException();
        if(answer.Status==AccountingApiStatus.NotFound&&answer.Resource is null)return null;
        if(answer is not{Status:AccountingApiStatus.Ok,Resource:{} detail,IsReplay:false,Code:null,Field:null,ExpectedVersion:null,CurrentVersion:null,OriginalCorrelationId:null}
            ||!AccountingSourceVoucherDecisionBindingV1.ValidDetail(detail)||detail.Voucher.CompanyId!=context.CompanyId||detail.Voucher.Id!=itemId
            ||AccountingSourceVoucherDecisionBindingV1.FromView(detail.Voucher,detail.CreatedAtUtc,detail.PeriodOpen) is not{} item)
            throw new InvalidOperationException("Source voucher decision facts unavailable.");
        if(item.Excludes(context.UserId))throw new UnauthorizedAccessException();
        var v=detail.Voucher;
        return new(context.CompanyId,item,v.Version.ToString(CultureInfo.InvariantCulture),!detail.SourceCancelled&&v.Status is "AwaitingApproval" or "PeriodWaiting")
        {SourceVoucherProof=new(context.CompanyId,itemId,v.SourceDocumentId,v.InvoiceId,v.SourceActorId,detail.CreatedAtUtc,v.DocumentKind,v.FinalizedVersion,
            v.PartyId,v.DocumentCurrencyId,v.Status,detail.SourceCancelled,detail.PeriodOpen,detail.SourceFactsDigest,detail.PreviewDigest,v.OfficialInvoiceNo)};
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();if(context.GrantScope is not null)throw new UnauthorizedAccessException();
            var refusal=await fence.CheckAsync(context,ApproverPermission,ct);ct.ThrowIfCancellationRequested();
            if(refusal?.Outcome==NativeApprovalQueueOutcomeV1.Denied)throw new UnauthorizedAccessException();
            if(refusal is not null)throw new InvalidOperationException("Source voucher decision authority unavailable.");
        }
    }
}
