using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// SAT-D272 / SAT-D347 / SAT-D350 (STK-S35): the Inventory-owned company-wide average-cost read (contract V1). It reads the
/// resolved company's own company-filtered balances and ledger only (another company's rows are invisible), untracked, and
/// creates nothing. The aggregation is <see cref="CompanyWideAverageCostCalculator"/>, the one deterministic rule; this
/// class only gathers its input and maps its result. The port carries no authority: the Sales caller fences around it.
/// <para>
/// The stock and the ledger are read by two statements. A costed movement always changes the ledger quantity of every
/// position it touches, and a known position is only used when its quantity equals the stock read, so a movement that
/// commits between the two statements can only surface as a transient <c>uncosted_stock_on_hand</c>, never as a figure
/// mixed from two moments.
/// </para>
/// </summary>
internal sealed class InventoryCompanyWideAverageCostReadPort(InventoryDbContext dbContext, ICompanyContext companyContext)
    : IInventoryCompanyWideAverageCostReadPortV1
{
    public async Task<InventoryCompanyWideAverageCostReadAnswerV1> ReadCompanyWideAsync(
        Guid productId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            // Without a server-resolved company there is no tenant filter to read under: refuse, never read unscoped.
            throw new InvalidOperationException("Maliyet okuması yalnız çözümlenmiş şirket bağlamında yapılır.");
        }

        var companyId = companyContext.CompanyId;
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("The product is named.", nameof(productId));
        }

        var stock = await InventoryAverageCostLedgerRows.CostedStock(dbContext, companyId, productId)
            .GroupBy(balance => balance.LocationId)
            .Select(group => new { LocationId = group.Key, OnHandKg = group.Sum(balance => balance.QuantityKg) })
            .ToListAsync(cancellationToken);
        if (stock.Count == 0)
        {
            return InventoryCompanyWideAverageCostReadAnswerV1.ForNoSource(companyId, productId);
        }

        var stockedLocations = stock.Select(entry => entry.LocationId).ToArray();
        var ledger = await dbContext.AverageCostLedger.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.ProductId == productId
                && stockedLocations.Contains(row.LocationId))
            .ToDictionaryAsync(row => row.LocationId, cancellationToken);
        var exactPools = await dbContext.ExactCostPools.AsNoTracking().Where(row => row.CompanyId == companyId && row.ProductId == productId
            && row.CurrencyId == GoymenERP.Modules.Settings.Application.Contracts.V1.SettingsCurrencyIdentitiesV1.TurkishLira
            && stockedLocations.Contains(row.LocationId)).ToDictionaryAsync(row => row.LocationId, cancellationToken);

        var result = CompanyWideAverageCostCalculator.Aggregate(stock.Select(entry => new CompanyWideAverageCostLocation(
            entry.LocationId, entry.OnHandKg,
            ledger.TryGetValue(entry.LocationId, out var row)
                ? exactPools.TryGetValue(entry.LocationId, out var exact) && !InventoryAverageCostReadPort.ExactMatches(exact, row, entry.OnHandKg)
                    ? AverageCostPosition.Unreadable(AverageCostUnreadableReason.LedgerStockMismatch)
                    : InventoryAverageCostLedgerRows.PositionOf(row) : null)));
        var sources = result.Included.Select(location => new InventoryCompanyWideAverageCostSourceV1(
            location.LocationId, location.OnHandKg,
            ledger.TryGetValue(location.LocationId, out var row) ? row.Version : null));

        return result.Status switch
        {
            CompanyWideAverageCostStatus.Known => InventoryCompanyWideAverageCostReadAnswerV1.ForValue(
                companyId, productId, result.AverageCostPerKg!.Value, sources),
            CompanyWideAverageCostStatus.Unreadable => InventoryCompanyWideAverageCostReadAnswerV1.ForUnreadable(
                companyId, productId, InventoryAverageCostReadPort.CodeOf((int)result.UnreadableReason!.Value),
                result.UnreadableLocationId!.Value, sources),
            CompanyWideAverageCostStatus.NoSource => InventoryCompanyWideAverageCostReadAnswerV1.ForNoSource(companyId, productId),
            _ => throw new InvalidOperationException("Şirket geneli maliyet sonucu tanınmayan durumda."),
        };
    }
}
