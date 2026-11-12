using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

/// <summary>
/// Üretim U2-b / API-16 (SAT-D219): Manufacturing's own read for Sales's linked-supply question. An OPEN supply is a plan
/// linked to the sales order that is a Draft or Approved and has no order, or whose order is Open; a cancelled plan and a
/// closed order are not open. Reads under the caller's resolved company only (the tenant filter) and creates nothing.
/// </summary>
internal sealed class ManufacturingSalesOrderSupplyReadPort(ManufacturingDbContext dbContext, ICompanyContext companyContext)
    : IManufacturingSalesOrderSupplyReadPortV1
{
    private const int DraftStatus = (int)PlanStatus.Draft;
    private const int ApprovedStatus = (int)PlanStatus.Approved;
    private const int OpenOrderStatus = (int)OrderStatus.Open;

    public async Task<IReadOnlyList<ManufacturingSalesOrderSupplyItemV1>> ListOpenForSalesOrderAsync(Guid companyId,
        Guid salesOrderId, IReadOnlyList<Guid>? salesOrderLineIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || companyContext.CompanyId != companyId)
        {
            // Deny-first: without the caller's resolved company, or for another company, there is no tenant filter to read under.
            throw new InvalidOperationException("Bağlı üretim yalnız çözümlenmiş şirket bağlamında okunur.");
        }

        if (salesOrderId == Guid.Empty
            || salesOrderLineIds is { Count: > IManufacturingSalesOrderSupplyReadPortV1.MaximumLineFilter }
            || salesOrderLineIds?.Contains(Guid.Empty) == true)
        {
            throw new ArgumentException("Bağlı üretim sorgusu geçersiz.", nameof(salesOrderId));
        }

        var lineFilter = salesOrderLineIds is { Count: > 0 } ? salesOrderLineIds.Distinct().ToArray() : null;
        var plans = dbContext.Plans.AsNoTracking()
            .Where(plan => plan.SalesOrderId == salesOrderId && plan.SalesOrderLineId != null
                           && (plan.Status == DraftStatus || plan.Status == ApprovedStatus));
        if (lineFilter is not null)
        {
            plans = plans.Where(plan => lineFilter.Contains(plan.SalesOrderLineId!.Value));
        }

        var rows = await (
            from plan in plans
            join order in dbContext.Orders.AsNoTracking() on new { plan.CompanyId, PlanId = plan.Id }
                equals new { order.CompanyId, order.PlanId } into orders
            from order in orders.DefaultIfEmpty()
            where order == null || order.Status == OpenOrderStatus
            select new
            {
                plan.CompanyId,
                plan.Id,
                plan.Status,
                OrderId = order == null ? (Guid?)null : order.Id,
                OrderStatus = order == null ? (int?)null : order.Status,
                SalesOrderLineId = plan.SalesOrderLineId!.Value,
                PlannedKg = dbContext.PlanInputs.Where(input => input.PlanId == plan.Id).Sum(input => input.PlannedKg),
            })
            .OrderBy(row => row.Id)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new ManufacturingSalesOrderSupplyItemV1(row.CompanyId, row.Id,
            ((PlanStatus)row.Status).ToString(), row.OrderId,
            row.OrderStatus is { } status ? ((OrderStatus)status).ToString() : null,
            row.SalesOrderLineId, row.PlannedKg, null))];
    }
}
