namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class ManufacturingCostDifferenceServiceV1
{
    private sealed record ApprovalCapture(Guid CompanyId, Guid ActorId, string ActorText);
    private Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>> RunApprovalAsync(Guid id, long version,
        string key, CancellationToken token)
    {
        // Preserve native pure shape/auth refusals before the store takes cleanup ownership.
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            return Task.FromResult(new AccountingApiResult<ManufacturingCostDifferenceViewV1>(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid));
        if (!Capture(out var companyId, out var actor, out var actorText))
            return Task.FromResult(Denied<ManufacturingCostDifferenceViewV1>());
        var captured = new ApprovalCapture(companyId, actor, actorText);
        return store.RunApprovalAsync(replayOnly => Run("approve", AccountingJournalReversalPermissionNamesV1.Approve,
            new { id, version }, key, (userId, fence, metadata) => store.ApproveAsync(id, version, userId, fence, metadata, token),
            token, captured, replayOnly), ct => ApprovalFenceAsync(captured, ct), token);
    }
    private async Task ApprovalFenceAsync(ApprovalCapture captured, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Current(captured.CompanyId, captured.ActorId, captured.ActorText)) throw new NormalJournalAuthorityDeniedException();
        var scope = await authority.RevalidatePermissionScopeAsync(AccountingJournalReversalPermissionNamesV1.Approve, captured.CompanyId, token);
        token.ThrowIfCancellationRequested();
        if (scope is null || !scope.IsGranted || !scope.IsCompanyWide || !Current(captured.CompanyId, captured.ActorId, captured.ActorText))
            throw new NormalJournalAuthorityDeniedException();
    }
}
