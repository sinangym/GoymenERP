using GoymenERP.Modules.Manufacturing.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

public sealed partial class ManufacturingStore
{
    private Func<CancellationToken, Task>? planRejectionFence;
    private bool planRejectionReceiptSaved;

    public async Task<ManufacturingApiResult<PlanViewV1>> ExecutePlanRejectionAsync(
        Func<CancellationToken, Task<ManufacturingApiResult<PlanViewV1>>> execute,
        Func<CancellationToken, Task> freshDecisionFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(freshDecisionFence);
        // Before any read, callback or replay. Caller-owned work must never enter this attempt's cleanup.
        if (System.Transactions.Transaction.Current is not null || !dbContext.Database.IsRelational()
            || dbContext.Database.CurrentTransaction is not null || dbContext.ChangeTracker.HasChanges()
            || decisionAuditWriter?.HasPendingDrafts == true || queueMutationMetadata is not null
            || planRejectionFence is not null)
            throw new InvalidOperationException("Plan rejection requires its own clean Manufacturing transaction.");
        var companyId = RequireResolvedCompany();
        async Task Fence(CancellationToken token)
        {
            if (RequireResolvedCompany() != companyId) throw Refused(ManufacturingStoreCodes.AuthorityDenied, "Company changed.");
            await freshDecisionFence(token);
            if (RequireResolvedCompany() != companyId) throw Refused(ManufacturingStoreCodes.AuthorityDenied, "Company changed.");
        }
        try
        {
            planRejectionFence = Fence;
            planRejectionReceiptSaved = false;
            await Fence(cancellationToken);
            await using var owned = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var result = await execute(cancellationToken);
            await Fence(cancellationToken);
            // A native saved receipt (business success or terminal refusal), or a real ledger replay,
            // is the evidence. A returned 404/409 by itself must never commit staged work.
            if (planRejectionReceiptSaved && result.Status is not (ManufacturingApiStatus.Denied
                or ManufacturingApiStatus.Invalid or ManufacturingApiStatus.Unavailable))
            {
                await owned.CommitAsync(cancellationToken);
            }
            else
            {
                await owned.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
            }
            return result;
        }
        catch
        {
            // The await-using owner transaction has disposed/rolled back before the caller can read a winner.
            dbContext.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            planRejectionFence = null;
            planRejectionReceiptSaved = false;
            queueMutationMetadata = null;
            decisionAuditWriter?.DiscardPending();
        }
    }

    private Task FencePlanRejectionAsync(CancellationToken token) =>
        planRejectionFence?.Invoke(token) ?? Task.CompletedTask;
}
