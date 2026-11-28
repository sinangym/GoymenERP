using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

internal sealed partial class SalesRiskExceptionWorkInboxSourceV1 : IWorkApprovalDecisionVersionSourceV1
{
    public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context,Guid itemId,CancellationToken token)=>ReadPurposeAsync(context,itemId,null,token);
    public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionVersionAsync(WorkInboxSourceContextV1 context,Guid itemId,string expectedProposalVersion,CancellationToken token)=>ReadPurposeAsync(context,itemId,expectedProposalVersion,token);
    private async Task<WorkApprovalDecisionSnapshotV1?> ReadPurposeAsync(WorkInboxSourceContextV1 context,Guid id,string? selected,CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);await Fence(token);if(id==Guid.Empty)return null;
        if(owner is null)throw new InvalidOperationException("Risk karar okuma sahibi kayıtlı değil.");
        if(selected is not null&&(!SalesRiskExceptionDecisionBindingV1.TryVersion(selected,out var v)||v==long.MaxValue))throw new InvalidOperationException("Risk karar sürümü geçersiz.");
        var answer=await owner.ReadRiskExceptionDecisionAsync(id,Fence,token);await Fence(token);
        if(answer is{State:SalesRiskExceptionDecisionReadStateV1.Denied,Detail:null})throw new UnauthorizedAccessException();
        if(answer is{State:SalesRiskExceptionDecisionReadStateV1.NotFound,Detail:null})return null;
        if(answer is not{State:SalesRiskExceptionDecisionReadStateV1.Found,Detail:{} detail}||detail.CompanyId!=context.CompanyId||detail.ExceptionId!=id
            ||!SalesRiskExceptionService.MatchesRiskDecisionDetail(detail))throw new InvalidOperationException("Risk karar olguları doğrulanamadı.");
        var p=SalesRiskExceptionDecisionBindingV1.Proof(detail);
        if(selected is not null&&selected!=detail.Exception.Version)
        {
            var original=await owner.ReadRiskExceptionOriginalVersionAsync(id,selected,Fence,token);await Fence(token);
            if(original is{State:SalesRiskExceptionDecisionReadStateV1.Denied,Receipt:null})throw new UnauthorizedAccessException();
            if(original is not{State:SalesRiskExceptionDecisionReadStateV1.Found,Receipt:{} receipt}||receipt.CompanyId!=context.CompanyId||receipt.ExceptionId!=id
                ||receipt.ExpectedVersion!=selected||receipt.ActorId!=context.UserId)throw new InvalidOperationException("Özgün risk karar makbuzu yok.");
            p=SalesRiskExceptionDecisionBindingV1.Proof(receipt);
        }
        var item=SalesRiskExceptionDecisionBindingV1.Item(p);
        if(!SalesRiskExceptionDecisionBindingV1.MatchesDecisionProof(item,p))throw new InvalidOperationException("Risk karar kartı doğrulanamadı.");
        if(item.Excludes(context.UserId))throw new UnauthorizedAccessException();
        return new(context.CompanyId,item,p.Version,p.Pending){SalesRiskExceptionProof=p};
        async Task Fence(CancellationToken ct){ct.ThrowIfCancellationRequested();if(!await DecisionAllowedAsync(context,ct))throw new UnauthorizedAccessException();ct.ThrowIfCancellationRequested();}
    }
}
