using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

/// <summary>One already-named order under its native decision permission. No draft-read or price grant is added.
/// Completed records retain their maker/version facts so the native command can replay its own receipt.</summary>
public sealed class SalesOrderWorkDecisionReadV1(SalesDbContext database,ICompanyContext company,ICurrentUser user,
    IErpAuthorityRevalidationService authority)
{
    private bool Same(WorkInboxSourceContextV1 context) => context.CompanyId != Guid.Empty && context.GrantScope is null
        && company.IsResolved && company.CompanyId == context.CompanyId && user.IsAuthenticated && user.UserId == context.UserId
        && user.CanAccessCompany(context.CompanyId);
    private async Task<bool> Allowed(WorkInboxSourceContextV1 context,CancellationToken token)
    {
        if(!Same(context))return false;
        var scope=await authority.RevalidatePermissionScopeAsync(SalesPermissionManifestV1.OrdersApprove,context.CompanyId,token);
        return Same(context)&&scope.IsGranted&&scope.IsCompanyWide;
    }
    public async Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context,int maxCount,CancellationToken token)
    {
        if(!await Allowed(context,token))return WorkInboxSourceResultV1.Denied();
        var take=Math.Clamp(maxCount,1,NativeApprovalQueueRulesV1.MaxItemsPerSource);
        var rows=await database.Drafts.AsNoTracking().Where(row=>row.CompanyId==context.CompanyId
            &&row.State==(int)SalesDraftState.Draft&&row.ApprovalState==(int)InternalApprovalState.PendingApproval)
            .OrderBy(row=>row.SubmittedAtUtc).ThenBy(row=>row.Id).Take(take+1).ToArrayAsync(token);
        if(!await Allowed(context,token))return WorkInboxSourceResultV1.Denied();
        var items=rows.Take(take).Select(Item).ToArray();
        return items.Any(item=>item is null)?WorkInboxSourceResultV1.Unavailable()
            :WorkInboxSourceResultV1.Found(context.CompanyId,items.Select(item=>item!).ToArray(),rows.Length>take);
    }
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadAsync(WorkInboxSourceContextV1 context,Guid id,CancellationToken token)
    {
        if(id==Guid.Empty||!await Allowed(context,token))return null;
        var row=await database.Drafts.AsNoTracking().SingleOrDefaultAsync(row=>row.CompanyId==context.CompanyId&&row.Id==id,token);
        if(!await Allowed(context,token)||row is null||row.Version<1||Item(row) is not {} item)return null;
        return new(context.CompanyId,item,row.Version.ToString(CultureInfo.InvariantCulture),
            row.State==(int)SalesDraftState.Draft&&row.ApprovalState==(int)InternalApprovalState.PendingApproval);
    }
    private static WorkInboxItemV1? Item(DraftRow row)
    {
        if(row.Id==Guid.Empty||string.IsNullOrWhiteSpace(row.CreatedBy)||string.IsNullOrWhiteSpace(row.DocumentNo))return null;
        var maker=SalesWorkInboxItemsV1.RequireActor(row.CreatedBy);
        return new WorkInboxItemV1("sales","sales.order-draft.v1",row.Id,row.DocumentNo,
            (row.SubmittedAtUtc??row.CreatedAtUtc).ToUniversalTime(),$"/onaylarim/sales.order-draft.v1/{row.Id:D}",maker,
            [new WorkApprovalRuleResultV1("sales.order-draft-approval",null,null,null,null)],
            [SalesWorkInboxItemsV1.OrderReference(row.Id,row.DocumentNo)],
            string.IsNullOrWhiteSpace(row.SubmittedBy)?[]:SalesWorkInboxItemsV1.Others(maker,row.SubmittedBy));
    }
}
