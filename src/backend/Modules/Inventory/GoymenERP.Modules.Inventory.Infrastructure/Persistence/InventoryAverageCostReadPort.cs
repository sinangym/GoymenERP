using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// SAT-D262: the Inventory-owned answer to the average-cost read (contract V1). It reads Inventory's own company-filtered
/// ledger only (another company's rows are invisible), untracked, and creates nothing. No row is NoSource; an unreadable
/// row is Unreadable with its code; a known row is the stored 4-decimal average, returned as stored (no recalculation, no
/// rounding here). The port carries no authority: the Sales caller fences around the call.
/// </summary>
internal sealed class InventoryAverageCostReadPort(InventoryDbContext dbContext, ICompanyContext companyContext)
    : IInventoryAverageCostReadPortV1
{
    public async Task<InventoryAverageCostReadAnswerV1> ReadAsync(
        Guid locationId, Guid productId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            // Without a server-resolved company there is no tenant filter to read under: refuse, never read unscoped.
            throw new InvalidOperationException("Maliyet okuması yalnız çözümlenmiş şirket bağlamında yapılır.");
        }

        var companyId = companyContext.CompanyId;
        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("The location is named.", nameof(locationId));
        }

        if (productId == Guid.Empty)
        {
            throw new ArgumentException("The product is named.", nameof(productId));
        }

        var row = await dbContext.AverageCostLedger.AsNoTracking()
            .Where(candidate => candidate.CompanyId == companyId
                && candidate.LocationId == locationId && candidate.ProductId == productId)
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return InventoryAverageCostReadAnswerV1.ForNoSource(companyId, locationId, productId);
        }

        return (AverageCostStatus)row.Status switch
        {
            AverageCostStatus.Known when row.AverageCostPerKg is { } average =>
                InventoryAverageCostReadAnswerV1.ForValue(companyId, locationId, productId, average, row.Version),
            AverageCostStatus.Unreadable when row.UnreadableReason is { } reason =>
                InventoryAverageCostReadAnswerV1.ForUnreadable(companyId, locationId, productId, CodeOf(reason), row.Version),
            // A row the schema checks forbid: fail closed, never a figure.
            _ => throw new InvalidOperationException("Maliyet defteri satırı tanınmayan durumda."),
        };
    }

    internal static string CodeOf(int reason) => (AverageCostUnreadableReason)reason switch
    {
        AverageCostUnreadableReason.NegativeStock => InventoryAverageCostUnreadableCodesV1.NegativeStock,
        AverageCostUnreadableReason.UnitConversion => InventoryAverageCostUnreadableCodesV1.UnitConversion,
        AverageCostUnreadableReason.ForeignCurrency => InventoryAverageCostUnreadableCodesV1.ForeignCurrency,
        AverageCostUnreadableReason.LandedOrAdditionalCost => InventoryAverageCostUnreadableCodesV1.LandedOrAdditionalCost,
        AverageCostUnreadableReason.Return => InventoryAverageCostUnreadableCodesV1.Return,
        AverageCostUnreadableReason.ProductionSource => InventoryAverageCostUnreadableCodesV1.ProductionSource,
        AverageCostUnreadableReason.TransferSourceUnreadable => InventoryAverageCostUnreadableCodesV1.TransferSourceUnreadable,
        AverageCostUnreadableReason.LedgerStockMismatch => InventoryAverageCostUnreadableCodesV1.LedgerStockMismatch,
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };
}
