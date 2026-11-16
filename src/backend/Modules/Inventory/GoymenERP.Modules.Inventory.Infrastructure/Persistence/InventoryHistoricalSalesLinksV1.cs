using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>Read-only owner evidence. No status filter, query-filter bypass, request-intent inference or write.</summary>
public sealed class InventoryHistoricalSalesLinksV1(InventoryDbContext database, ICompanyContext company)
    : IInventoryHistoricalSalesLinksV1
{
    public async Task<InventoryHistoricalSalesLinksAnswerV1> ReadAsync(Guid purchaseOrderId, Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        InventoryHistoricalSalesLinksAnswerV1 Unavailable() => new(InventoryHistoricalSalesLinksOutcomeV1.Unavailable,
            companyId, purchaseOrderId, purchaseOrderLineId, []);
        if (companyId == Guid.Empty || purchaseOrderId == Guid.Empty || purchaseOrderLineId == Guid.Empty) return Unavailable();
        try
        {
            var rows = await database.FutureSupplyAllocations.AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.PurchaseOrderId == purchaseOrderId
                    && row.PurchaseOrderLineId == purchaseOrderLineId)
                .Select(row => new { row.CompanyId, row.SourceKind, row.DemandKind, row.DemandId, row.DemandLineId, row.Version })
                .ToListAsync(cancellationToken);
            if (!company.IsResolved || company.CompanyId != companyId
                || rows.Any(row => row.CompanyId != companyId || row.SourceKind != InventoryFutureAllocationKindsV1.PurchaseOrderLine
                    || row.DemandKind != InventoryFutureAllocationKindsV1.SalesOrderLine || row.DemandId == Guid.Empty
                    || row.DemandLineId == Guid.Empty || row.Version < 1)) return Unavailable();
            var lines = rows.Select(row => new InventoryHistoricalSalesLineV1(row.DemandId, row.DemandLineId))
                .Distinct().OrderBy(row => row.SalesOrderId).ThenBy(row => row.SalesOrderLineId).ToArray();
            return new(InventoryHistoricalSalesLinksOutcomeV1.Available, companyId, purchaseOrderId, purchaseOrderLineId, lines);
        }
        // Error categories only: this reader opens no connection and emits no SQL text.
        catch (DbException) { return Unavailable(); }
        catch (InvalidOperationException) { return Unavailable(); }
    }
}
