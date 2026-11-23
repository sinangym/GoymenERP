using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.FutureSupply;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// F-017 STOK §5.1 (F017-s): the Inventory-owned answer to the Sales-declared <see cref="ISalesOrderAllocationReadPortV2"/> —
/// per Sales order line, BOTH kinds of ACTIVE allocation Inventory records against it, kept apart:
/// <list type="bullet">
/// <item><see cref="SalesOrderAllocationKindsV2.Physical"/>: the physical allocations of demand kind
/// <see cref="InventoryAllocationDemandKindsV1.SalesOrderLine"/> (INV-AL-1), count and the kilograms they still hold, no unit id —
/// exactly the V1 figure of <see cref="InventorySalesOrderAllocationReadPort"/>;</item>
/// <item><see cref="SalesOrderAllocationKindsV2.FutureSupply"/>: the future supply allocations of demand kind
/// <see cref="InventoryFutureAllocationKindsV1.SalesOrderLine"/> (F-017 STOK §3), one entry per purchase unit with that unit's id,
/// count and the remaining quantity in that unit — never converted to kilograms and never added to the physical figure.</item>
/// </list>
/// It reads Inventory's own company-filtered rows only (another company's are invisible, never a 403), untracked, and creates
/// nothing. Released and consumed allocations of either kind are not active and are not counted. No authority (the AT-024 /
/// LOG-4 D6 shape): the Sales caller fences its own name around the call. V1 stays registered beside it, unchanged.
/// </summary>
internal sealed class InventorySalesOrderAllocationReadPortV2(InventoryDbContext dbContext, ICompanyContext companyContext)
    : ISalesOrderAllocationReadPortV2
{
    public async Task<SalesOrderAllocationReadAnswerV2> ReadOrderAllocationsAsync(Guid orderId, CancellationToken cancellationToken = default)
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
            return new SalesOrderAllocationReadAnswerV2(companyId, orderId, []);
        }

        const int physicalActive = (int)StockAllocationStatus.Active;
        var physical = await dbContext.Allocations.AsNoTracking()
            .Where(row => row.CompanyId == companyId
                && row.DemandKind == InventoryAllocationDemandKindsV1.SalesOrderLine
                && row.DemandId == orderId
                && row.DemandLineId != null
                && row.Status == physicalActive)
            .GroupBy(row => row.DemandLineId!.Value)
            .Select(group => new { LineId = group.Key, Count = group.Count(), RemainingKg = group.Sum(row => row.RemainingKg) })
            .ToListAsync(cancellationToken);

        const int futureActive = (int)FutureSupplyAllocationStatus.Active;
        var future = await dbContext.FutureSupplyAllocations.AsNoTracking()
            .Where(row => row.CompanyId == companyId
                && row.DemandKind == InventoryFutureAllocationKindsV1.SalesOrderLine
                && row.DemandId == orderId
                && row.Status == futureActive)
            .GroupBy(row => new { row.DemandLineId, row.PurchaseUnitId })
            .Select(group => new
            {
                LineId = group.Key.DemandLineId,
                UnitId = group.Key.PurchaseUnitId,
                Count = group.Count(),
                Remaining = group.Sum(row => row.RemainingQuantity),
            })
            .ToListAsync(cancellationToken);

        var lines = physical
            .Select(total => new SalesOrderLineAllocationV2(total.LineId, SalesOrderAllocationKindsV2.Physical, total.Count,
                total.RemainingKg, null))
            .Concat(future.Select(total => new SalesOrderLineAllocationV2(total.LineId, SalesOrderAllocationKindsV2.FutureSupply,
                total.Count, total.Remaining, total.UnitId)))
            .OrderBy(line => line.OrderLineId)
            .ThenBy(line => line.Kind, StringComparer.Ordinal)
            .ThenBy(line => line.UnitId)
            .ToArray();
        return new SalesOrderAllocationReadAnswerV2(companyId, orderId, lines);
    }
}
