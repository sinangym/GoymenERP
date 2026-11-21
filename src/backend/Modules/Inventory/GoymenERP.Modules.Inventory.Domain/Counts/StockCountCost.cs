using GoymenERP.Modules.Inventory.Domain.Costing;

namespace GoymenERP.Modules.Inventory.Domain.Counts;

/// <summary>What the cost rule answers for one approved count difference (SAT-D331, SAT-D334).</summary>
public enum StockCountCostStatus
{
    /// <summary>The difference is valued at the position's readable average: <see cref="StockCountCost.Amount"/> holds it.</summary>
    Known = 1,

    /// <summary>
    /// SAT-D331 (3): a surplus whose average cannot be read is still added, with its cost "hesaplanamadı" — never a
    /// guessed figure and never zero.
    /// </summary>
    NotCalculated = 2,

    /// <summary>
    /// SAT-D334 (2): a shortage whose average cannot be read is not deducted: the system says "hesaplanamadı" and the
    /// deduction stops.
    /// </summary>
    Refused = 3,
}

/// <summary>
/// The cost of one approved count difference. <see cref="Amount"/> is the difference's kilograms times the average per
/// kilogram and exists only when <see cref="StockCountCostStatus.Known"/>; otherwise it is null, never zero.
/// <see cref="UnreadableReason"/> names why the ledger position is unreadable; it is null both for a known cost and for a
/// position the ledger never recorded (no source).
/// </summary>
public sealed record StockCountCost
{
    private StockCountCost(StockCountCostStatus status, decimal? averageCostPerKg, decimal? amount,
        AverageCostUnreadableReason? unreadableReason)
    {
        Status = status;
        AverageCostPerKg = averageCostPerKg;
        Amount = amount;
        UnreadableReason = unreadableReason;
    }

    public StockCountCostStatus Status { get; }

    /// <summary>The average the difference was valued at; only with <see cref="StockCountCostStatus.Known"/>.</summary>
    public decimal? AverageCostPerKg { get; }

    /// <summary>|difference| kg × <see cref="AverageCostPerKg"/>, exact (no rounding); only with a known cost.</summary>
    public decimal? Amount { get; }

    public AverageCostUnreadableReason? UnreadableReason { get; }

    internal static StockCountCost Known(decimal averageCostPerKg, decimal amount) =>
        new(StockCountCostStatus.Known, averageCostPerKg, amount, null);

    internal static StockCountCost NotCalculated(AverageCostUnreadableReason? reason) =>
        new(StockCountCostStatus.NotCalculated, null, null, reason);

    internal static StockCountCost Refused(AverageCostUnreadableReason? reason) =>
        new(StockCountCostStatus.Refused, null, null, reason);

    /// <summary>
    /// The movement cost of the approved adjustment in the company's base currency (the ledger's currency, SAT-D262):
    /// a known amount, or "not calculated". A refused cost has no movement: asking for one is a programming error.
    /// </summary>
    public CostResult ToMovementCost(Guid baseCurrencyId) => Status switch
    {
        StockCountCostStatus.Known => CostResult.Known(Amount!.Value, baseCurrencyId),
        StockCountCostStatus.NotCalculated => CostResult.NotCalculated,
        _ => throw new InventoryRuleException(InventoryRuleCodes.InvalidStockCount,
            "Ortalama maliyeti okunamayan sayım eksiği düşülmez; hareket maliyeti yoktur."),
    };
}

/// <summary>
/// SAT-D331 / SAT-D334 (SINAN_KARARLARI_2026-11-13_EK1): the cost of an approved count difference, read from the
/// moving-average purchase cost of the same product at the same location. Pure and deterministic; it never writes the
/// ledger.
/// <list type="bullet">
/// <item><b>Surplus</b> (SAT-D331): added at the position's average; the average does not change. A position whose stock
/// fell to exactly zero still holds its last average as known (SAT-D296), so that last average is used (SAT-D331 (2)).
/// An unreadable position, or one the ledger never recorded, leaves the cost "hesaplanamadı"
/// (<see cref="StockCountCostStatus.NotCalculated"/>); the surplus itself is still added (SAT-D331 (3)). The rule holds for
/// every surplus, not only for a label shown at zero (SAT-D331 (4)).</item>
/// <item><b>Shortage</b> (SAT-D334): deducted at the position's current average; the average does not change. An
/// unreadable or never-recorded position stops the deduction (<see cref="StockCountCostStatus.Refused"/>).</item>
/// </list>
/// "The average does not change" means this rule never produces a new average: the ledger keeps the one it holds. Moving
/// the ledger's kilograms with the difference belongs to the wiring of the ledger to stock movements, which SAT-D331 (5)
/// places before this rule takes effect on an approval.
/// </summary>
public static class StockCountCostCalculator
{
    /// <param name="count">The approved count; it must carry a difference.</param>
    /// <param name="average">The ledger position of the count's location and product, or null when the ledger has none.</param>
    public static StockCountCost For(StockCount count, AverageCostPosition? average)
    {
        ArgumentNullException.ThrowIfNull(count);
        return ForDifference(count.Difference, average);
    }

    /// <summary>The same rule for the actual approved adjustment's signed kilograms.</summary>
    public static StockCountCost ForDifference(decimal differenceKg, AverageCostPosition? average)
    {
        if (differenceKg == 0m)
        {
            throw new InventoryRuleException(InventoryRuleCodes.InvalidStockCount,
                "Farkı olmayan sayımın maliyet etkisi yoktur.");
        }

        var surplus = differenceKg > 0m;
        if (average is not { Status: AverageCostStatus.Known, AverageCostPerKg: { } averageCostPerKg })
        {
            var reason = average?.UnreadableReason;
            return surplus ? StockCountCost.NotCalculated(reason) : StockCountCost.Refused(reason);
        }

        return StockCountCost.Known(averageCostPerKg, Math.Abs(differenceKg) * averageCostPerKg);
    }
}
