using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// The one place that says which stock the moving-average ledger costs and how a ledger row reads as a position, so the
/// ledger writer and the company-wide read cannot drift apart.
/// </summary>
internal static class InventoryAverageCostLedgerRows
{
    /// <summary>
    /// The physical stock the purchase average is weighted by (SAT-D347 "depodaki eldeki kg"): the company's own goods
    /// (owner = the resolved company), whoever holds them, free and blocked alike (a block does not move goods). Goods a
    /// counterparty owns never had a purchase cost of this company, so they are not part of its average. That reading of
    /// "eldeki kg" is the author's (reversible; reported with the slice).
    /// </summary>
    public static IQueryable<BalanceRow> CostedStock(InventoryDbContext dbContext, Guid companyId, Guid productId) =>
        dbContext.Balances.AsNoTracking()
            .Where(balance => balance.CompanyId == companyId && balance.ProductId == productId
                && balance.OwnerKind == (int)StockPartyKind.Company && balance.OwnerId == companyId
                && balance.QuantityKg > 0m);

    /// <summary>A stored row as the cost core's position. A row the schema checks forbid fails closed, never a figure.</summary>
    public static AverageCostPosition PositionOf(AverageCostLedgerRow row) => (AverageCostStatus)row.Status switch
    {
        AverageCostStatus.Known when row.QuantityKg is { } quantityKg && row.AverageCostPerKg is { } average
            && row.UnreadableReason is null => AverageCostPosition.Known(quantityKg, average),
        AverageCostStatus.Unreadable when row.UnreadableReason is { } reason
            && Enum.IsDefined((AverageCostUnreadableReason)reason)
            && row.QuantityKg is null && row.AverageCostPerKg is null =>
            AverageCostPosition.Unreadable((AverageCostUnreadableReason)reason),
        _ => throw new InvalidOperationException("Maliyet defteri satırı tanınmayan durumda."),
    };

    /// <summary>Writes a position onto a row (status, reason and figures only; key, version and time are the writer's).</summary>
    public static void Write(AverageCostLedgerRow row, AverageCostPosition position)
    {
        row.Status = (int)position.Status;
        row.UnreadableReason = position.UnreadableReason is { } reason ? (int)reason : null;
        row.QuantityKg = position.QuantityKg;
        row.AverageCostPerKg = position.AverageCostPerKg;
    }
}
