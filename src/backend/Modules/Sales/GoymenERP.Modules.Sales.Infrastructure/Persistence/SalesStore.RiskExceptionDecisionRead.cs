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
    public async Task<SalesRiskExceptionDecisionSourceV1?> ReadRiskExceptionDecisionSourceAsync(Guid exceptionId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshFence); var company = RequireResolvedCompany();
        if (riskExceptionDecisionReadFence is not null) throw new InvalidOperationException("Risk okuma sınırı zaten kullanılıyor.");
        riskExceptionDecisionReadFence = freshFence;
        try
        {
            await Fence();
            // One company-scoped statement binds the selected exception, current order readiness and append-only drop mirror.
            var rows = await (from risk in dbContext.ShipmentRiskExceptions.AsNoTracking()
                              join order in dbContext.Drafts.AsNoTracking() on new { risk.CompanyId, Id = risk.OrderId } equals new { order.CompanyId, order.Id }
                              where risk.CompanyId == company && order.CompanyId == company && risk.Id == exceptionId
                              select new { Risk = risk, Order = order,
                                  HasApprovedRevision = dbContext.DraftRevisions.Any(x => x.CompanyId == company && x.DraftId == order.Id),
                                  DropCount = dbContext.ShipmentRiskExceptionUsages.Count(x => x.CompanyId == company && x.ExceptionId == risk.Id && x.Kind == (int)SalesRiskExceptionUsageKind.Dropped),
                                  Dropped = dbContext.ShipmentRiskExceptionUsages.Where(x => x.CompanyId == company && x.ExceptionId == risk.Id && x.Kind == (int)SalesRiskExceptionUsageKind.Dropped).Select(x => (Guid?)x.ShipmentId).FirstOrDefault() })
                .Take(2).ToArrayAsync(cancellationToken);
            await Fence();
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
        finally { riskExceptionDecisionReadFence = null; }
        async Task Fence()
        {
            cancellationToken.ThrowIfCancellationRequested(); RequireCompany(company); await freshFence(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); RequireCompany(company);
        }
    }
}
