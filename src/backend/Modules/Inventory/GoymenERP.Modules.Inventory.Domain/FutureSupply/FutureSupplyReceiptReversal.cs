namespace GoymenERP.Modules.Inventory.Domain.FutureSupply;

public sealed class FutureSupplyReceiptReversalPrecisionException(string message) : ArgumentException(message);

/// <summary>The exact, already-converted share of one receipt link; no current unit coefficient is substituted.</summary>
public sealed record FutureSupplyReceiptReversalPlan(decimal UndoneQuantity, decimal UndoneKg,
    decimal ReopenedQuantity, decimal OverflowReleasedQuantity, decimal UnheldQuantity);

/// <summary>F017-e / 7B: exact arithmetic over the original conversion and locked owner caps; never rounds quantities.</summary>
public static class FutureSupplyReceiptReversal
{
    public static FutureSupplyReceiptReversalPlan Plan(decimal originalQuantity, decimal originalKg,
        decimal alreadyUndoneQuantity, decimal undoKg, decimal activePhysicalKg, bool demandOpen,
        decimal demandCapacityKg, decimal supplyCapacityQuantity)
    {
        if (originalQuantity <= 0m || originalKg <= 0m || alreadyUndoneQuantity < 0m
            || alreadyUndoneQuantity > originalQuantity || undoKg <= 0m || activePhysicalKg < 0m
            || demandCapacityKg < 0m || supplyCapacityQuantity < 0m)
            throw new ArgumentException("A reversal needs a complete original conversion and nonnegative locked quantities.");
        var undone = ExactRatio(undoKg, originalQuantity, originalKg);
        if (undone > originalQuantity - alreadyUndoneQuantity)
            throw new ArgumentException("A receipt conversion cannot be undone more than once.");
        if (!demandOpen || activePhysicalKg == 0m)
            return new(undone, undoKg, 0m, 0m, undone);

        // A released physical part is not revived. Only the still active original hold is a reopening candidate.
        var eligibleKg = Math.Min(undoKg, activePhysicalKg);
        var eligible = ExactRatio(eligibleKg, originalQuantity, originalKg);
        var fittingKg = Math.Min(eligibleKg, demandCapacityKg);
        var fitting = Math.Min(ExactRatio(fittingKg, originalQuantity, originalKg), supplyCapacityQuantity);
        RequireQuantity(fitting);
        return new(undone, undoKg, fitting, eligible - fitting, undone - eligible);
    }

    /// <summary>Uses only the stamped original conversion; refuses a ratio that cannot be represented exactly.</summary>
    public static decimal ExactRatio(decimal part, decimal originalQuantity, decimal originalKg)
    {
        var converted = checked(part * originalQuantity) / originalKg;
        RequireQuantity(converted);
        if (checked(converted * originalKg) != checked(part * originalQuantity))
            throw new FutureSupplyReceiptReversalPrecisionException("The original conversion has no exact representable partial share.");
        return converted;
    }

    private static void RequireQuantity(decimal value)
    {
        if (value < 0m || decimal.Round(value, FutureSupplyQuantity.MaximumDecimals, MidpointRounding.ToZero) != value)
            throw new FutureSupplyReceiptReversalPrecisionException("A reversal quantity is exact at the stored quantity scale.");
    }
}
