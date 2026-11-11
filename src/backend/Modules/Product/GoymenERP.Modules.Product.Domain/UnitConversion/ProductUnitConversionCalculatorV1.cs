namespace GoymenERP.Modules.Product.Domain.UnitConversion;

/// <summary>DEC-PRD-006 conversion classes. A product conversion is always one of these; there is no default.</summary>
public enum UnitConversionClassV1
{
    Fixed = 1,
    FormulaBound = 2,
    Actual = 3,
    None = 4,
}

public enum UnitConversionOutcomeV1
{
    /// <summary>The value was computed from complete, valid inputs.</summary>
    Computed = 1,

    /// <summary>No conversion source exists ("çevrim yok"); never a guessed or zero value (SAT-D298).</summary>
    NoSource = 2,

    /// <summary>An input was present but invalid (not positive); never rounded into a result.</summary>
    Refused = 3,
}

/// <summary>A conversion result: either a value or an explicit NoSource/Refused, never a silent zero.</summary>
public sealed record UnitConversionResultV1<T>(UnitConversionOutcomeV1 Outcome, T? Value, string? Reason)
    where T : struct
{
    public static UnitConversionResultV1<T> Computed(T value) => new(UnitConversionOutcomeV1.Computed, value, null);

    public static UnitConversionResultV1<T> NoSource(string reason) => new(UnitConversionOutcomeV1.NoSource, null, reason);

    public static UnitConversionResultV1<T> Refused(string reason) => new(UnitConversionOutcomeV1.Refused, null, reason);
}

/// <summary>
/// Company settings for conversion rounding and the weighbridge check. Every value comes from Settings
/// (Sinan 2026-11-11, #66 6806641583: coefficient 3 decimals, document kg total 2 decimals, tolerance 1 percent,
/// all adjustable); the calculator has no built-in default, so a missing setting cannot be silently replaced.
/// </summary>
public sealed record UnitConversionSettingsV1(int CoefficientDecimals, int DocumentTotalDecimals, decimal WeighbridgeTolerancePercent);

/// <summary>
/// Deterministic unit-conversion calculator, version 1 (DEC-PRD-006, B-07). Pure decimal maths shared by the UI,
/// documents and reports. Sheet and coil formulas only; tube/profile cross-section conversion waits for the
/// "Ebat/kalınlık" table design. Intermediate steps are never rounded; rounding happens only in the explicit
/// methods that take <see cref="UnitConversionSettingsV1"/>.
/// </summary>
public static class ProductUnitConversionCalculatorV1
{
    public const string FormulaVersion = "GDC-UC/1 (DEC-PRD-006)";

    // mm * mm * mm * g/cm3 -> kg: 1 mm3 = 1e-3 cm3, 1 g = 1e-3 kg.
    private const decimal CubicMillimetreGramPerCubicCentimetreToKilogram = 0.000001m;

    // mm * mm * g/cm3 per metre of length: 1000 mm of length, then the same scale.
    private const decimal MillimetresPerMetre = 1000m;

    /// <summary>F-A: kg per piece of a cut sheet from thickness, width, length (mm) and density (g/cm3).</summary>
    public static UnitConversionResultV1<decimal> SheetKgPerPiece(decimal? thicknessMm, decimal? widthMm, decimal? lengthMm, decimal? densityGramsPerCubicCm)
    {
        if (thicknessMm is null || widthMm is null || lengthMm is null || densityGramsPerCubicCm is null)
        {
            return UnitConversionResultV1<decimal>.NoSource("A sheet conversion needs thickness, width, length and density.");
        }

        if (thicknessMm <= 0 || widthMm <= 0 || lengthMm <= 0 || densityGramsPerCubicCm <= 0)
        {
            return UnitConversionResultV1<decimal>.Refused("Sheet conversion inputs must be positive.");
        }

        return UnitConversionResultV1<decimal>.Computed(
            thicknessMm.Value * widthMm.Value * lengthMm.Value * densityGramsPerCubicCm.Value * CubicMillimetreGramPerCubicCentimetreToKilogram);
    }

