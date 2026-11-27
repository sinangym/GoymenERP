namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class AccountingSourceVoucherServiceV1
{
    private sealed record ApprovalCapture(Guid CompanyId, Guid ActorId, string ActorText);
    private Task<AccountingApiResult<AccountingSourceVoucherViewV1>> RunApprovalAsync(Guid id, long version, string key, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            return Task.FromResult(new AccountingApiResult<AccountingSourceVoucherViewV1>(AccountingApiStatus.Invalid,null,false,AccountingApiCodes.RequestInvalid));
        if (!Capture(out var companyId,out var actor,out var actorText)) return Task.FromResult(Denied<AccountingSourceVoucherViewV1>());
        token.ThrowIfCancellationRequested();
        var captured=new ApprovalCapture(companyId,actor,actorText);
        return store.RunApprovalAsync(replayOnly=>Run("approve",NormalJournalPermissionNamesV1.DraftApprove,new{id,version},key,
            (userId,fence,metadata)=>store.ApproveAsync(id,version,userId,fence,metadata,token),token,captured,replayOnly,id,version),
            ct=>ApprovalFenceAsync(captured,ct),token);
    }
    private async Task ApprovalFenceAsync(ApprovalCapture captured,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!Current(captured.CompanyId,captured.ActorId,captured.ActorText))throw new NormalJournalAuthorityDeniedException();
        var scope=await authority.RevalidatePermissionScopeAsync(NormalJournalPermissionNamesV1.DraftApprove,captured.CompanyId,token);
        token.ThrowIfCancellationRequested();
        if(scope is null||!scope.IsGranted||!scope.IsCompanyWide||!Current(captured.CompanyId,captured.ActorId,captured.ActorText))
            throw new NormalJournalAuthorityDeniedException();
    }
}
