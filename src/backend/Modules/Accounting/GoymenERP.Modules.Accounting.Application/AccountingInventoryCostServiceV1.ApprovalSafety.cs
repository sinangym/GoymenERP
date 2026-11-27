namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class AccountingInventoryCostServiceV1
{
    private sealed record ApprovalCapture(Guid CompanyId, Guid ActorId, string ActorText, string Permission);
    private Task<AccountingApiResult<AccountingInventoryCostProposalV1>> RunApprovalAsync(Guid id,
        AccountingInventoryCostKindV1 kind, long version, string key, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            return Task.FromResult(new AccountingApiResult<AccountingInventoryCostProposalV1>(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid));
        if (!Capture(out var companyId, out var actor, out var actorText))
            return Task.FromResult(Denied<AccountingInventoryCostProposalV1>());
        token.ThrowIfCancellationRequested();
        var captured = new ApprovalCapture(companyId, actor, actorText, Permission(kind, true));
        return store.RunApprovalAsync(replayOnly => Run("approve", captured.Permission, new { id, kind, version }, key,
            (userId, fence, metadata) => store.ApproveAsync(id, kind, version, userId, fence, metadata, token),
            token, captured, replayOnly), ct => ApprovalFenceAsync(captured, ct), token);
    }
    private async Task ApprovalFenceAsync(ApprovalCapture captured, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Current(captured.CompanyId, captured.ActorId, captured.ActorText)) throw new NormalJournalAuthorityDeniedException();
        var scope = await authority.RevalidatePermissionScopeAsync(captured.Permission, captured.CompanyId, token);
        token.ThrowIfCancellationRequested();
        if (scope is null || !scope.IsGranted || !scope.IsCompanyWide || !Current(captured.CompanyId, captured.ActorId, captured.ActorText))
            throw new NormalJournalAuthorityDeniedException();
    }
}
