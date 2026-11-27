using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingStore
{
    public Task<AccountingApiResult<T>> RunPeriodApprovalAsync<T>(string commandName,
        Func<bool, Task<AccountingApiResult<T>>> attempt, Func<CancellationToken, Task> freshFence,
        CancellationToken cancellationToken = default) where T : class
    {
        if (commandName is not AccountingCommandNames.ApprovePeriod and not "accounting.period.reopening.approve.v1")
            throw new ArgumentException("Only the two existing period approvals own this boundary.", nameof(commandName));
        // The normal approval's audited owner transaction is also the period command's transaction.
        // The Finance carry-forward participant continues to enlist in that same transaction.
        return RunOwnedAccountingApprovalAsync(commandName, attempt, freshFence, cancellationToken);
    }
}
