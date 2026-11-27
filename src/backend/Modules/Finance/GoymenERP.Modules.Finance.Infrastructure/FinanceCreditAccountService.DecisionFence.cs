using GoymenERP.Modules.Finance.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceCreditAccountService
{
    public Task<CreditAccountCommandResult> ApproveAsync(FinanceContext context, CommandMetadata metadata,
        CreditAccountApproveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CreditDecisionTransactionAsync(context, metadata, () => ApproveCoreAsync(context, metadata, request, cancellationToken), cancellationToken);
    }

    public Task<CreditAccountCommandResult> RejectAsync(FinanceContext context, CommandMetadata metadata,
        CreditAccountRejectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CreditDecisionTransactionAsync(context, metadata, () => RejectCoreAsync(context, metadata, request, cancellationToken), cancellationToken);
    }

    private async Task<CreditAccountCommandResult> CreditDecisionTransactionAsync(FinanceContext context, CommandMetadata metadata,
        Func<Task<CreditAccountCommandResult>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        // Decisions own their transaction. A refused entry never touches a caller's handles or staged Audit batch.
        if (System.Transactions.Transaction.Current is not null || dbContext.Database.CurrentTransaction is not null
            || dbContext.ChangeTracker.HasChanges() || auditOwner is { HasPendingDrafts: true })
            return Refused(context, CommandOutcome.Unavailable);
        if (await StartAsync(context, metadata, FinanceAction.ApproveCredit, cancellationToken) is { } refused) return refused;
        return await FinanceTreasuryTransaction.ExecuteAsync(dbContext, operation,
            async () => await FenceAsync(context, FinanceAction.ApproveCredit, cancellationToken) ?? Refused(context, CommandOutcome.Unavailable),
            auditOwner, cancellationToken,
            // Every result, including a terminal refusal/replay, is fenced after the actual save and before Commit.
            canCommit: async _ => await FenceAsync(context, FinanceAction.ApproveCredit, cancellationToken) is null);
    }
}
