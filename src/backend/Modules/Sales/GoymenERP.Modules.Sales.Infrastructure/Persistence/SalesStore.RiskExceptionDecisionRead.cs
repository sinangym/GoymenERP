using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.RiskExceptions;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore : ISalesRiskExceptionDecisionReadStoreV1
{
    private Func<CancellationToken, Task>? riskExceptionDecisionReadFence;
    private async Task RiskExceptionDecisionReadFenceAsync(CancellationToken token)
    {
        if (riskExceptionDecisionReadFence is not { } fence) return;
        token.ThrowIfCancellationRequested(); var company = RequireResolvedCompany(); await fence(token);
        token.ThrowIfCancellationRequested(); RequireCompany(company);
    }
    private async Task<T> WithRiskExceptionDecisionReadAsync<T>(Func<Task<T>> read,
        Func<CancellationToken, Task> freshFence, CancellationToken token, bool allowOwnedRiskReceipt = false)
    {
        ArgumentNullException.ThrowIfNull(freshFence); token.ThrowIfCancellationRequested();
        var company = RequireResolvedCompany();
        if (riskExceptionDecisionReadFence is not null || contractDiscountDecisionReadFence is not null || customerReturnDecisionReadFence is not null || remainderDecisionReadFence is not null || quoteDecisionReadFence is not null || overDeliveryRequestDecisionReadFence is not null
            || ownedRequestApproval is not null || ownedQuoteApproval is not null || ownedPriceFloorApproval is not null
            || ownedDiscountApproval is not null || ownedOverDeliveryRequestApproval is not null || ownedRemainderDecision is not null || ownedCustomerReturnDecision is not null || ownedContractDiscountDecision is not null
            || ownedRiskExceptionDecision is not null && !allowOwnedRiskReceipt)
            throw new InvalidOperationException("Risk karar okuması başka bir owned/read işlemin içine giremez.");
        riskExceptionDecisionReadFence = Fence;
        try { return await read(); }
        finally { riskExceptionDecisionReadFence = null; }
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); RequireCompany(company); await freshFence(ct);
            ct.ThrowIfCancellationRequested(); RequireCompany(company);
        }
    }
    public Task<SalesRiskExceptionDecisionSourceV1?> ReadRiskExceptionDecisionSourceAsync(Guid exceptionId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default) =>
        WithRiskExceptionDecisionReadAsync(() => ReadRiskExceptionDecisionSourceCoreAsync(exceptionId, cancellationToken), freshFence, cancellationToken);

    private async Task<SalesRiskExceptionDecisionSourceV1?> ReadRiskExceptionDecisionSourceCoreAsync(Guid exceptionId, CancellationToken cancellationToken)
    {
        var company = RequireResolvedCompany(); await RiskExceptionDecisionReadFenceAsync(cancellationToken);
        // One company-scoped statement binds the selected exception, current order readiness and append-only drop mirror.
        var rows = await (from risk in dbContext.ShipmentRiskExceptions.AsNoTracking()
                          join order in dbContext.Drafts.AsNoTracking() on new { risk.CompanyId, Id = risk.OrderId } equals new { order.CompanyId, order.Id }
                          where risk.CompanyId == company && order.CompanyId == company && risk.Id == exceptionId
                          select new { Risk = risk, Order = order,
                              HasApprovedRevision = dbContext.DraftRevisions.Any(x => x.CompanyId == company && x.DraftId == order.Id),
                              DropCount = dbContext.ShipmentRiskExceptionUsages.Count(x => x.CompanyId == company && x.ExceptionId == risk.Id && x.Kind == (int)SalesRiskExceptionUsageKind.Dropped),
                              Dropped = dbContext.ShipmentRiskExceptionUsages.Where(x => x.CompanyId == company && x.ExceptionId == risk.Id && x.Kind == (int)SalesRiskExceptionUsageKind.Dropped).Select(x => (Guid?)x.ShipmentId).FirstOrDefault() })
            .Take(2).ToArrayAsync(cancellationToken);
        await RiskExceptionDecisionReadFenceAsync(cancellationToken);
        if (rows.Length == 0) return null;
        if (rows.Length != 1 || rows[0].DropCount > 1 || !Enum.IsDefined((SalesDraftState)rows[0].Order.State)
            || !Enum.IsDefined((InternalApprovalState)rows[0].Order.ApprovalState)) throw new InvalidOperationException("Risk karar kaydı belirsiz.");
        var r = rows[0]; var item = SalesRiskExceptionRows.Restore(r.Risk);
        if (r.Dropped is { } drop) item.ApplyShipmentDropped(drop);
        return new(company, r.Order.DocumentNo, r.Order.Version, ((SalesDraftState)r.Order.State).ToString(),
            ((InternalApprovalState)r.Order.ApprovalState).ToString(), r.HasApprovedRevision,
            new(item.Id, item.OrderId, item.ExceptionNo, item.LoadingInstructionId, item.State.ToString(), item.Version, item.Reason,
                item.RequestedBy, item.RequestedAtUtc, item.Decision?.DecidedBy, item.Decision?.DecidedAtUtc, item.Decision?.RejectionReason, item.ValidOn), r.Dropped);
    }
}
