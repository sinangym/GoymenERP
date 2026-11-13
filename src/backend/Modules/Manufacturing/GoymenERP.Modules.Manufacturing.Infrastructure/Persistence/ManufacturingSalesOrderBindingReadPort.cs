using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

/// <summary>
/// Üretim U5-b / API-36 (SAT-D175): Manufacturing's own read for Sales's binding question. A plan binds the sales order when
/// it is linked to it and is not cancelled — a Draft, or an Approved plan whatever its order's state (a closed order still
/// processed the customer's goods). Reads under the caller's resolved company only (the tenant filter) and creates nothing.
/// </summary>
internal sealed class ManufacturingSalesOrderBindingReadPort(ManufacturingDbContext dbContext, ICompanyContext companyContext)
    : IManufacturingSalesOrderBindingReadPortV1
{
    private const int CancelledStatus = (int)PlanStatus.Cancelled;

    public async Task<bool> HasBindingPlanAsync(Guid companyId, Guid salesOrderId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || companyContext.CompanyId != companyId)
        {
            // Deny-first: without the caller's resolved company, or for another company, there is no tenant filter to read under.
            throw new InvalidOperationException("Satış siparişi bağı yalnız çözümlenmiş şirket bağlamında okunur.");
        }

        if (salesOrderId == Guid.Empty)
        {
            throw new ArgumentException("Satış siparişi bağı sorgusu geçersiz.", nameof(salesOrderId));
        }

        return await dbContext.Plans.AsNoTracking()
            .AnyAsync(plan => plan.SalesOrderId == salesOrderId && plan.Status != CancelledStatus, cancellationToken);
    }
}