    /// <summary>F-B: kg per metre of coil or slit coil from thickness, width (mm) and density (g/cm3).</summary>
    public static UnitConversionResultV1<decimal> CoilKgPerMetre(decimal? thicknessMm, decimal? widthMm, decimal? densityGramsPerCubicCm)
    {
        if (thicknessMm is null || widthMm is null || densityGramsPerCubicCm is null)
        {
            return UnitConversionResultV1<decimal>.NoSource("A coil conversion needs thickness, width and density.");
        }

        if (thicknessMm <= 0 || widthMm <= 0 || densityGramsPerCubicCm <= 0)
        {
            return UnitConversionResultV1<decimal>.Refused("Coil conversion inputs must be positive.");
        }

        return UnitConversionResultV1<decimal>.Computed(
            thicknessMm.Value * widthMm.Value * MillimetresPerMetre * densityGramsPerCubicCm.Value * CubicMillimetreGramPerCubicCentimetreToKilogram);
    }

    /// <summary>Units to kg at full precision (no intermediate rounding).</summary>
    public static UnitConversionResultV1<decimal> UnitsToKg(decimal units, decimal? kgPerUnit)
    {
        if (kgPerUnit is null)
        {
            return UnitConversionResultV1<decimal>.NoSource("No kg-per-unit coefficient exists.");
        }

        if (units < 0 || kgPerUnit <= 0)
        {
            return UnitConversionResultV1<decimal>.Refused("Units must not be negative and the coefficient must be positive.");
        }

        return UnitConversionResultV1<decimal>.Computed(units * kgPerUnit.Value);
    }

    /// <summary>kg to whole units, rounded down (Sinan 2026-11-11); the remainder stays visible to the caller.</summary>
    public static UnitConversionResultV1<long> KgToWholeUnits(decimal kg, decimal? kgPerUnit)
    {
        if (kgPerUnit is null)
        {
            return UnitConversionResultV1<long>.NoSource("No kg-per-unit coefficient exists.");
        }

        if (kg < 0 || kgPerUnit <= 0)
        {
            return UnitConversionResultV1<long>.Refused("Kg must not be negative and the coefficient must be positive.");
        }

        return UnitConversionResultV1<long>.Computed((long)decimal.Floor(kg / kgPerUnit.Value));
    }

    /// <summary>Coefficient as stored/displayed: the setting's decimals, midpoint away from zero.</summary>
    public static decimal RoundCoefficient(decimal coefficient, UnitConversionSettingsV1 settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return decimal.Round(coefficient, RequireDecimals(settings.CoefficientDecimals), MidpointRounding.AwayFromZero);
    }

    /// <summary>Document kg total as displayed: the setting's decimals, midpoint away from zero.</summary>
    public static decimal RoundDocumentTotal(decimal kg, UnitConversionSettingsV1 settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return decimal.Round(kg, RequireDecimals(settings.DocumentTotalDecimals), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Weighbridge vs calculated kg: within tolerance when |weighed - calculated| / calculated * 100 is at most the
    /// setting (inclusive). A non-positive calculated value or tolerance below zero is refused, not passed.
    /// </summary>
    public static UnitConversionResultV1<bool> WithinWeighbridgeTolerance(decimal weighedKg, decimal calculatedKg, UnitConversionSettingsV1 settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (weighedKg < 0 || calculatedKg <= 0 || settings.WeighbridgeTolerancePercent < 0)
        {
            return UnitConversionResultV1<bool>.Refused("Weighed kg must not be negative, calculated kg must be positive and the tolerance must not be negative.");
        }

        var deviationPercent = decimal.Abs(weighedKg - calculatedKg) / calculatedKg * 100m;
        return UnitConversionResultV1<bool>.Computed(deviationPercent <= settings.WeighbridgeTolerancePercent);
    }

    private static int RequireDecimals(int decimals)
    {
        if (decimals is < 0 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(decimals), "Rounding decimals must be between 0 and 12.");
        }

        return decimals;
    }
}
