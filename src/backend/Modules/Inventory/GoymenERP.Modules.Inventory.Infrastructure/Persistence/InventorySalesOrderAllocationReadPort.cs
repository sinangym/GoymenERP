using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// DEC-SAL-005 ("aktif tahsis"): the Inventory-owned answer to the Sales-declared <see cref="ISalesOrderAllocationReadPortV1"/>
/// — per Sales order line, the number of ACTIVE physical allocations Inventory recorded against it (demand kind
/// <see cref="InventoryAllocationDemandKindsV1.SalesOrderLine"/>, INV-AL-1) and the kilograms they still hold. It reads
/// Inventory's own company-filtered allocations only (another company's are invisible, never a 403), untracked, and creates
/// nothing: no attempt, audit, movement or version effect. Released and consumed allocations are not active and are not
/// counted. The port carries no authority (the AT-024 / LOG-4 D6 shape): the Sales caller measures its own name around the
/// call. Sales never reads Inventory tables; Inventory never learns why it was asked.
/// </summary>
internal sealed class InventorySalesOrderAllocationReadPort(InventoryDbContext dbContext, ICompanyContext companyContext)
    : ISalesOrderAllocationReadPortV1
{
    public async Task<SalesOrderAllocationReadAnswerV1> ReadOrderAllocationsAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            // Without a server-resolved company there is no tenant filter to read under: refuse, never read unscoped.
            throw new InvalidOperationException("Tahsis okuması yalnız çözümlenmiş şirket bağlamında yapılır.");
        }

        var companyId = companyContext.CompanyId;
        if (orderId == Guid.Empty)
        {
            return new SalesOrderAllocationReadAnswerV1(companyId, orderId, []);
        }

        const int active = (int)StockAllocationStatus.Active;
        var totals = await dbContext.Allocations.AsNoTracking()
            .Where(row => row.CompanyId == companyId
                && row.DemandKind == InventoryAllocationDemandKindsV1.SalesOrderLine
                && row.DemandId == orderId
                && row.DemandLineId != null
                && row.Status == active)
            .GroupBy(row => row.DemandLineId!.Value)
            .Select(group => new { LineId = group.Key, Count = group.Count(), RemainingKg = group.Sum(row => row.RemainingKg) })
            .ToListAsync(cancellationToken);

        return new SalesOrderAllocationReadAnswerV1(companyId, orderId,
            [.. totals.OrderBy(total => total.LineId).Select(total => new SalesOrderLineAllocationV1(total.LineId, total.Count, total.RemainingKg))]);
    }
}
