namespace GoymenERP.Modules.Inventory.Domain.Costing;

public enum AverageCostUnitConversionStatus
{
    /// <summary>The cost per target unit is known.</summary>
    Converted = 1,

    /// <summary>
    /// The coefficient rounds to zero at the policy's decimals: a unit that weighs nothing at that precision carries no
    /// cost, and no figure is given (fail-closed; never a zero cost).
    /// </summary>
    CoefficientNotPositive = 2,
}

/// <summary>
/// The conversion's answer: the kilograms-per-unit coefficient as the policy rounds it (also for a refusal, so the refusal
/// can be shown with its cause) and, only when <see cref="AverageCostUnitConversionStatus.Converted"/>, the cost per unit.
/// </summary>
public sealed record AverageCostUnitConversionResult(
    AverageCostUnitConversionStatus Status,
    decimal Coefficient,
    decimal? CostPerUnit);

/// <summary>
/// KLN-D85 (STK-SC33-a): the moving-average purchase cost per stock kilogram read in another unit (piece, metre…). The
/// kilograms one unit of the product weighs is the input (it comes from the product's facts through the Settings unit
/// conversion policy; the product card has no separate coefficient field, KLN-D85). The policy's coefficient rule rounds
/// it once: <c>CoefficientDecimals</c> decimals with the policy's rounding (URN-D8 / BS-13; KLN-D88: AwayFromZero is the
/// policy value, this core takes the value the policy carries and decides none). The cost per unit is the cost per
/// kilogram times that coefficient, rounded once at the cost's internal scale, AwayFromZero (SAT-D265 (2),
/// <see cref="AverageCostPrecision"/>). Pure: no clock, no storage, no company, no policy default.
/// </summary>
public static class AverageCostUnitConversion
{
    /// <summary>The largest scale <see cref="decimal"/> rounding accepts; a policy outside 0…28 decimals is malformed.</summary>
    public const int MaximumCoefficientDecimals = 28;

    public static AverageCostUnitConversionResult Convert(
        decimal averageCostPerKg, decimal kilogramsPerUnit, int coefficientDecimals, MidpointRounding coefficientRounding)
    {
        if (averageCostPerKg < 0m || !AverageCostPrecision.IsAtInternalScale(averageCostPerKg))
        {
            throw new ArgumentOutOfRangeException(nameof(averageCostPerKg),
                "The average cost is non-negative with at most the internal scale.");
        }

        if (kilogramsPerUnit <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(kilogramsPerUnit), "A unit weighs a positive number of kilograms.");
        }

        if (coefficientDecimals is < 0 or > MaximumCoefficientDecimals)
        {
            throw new ArgumentOutOfRangeException(nameof(coefficientDecimals));
        }

        if (!Enum.IsDefined(coefficientRounding))
        {
            throw new ArgumentOutOfRangeException(nameof(coefficientRounding));
        }

        var coefficient = Math.Round(kilogramsPerUnit, coefficientDecimals, coefficientRounding);
        if (coefficient <= 0m)
        {
            return new(AverageCostUnitConversionStatus.CoefficientNotPositive, coefficient, null);
        }

        return new(AverageCostUnitConversionStatus.Converted, coefficient,
            AverageCostPrecision.Round(averageCostPerKg * coefficient));
    }
}
