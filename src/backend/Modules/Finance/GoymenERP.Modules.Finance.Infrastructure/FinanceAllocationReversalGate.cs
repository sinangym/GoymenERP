using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// F-03's company-filtered reversal guard. A released distribution no longer blocks reversal; any
/// active distribution does. Context continuity is checked both before and after the awaited SQL
/// read so a changed actor/company cannot reuse its answer.
/// </summary>
public sealed class FinanceAllocationReversalGate(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser) : IFinanceReversalAllocationGate
{
    public async Task<ReversalAllocationAnswer> CheckAsync(
        FinanceContext context,
        Guid originalSourceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (originalSourceId == Guid.Empty)
        {
            return new ReversalAllocationAnswer(
                ReversalAllocationOutcome.Unavailable,
                "finance_source_identity_required");
        }

        if (!SameContext(context))
        {
            return new ReversalAllocationAnswer(ReversalAllocationOutcome.Denied);
        }

        var hasAllocation = await dbContext.Allocations.AsNoTracking()
            .AnyAsync(row => row.SourceId == originalSourceId
                && row.Status == FinanceAllocationStatuses.Active, cancellationToken);

        if (!SameContext(context))
        {
            return new ReversalAllocationAnswer(ReversalAllocationOutcome.Denied);
        }

        return hasAllocation
            ? new ReversalAllocationAnswer(
                ReversalAllocationOutcome.HasAllocation,
                "finance_reversal_has_allocations")
            : new ReversalAllocationAnswer(ReversalAllocationOutcome.NoAllocation);
    }

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId
        && currentUser.CanAccessCompany(context.CompanyId);
}
