using GoymenERP.Modules.Inventory.Domain.Costing;

namespace GoymenERP.Modules.Inventory.Application;

public sealed record InventoryManufacturingTlSplitV1(decimal OriginalTlAmount, decimal ConsumedTlAmount, decimal RemainingTlAmount);

/// <summary>User's W8 answer: round the already shipped share once to TL 2dp AwayFromZero, retain the exact authoritative
/// label residual in stock. Accounting receives this final value and does not independently round it again.</summary>
public static class InventoryManufacturingCostSplitCalculatorV1
{
    public const string Version = "inventory.manufacturing-cost-split.v1";
    public static InventoryManufacturingTlSplitV1 Split(decimal originalTlAmount, decimal totalKg, decimal consumedKg)
        => Split(originalTlAmount, totalKg, ExactCostRatio.Of(consumedKg));

    public static InventoryManufacturingTlSplitV1 Split(decimal originalTlAmount, decimal totalKg, ExactCostRatio consumedKg)
    {
        if (originalTlAmount < 0m || decimal.Round(originalTlAmount, 2) != originalTlAmount || totalKg < 0m
            || consumedKg.Sign < 0 || consumedKg.CompareTo(ExactCostRatio.Of(totalKg)) > 0 || totalKg == 0m && originalTlAmount != 0m)
            throw new ArgumentOutOfRangeException(nameof(originalTlAmount));
        var consumed = totalKg == 0m ? 0m : ExactCostRatio.Of(originalTlAmount).Multiply(consumedKg).Divide(totalKg).Round(2);
        return new(originalTlAmount, consumed, originalTlAmount - consumed);
    }
}
