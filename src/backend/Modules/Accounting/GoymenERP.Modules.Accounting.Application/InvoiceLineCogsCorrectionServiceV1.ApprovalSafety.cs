namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class InvoiceLineCogsCorrectionServiceV1
{
    private sealed record ApprovalCapture(Guid CompanyId, Guid ActorId, string ActorText);
    private Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>> RunApprovalAsync(Guid invoiceId, Guid lineId, Guid proposalId,
        ApproveInvoiceLineCogsCorrectionV1 request, string key, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Preserve native pure shape/key refusal; ExpectedVersion remains validated by the native core.
        if (invoiceId == Guid.Empty || lineId == Guid.Empty || string.IsNullOrWhiteSpace(key) || key.Trim().Length > 128 || key.Contains(','))
            return Task.FromResult(new AccountingApiResult<InvoiceLineCogsCorrectionViewV1>(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid));
        if (!Capture(out var companyId, out var actor, out var actorText)) return Task.FromResult(Denied<InvoiceLineCogsCorrectionViewV1>());
        var capture = new ApprovalCapture(companyId, actor, actorText);
        return store.RunApprovalAsync(replayOnly => Run("approve", AccountingJournalReversalPermissionNamesV1.Approve,
            invoiceId, lineId, new { proposalId, request }, key,
            (facts, checker, fence, metadata) => store.ApproveAsync(facts, proposalId, request.ExpectedVersion, checker, fence, metadata, token),
            token, capture, replayOnly), ct => ApprovalFenceAsync(capture, ct), token);
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
