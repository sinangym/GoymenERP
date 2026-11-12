using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

/// <summary>
/// Üretim U2-b / API-16 (SAT-D219): Manufacturing's own read for Sales's linked-supply question. An OPEN supply is a plan
/// linked to the sales order that is a Draft or Approved and has no order, or whose order is Open; a cancelled plan and a
/// closed order are not open. Reads under the caller's resolved company only (the tenant filter) and creates nothing.
/// The planned kg is the plan's CURRENT content (MFG-PLAN-EDIT): the newest revision its row's version covers, else the
/// inputs it was created with — the same rule as the plan list.
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
                plan.Version,
                OrderId = order == null ? (Guid?)null : order.Id,
                OrderStatus = order == null ? (int?)null : order.Status,
                SalesOrderLineId = plan.SalesOrderLineId!.Value,
                CreatedKg = dbContext.PlanInputs.Where(input => input.PlanId == plan.Id).Sum(input => input.PlannedKg),
            })
            .OrderBy(row => row.Id)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        // MFG-PLAN-EDIT: a revision committed after the row was read is above the row's version and not this read's.
        var ids = rows.Select(row => row.Id).ToArray();
        var revisions = await dbContext.PlanRevisions.AsNoTracking()
            .Where(revision => ids.Contains(revision.PlanId))
            .Select(revision => new { revision.PlanId, revision.RevisionNo, revision.PlanVersion })
            .ToListAsync(cancellationToken);
        var currentRevision = rows.ToDictionary(row => row.Id, row => revisions
            .Where(revision => revision.PlanId == row.Id && revision.PlanVersion <= row.Version)
            .Select(revision => revision.RevisionNo).DefaultIfEmpty(0).Max());
        var revised = currentRevision.Where(pair => pair.Value > 0).Select(pair => pair.Key).ToArray();
        var revisionKg = await dbContext.PlanRevisionInputs.AsNoTracking()
            .Where(input => revised.Contains(input.PlanId))
            .GroupBy(input => new { input.PlanId, input.RevisionNo })
            .Select(group => new { group.Key.PlanId, group.Key.RevisionNo, PlannedKg = group.Sum(input => input.PlannedKg) })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new ManufacturingSalesOrderSupplyItemV1(row.CompanyId, row.Id,
            ((PlanStatus)row.Status).ToString(), row.OrderId,
            row.OrderStatus is { } status ? ((OrderStatus)status).ToString() : null,
            row.SalesOrderLineId, PlannedKgOf(row.Id, row.CreatedKg), null))];

        decimal PlannedKgOf(Guid planId, decimal createdKg)
        {
            var revisionNo = currentRevision[planId];
            if (revisionNo == 0)
            {
                return createdKg;
            }

            // A revision always records its complete inputs; without them the stored plan is not answered (fail-closed).
            return revisionKg.SingleOrDefault(input => input.PlanId == planId && input.RevisionNo == revisionNo)?.PlannedKg
                ?? throw new ManufacturingRuleException(ManufacturingRuleCodes.StoredRecordInvalid,
                    "Kayıtlı plan revizyonunun planlanan girdisi yok.");
        }
    }
}
