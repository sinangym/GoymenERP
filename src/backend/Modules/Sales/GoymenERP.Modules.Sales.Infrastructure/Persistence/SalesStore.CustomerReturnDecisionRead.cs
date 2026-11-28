using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.CustomerReturns;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore : ISalesCustomerReturnDecisionReadStoreV1
{
    private Func<CancellationToken, Task>? customerReturnDecisionReadFence;
    public async Task<SalesCustomerReturn?> ReadCustomerReturnDecisionSourceAsync(Guid returnId, Func<CancellationToken, Task> freshFence,
        CancellationToken cancellationToken = default)
    {
        EnterCustomerReturnDecisionRead(freshFence, cancellationToken);
        try
        {
            var company = RequireResolvedCompany();
            var row = await CustomerReturnDecisionReadAsync(() => dbContext.CustomerReturns.AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == company && x.Id == returnId, cancellationToken), cancellationToken);
            return row is null ? null : SalesCustomerReturnRows.Restore(row);
        }
        finally { customerReturnDecisionReadFence = null; }
    }
    private void EnterCustomerReturnDecisionRead(Func<CancellationToken, Task> fence, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(fence); token.ThrowIfCancellationRequested();
        if (!dbContext.Database.IsRelational() || customerReturnDecisionReadFence is not null || ownedCustomerReturnDecision is not null
            || ownedContractDiscountDecision is not null || ownedRemainderDecision is not null || ownedPriceFloorApproval is not null
            || ownedDiscountApproval is not null || ownedOverDeliveryRequestApproval is not null || ownedRiskExceptionDecision is not null
            || ownedRequestApproval is not null || ownedQuoteApproval is not null || overDeliveryRequestDecisionReadFence is not null
            || riskExceptionDecisionReadFence is not null || remainderDecisionReadFence is not null || quoteDecisionReadFence is not null)
            throw new InvalidOperationException("Müşteri iadesi karar okuması başka sahip işlemiyle karışamaz.");
        var company = RequireResolvedCompany();
        customerReturnDecisionReadFence = async ct => { ct.ThrowIfCancellationRequested(); RequireCompany(company); await fence(ct); ct.ThrowIfCancellationRequested(); RequireCompany(company); };
    }
}
