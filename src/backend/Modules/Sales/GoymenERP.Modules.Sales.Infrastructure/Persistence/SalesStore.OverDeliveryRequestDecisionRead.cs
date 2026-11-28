using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore : ISalesOverDeliveryRequestDecisionReadStoreV1
{
    private Func<CancellationToken, Task>? overDeliveryRequestDecisionReadFence;
    public async Task<SalesOverDeliveryRequestDecisionSourceV1?> ReadOverDeliveryRequestDecisionSourceAsync(Guid requestId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshFence); cancellationToken.ThrowIfCancellationRequested();
        var company = RequireResolvedCompany();
        EnterOverRequestRead(freshFence);
        try
        {
            var row = await OverDeliveryRequestApprovalReadAsync(() => (from request in dbContext.ApprovalRequests.AsNoTracking()
                join order in dbContext.Drafts.AsNoTracking() on request.SubjectId equals order.Id
                where request.CompanyId == company && order.CompanyId == company && request.Id == requestId
                    && request.Kind == (int)SalesApprovalRequestKind.OverDelivery
                select new { Request = request, Order = order }).SingleOrDefaultAsync(cancellationToken), cancellationToken);
            if (row is null) return null;
            var order = (await OverDeliveryRequestApprovalReadAsync(() => RestoreAsync([row.Order], cancellationToken), cancellationToken)).Single();
            var unchanged = await OverDeliveryRequestApprovalReadAsync(() => (from request in dbContext.ApprovalRequests.AsNoTracking()
                join header in dbContext.Drafts.AsNoTracking() on request.SubjectId equals header.Id
                where request.CompanyId == company && header.CompanyId == company && request.Id == requestId
                    && request.Version == row.Request.Version && header.Version == row.Order.Version
                select request.Id).AnyAsync(cancellationToken), cancellationToken);
            if (!unchanged) throw new InvalidOperationException("Over-delivery selection changed during owner read.");
            return new(SalesApprovalRequestRows.Restore(row.Request), order);
        }
        finally { overDeliveryRequestDecisionReadFence = null; }
    }

    private void EnterOverRequestRead(Func<CancellationToken, Task> fence)
    {
        if (overDeliveryRequestDecisionReadFence is not null || ownedOverDeliveryRequestApproval is not null || ownedRiskExceptionDecision is not null || ownedQuoteApproval is not null
            || ownedRequestApproval is not null || ownedPriceFloorApproval is not null || ownedDiscountApproval is not null || quoteDecisionReadFence is not null)
            throw new InvalidOperationException("Over-delivery selected read is not reentrant.");
        var company = RequireResolvedCompany();
        overDeliveryRequestDecisionReadFence = async token =>
        { token.ThrowIfCancellationRequested(); RequireCompany(company); await fence(token); token.ThrowIfCancellationRequested(); RequireCompany(company); };
    }
}
