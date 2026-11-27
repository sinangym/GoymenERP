namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class AccountingInventoryCostServiceV1
{
    private async Task<AccountingApiResult<AccountingInventoryCostProposalV1>> RunCancellationApprovalAsync(Guid id,
        ApproveInventoryCostCancellationV1 request, string key, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            return new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid);
        if (!Capture(out var companyId, out var actor, out var actorText)) return Denied<AccountingInventoryCostProposalV1>();
        var captured = new ApprovalCapture(companyId, actor, actorText, AccountingJournalReversalPermissionNamesV1.Approve);
        // Keep the native permission-only refusal before an unconfigured store; capture precedes the first await.
        try { await ApprovalFenceAsync(captured, token); }
        catch (NormalJournalAuthorityDeniedException) { return Denied<AccountingInventoryCostProposalV1>(); }
        return await store.RunCancellationApprovalAsync(replayOnly => Run("approve-component-cancellation", captured.Permission,
            new { id, request }, key, (checker, fence, metadata) => store.ApproveCancellationAsync(id, request, checker, fence, metadata, token),
            token, captured, replayOnly), ct => ApprovalFenceAsync(captured, ct), token);
    }
}
