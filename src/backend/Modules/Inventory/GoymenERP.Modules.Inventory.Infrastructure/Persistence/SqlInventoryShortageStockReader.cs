using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>The stock side of a shortage read: free unreserved kg per product and active reserved kg per Sales line.</summary>
internal sealed record InventoryShortageStockSnapshot(
    IReadOnlyDictionary<Guid, decimal> FreeUnreservedKgByProduct,
    IReadOnlyDictionary<(Guid OrderId, Guid LineId), decimal> ReservedKgBySalesLine);

internal interface IInventoryShortageStockReader
{
    Task<InventoryShortageStockSnapshot> ReadAsync(Guid companyId, IReadOnlyCollection<Guid> productIds,
        IReadOnlyCollection<Guid> salesOrderIds, CancellationToken cancellationToken);
}

/// <summary>One Free balance or one active Free allocation, keyed by its exact physical position (label + owner + custodian + location).</summary>
internal sealed record InventoryShortagePositionFact(
    Guid LabelId,
    int OwnerKind,
    Guid OwnerId,
    int CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    Guid ProductId,
    decimal QuantityKg);

/// <summary>
/// KLN-D42 (STK-S31): reads Inventory's own rows only, always under an explicit company predicate, and writes nothing.
/// <list type="bullet">
/// <item>Free stock of a product = Σ over its Free positions of <see cref="StockAllocation.Unreserved"/>(the position's Free
/// kilograms, the active allocations held at that exact position) — the same per-position rule the label disposition read uses,
/// so a reservation is never counted as free and an over-reserved position adds nothing.</item>
/// <item>Only the company's OWN stock counts (owner = the company): a counterparty's goods in our custody are not ours to sell.
/// This conservative reading is reported as NEEDS-SINAN.</item>
/// <item>Reserved kilograms of a Sales line = Σ remaining kilograms of its active <see cref="InventoryAllocationDemandKindsV1.SalesOrderLine"/>
/// allocations, whatever the position.</item>
/// </list>
/// </summary>
internal sealed class SqlInventoryShortageStockReader(InventoryDbContext dbContext) : IInventoryShortageStockReader
{
    public async Task<InventoryShortageStockSnapshot> ReadAsync(Guid companyId, IReadOnlyCollection<Guid> productIds,
        IReadOnlyCollection<Guid> salesOrderIds, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
        {
            throw new InvalidOperationException("Eksik uyarısı stok okuması yalnız çözümlenmiş şirket bağlamında çalışır.");
        }

        const int free = (int)BlockState.Free;
        const int active = (int)StockAllocationStatus.Active;
        const int company = (int)StockPartyKind.Company;
        var products = productIds.Distinct().ToArray();
        var orders = salesOrderIds.Distinct().ToArray();

        var balances = products.Length == 0
            ? []
            : await dbContext.Balances.AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.Block == free && row.OwnerKind == company
                    && row.OwnerId == companyId && products.Contains(row.ProductId) && row.QuantityKg > 0m)
                .Select(row => new InventoryShortagePositionFact(row.LabelId, row.OwnerKind, row.OwnerId, row.CustodianKind,
                    row.CustodianId, row.LocationId, row.ProductId, row.QuantityKg))
                .ToListAsync(cancellationToken);

        var allocations = products.Length == 0
            ? []
            : await dbContext.Allocations.AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.Status == active && row.Block == free
                    && row.OwnerKind == company && row.OwnerId == companyId
                    && dbContext.Balances.Any(balance => balance.CompanyId == companyId && balance.LabelId == row.LabelId
                        && balance.Block == free && products.Contains(balance.ProductId)))
                .Select(row => new InventoryShortagePositionFact(row.LabelId, row.OwnerKind, row.OwnerId, row.CustodianKind,
                    row.CustodianId, row.LocationId, Guid.Empty, row.RemainingKg))
                .ToListAsync(cancellationToken);

        var reserved = orders.Length == 0
            ? []
            : await dbContext.Allocations.AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.Status == active
                    && row.DemandKind == InventoryAllocationDemandKindsV1.SalesOrderLine
                    && row.DemandId != null && row.DemandLineId != null && orders.Contains(row.DemandId.Value))
                .GroupBy(row => new { OrderId = row.DemandId!.Value, LineId = row.DemandLineId!.Value })
                .Select(group => new { group.Key.OrderId, group.Key.LineId, RemainingKg = group.Sum(row => row.RemainingKg) })
                .ToListAsync(cancellationToken);

        return new InventoryShortageStockSnapshot(
            FreeUnreserved(balances, allocations),
            reserved.ToDictionary(row => (row.OrderId, row.LineId), row => row.RemainingKg));
    }

    /// <summary>The pure part, separated from the queries so the per-position rule has one testable place.</summary>
    internal static IReadOnlyDictionary<Guid, decimal> FreeUnreserved(IReadOnlyList<InventoryShortagePositionFact> balances,
        IReadOnlyList<InventoryShortagePositionFact> allocations)
    {
        var allocatedAt = allocations
            .GroupBy(Key)
            .ToDictionary(group => group.Key, group => group.Sum(fact => fact.QuantityKg));
        var result = new Dictionary<Guid, decimal>();
        foreach (var position in balances.GroupBy(fact => (Key: Key(fact), fact.ProductId)))
        {
            var freeKg = position.Sum(fact => fact.QuantityKg);
            var unreserved = StockAllocation.Unreserved(Kilograms.Of(freeKg),
                Kilograms.Of(allocatedAt.GetValueOrDefault(position.Key.Key))).Value;
            result[position.Key.ProductId] = result.GetValueOrDefault(position.Key.ProductId) + unreserved;
        }

        return result;
    }

    private static (Guid LabelId, int OwnerKind, Guid OwnerId, int CustodianKind, Guid CustodianId, Guid LocationId) Key(
        InventoryShortagePositionFact fact) =>
        (fact.LabelId, fact.OwnerKind, fact.OwnerId, fact.CustodianKind, fact.CustodianId, fact.LocationId);
}
