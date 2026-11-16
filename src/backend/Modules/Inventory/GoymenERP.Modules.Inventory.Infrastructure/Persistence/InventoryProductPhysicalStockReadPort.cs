using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// STK-S24: the Inventory-owned answer to <see cref="IInventoryProductPhysicalStockReadPortV1"/>. One grouped, untracked read
/// of the company's own Free balances it holds itself; the tenant query filter and the explicit company predicate both apply.
/// </summary>
internal sealed class InventoryProductPhysicalStockReadPort(InventoryDbContext dbContext, ICompanyContext companyContext)
    : IInventoryProductPhysicalStockReadPortV1
{
    public async Task<InventoryProductPhysicalStockReadV1> ReadAsync(
        Guid companyId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(productIds);
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || companyId != companyContext.CompanyId
            || productIds.Count == 0 || productIds.Contains(Guid.Empty))
        {
            return InventoryProductPhysicalStockReadV1.Unreadable;
        }

        var requested = productIds.Distinct().ToArray();
        var sums = await dbContext.Balances.AsNoTracking()
            .Where(balance => balance.CompanyId == companyId && requested.Contains(balance.ProductId)
                && balance.Block == (int)BlockState.Free
                && balance.OwnerKind == (int)StockPartyKind.Company && balance.OwnerId == companyId
                && balance.CustodianKind == (int)StockPartyKind.Company && balance.CustodianId == companyId)
            .GroupBy(balance => balance.ProductId)
            .Select(group => new { ProductId = group.Key, QuantityKg = group.Sum(balance => balance.QuantityKg) })
            .ToListAsync(cancellationToken);

        var byProduct = requested.ToDictionary(productId => productId, _ => 0m);
        foreach (var sum in sums)
        {
            if (sum.QuantityKg < 0m)
            {
                // A negative physical balance breaks SAT-D318 (1): fail closed, never a figure.
                return InventoryProductPhysicalStockReadV1.Unreadable;
            }

            byProduct[sum.ProductId] = sum.QuantityKg;
        }

        return new InventoryProductPhysicalStockReadV1(true, byProduct);
    }
}
