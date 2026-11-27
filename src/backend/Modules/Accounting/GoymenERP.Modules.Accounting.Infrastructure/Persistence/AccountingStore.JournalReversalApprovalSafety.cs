using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingStore
{
    public async Task<AccountingApiResult<AccountingJournalReversalProposalViewV1>> RunApprovalAsync(
        Func<bool, Task<AccountingApiResult<AccountingJournalReversalProposalViewV1>>> attempt,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        var answer = await RunOwnedAccountingApprovalAsync(AccountingJournalReversalCommandNamesV1.Approve,
            attempt, freshFence, cancellationToken);
        // Preserve this native lifecycle's pre-existing 403 envelope (no public error code).
        return answer.Status == AccountingApiStatus.Denied && answer.Code == AccountingApiCodes.AuthorityDenied
            ? answer with { Code = null } : answer;
    }

    private bool IsOwnedJournalReversalApproval =>
        ownedNormalApproval?.CommandName == AccountingJournalReversalCommandNamesV1.Approve;

    private Task JournalReversalApprovalFenceAsync(CancellationToken token) =>
        IsOwnedJournalReversalApproval ? NormalApprovalFence(token) : Task.CompletedTask;
}
