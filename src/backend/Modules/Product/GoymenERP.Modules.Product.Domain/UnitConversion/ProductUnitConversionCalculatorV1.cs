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
/// Whether a deviation exactly equal to the weighbridge tolerance passes. Not decided by Sinan
/// (ASSUMPTION-U08-TOLERANCE-BOUNDARY): a company setting, never a code default.
/// </summary>
public enum WeighbridgeToleranceBoundaryV1
{
    /// <summary>deviation &lt;= tolerance passes.</summary>
    Inclusive = 1,

    /// <summary>deviation &lt; tolerance passes; a deviation equal to the tolerance fails.</summary>
    Exclusive = 2,
}

/// <summary>
/// Which kg the deviation percent is measured against. Not decided by Sinan (ASSUMPTION-U08-TOLERANCE-BOUNDARY):
/// a company setting, never a code default.
/// </summary>
public enum WeighbridgeToleranceBaseV1
{
    /// <summary>|weighed - calculated| / calculated.</summary>
    Calculated = 1,

    /// <summary>|weighed - calculated| / weighed.</summary>
    Weighed = 2,
}

/// <summary>
/// A density with an explicit unit. Stored in g/cm3 (Sinan's example 7.85, BS-9); kg/m3 input is converted
/// (1 g/cm3 = 1000 kg/m3) so a settings value in either unit cannot be read at the wrong scale.
/// </summary>
public readonly record struct DensityV1
{
    private const decimal KilogramsPerCubicMetrePerGramPerCubicCentimetre = 1000m;

    private DensityV1(decimal gramsPerCubicCentimetre) => GramsPerCubicCentimetre = gramsPerCubicCentimetre;

    public decimal GramsPerCubicCentimetre { get; }

    public static DensityV1 FromGramsPerCubicCentimetre(decimal value) =>
        value > 0 ? new DensityV1(value) : throw new ArgumentOutOfRangeException(nameof(value), "Density must be positive.");

    public static DensityV1 FromKilogramsPerCubicMetre(decimal value) =>
        FromGramsPerCubicCentimetre(value / KilogramsPerCubicMetrePerGramPerCubicCentimetre);
}

/// <summary>
/// Company settings for conversion rounding and the weighbridge check. Every value comes from Settings
/// (Sinan 2026-11-11, #66 6806641583: coefficient 3 decimals, document kg total 2 decimals, tolerance 1 percent,
/// all adjustable; BS-13). The display rounding mode, the tolerance boundary and the tolerance base are settings
/// too (the last two are ASSUMPTION-U08-TOLERANCE-BOUNDARY, awaiting Sinan). kg to unit count is always floor
/// (BS-13). The calculator has no built-in default, so a missing setting cannot be silently replaced. A later
/// settings slice scopes these per CompanyId.
/// </summary>
public sealed record UnitConversionSettingsV1(
    int CoefficientDecimals,
    int DocumentTotalDecimals,
    decimal WeighbridgeTolerancePercent,
    MidpointRounding DisplayRounding,
    WeighbridgeToleranceBoundaryV1 ToleranceBoundary,
    WeighbridgeToleranceBaseV1 ToleranceBase);

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

    private const decimal PercentScale = 100m;

    // A technical guard, not business policy: settings ask for 2-3 decimals (BS-13); 12 leaves room while keeping
    // results far inside decimal's 28-29 significant digits for kg values, so rounding never silently saturates.
    private const int MaxRoundingDecimals = 12;

    /// <summary>F-A: kg per piece of a cut sheet from thickness, width, length (mm) and density.</summary>
    public static UnitConversionResultV1<decimal> SheetKgPerPiece(decimal? thicknessMm, decimal? widthMm, decimal? lengthMm, DensityV1? density)
    {
        if (thicknessMm is null || widthMm is null || lengthMm is null || density is null)
        {
            return UnitConversionResultV1<decimal>.NoSource("A sheet conversion needs thickness, width, length and density.");
        }

        if (thicknessMm <= 0 || widthMm <= 0 || lengthMm <= 0)
        {
            return UnitConversionResultV1<decimal>.Refused("Sheet conversion inputs must be positive.");
        }

        return UnitConversionResultV1<decimal>.Computed(
            thicknessMm.Value * widthMm.Value * lengthMm.Value * density.Value.GramsPerCubicCentimetre * CubicMillimetreGramPerCubicCentimetreToKilogram);
    }

    /// <summary>F-B: kg per metre of coil or slit coil from thickness, width (mm) and density.</summary>
    public static UnitConversionResultV1<decimal> CoilKgPerMetre(decimal? thicknessMm, decimal? widthMm, DensityV1? density)
    {
        if (thicknessMm is null || widthMm is null || density is null)
        {
            return UnitConversionResultV1<decimal>.NoSource("A coil conversion needs thickness, width and density.");
        }

        if (thicknessMm <= 0 || widthMm <= 0)
        {
            return UnitConversionResultV1<decimal>.Refused("Coil conversion inputs must be positive.");
        }

        return UnitConversionResultV1<decimal>.Computed(
            thicknessMm.Value * widthMm.Value * MillimetresPerMetre * density.Value.GramsPerCubicCentimetre * CubicMillimetreGramPerCubicCentimetreToKilogram);
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

    /// <summary>
    /// kg to whole units, rounded down (BS-13, #66 6806641583); the remainder stays visible to the caller.
    /// <paramref name="kgPerUnit"/> is the full-precision coefficient, not the 3-decimal stored/displayed one:
    /// intermediate steps are never rounded (BS-13), so callers pass the unrounded value.
    /// </summary>
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

    /// <summary>Coefficient as stored/displayed: the setting's decimals and midpoint rounding mode.</summary>
    public static decimal RoundCoefficient(decimal coefficient, UnitConversionSettingsV1 settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return decimal.Round(coefficient, RequireDecimals(settings.CoefficientDecimals), RequireMode(settings.DisplayRounding));
    }

    /// <summary>Document kg total as displayed: the setting's decimals and midpoint rounding mode.</summary>
    public static decimal RoundDocumentTotal(decimal kg, UnitConversionSettingsV1 settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return decimal.Round(kg, RequireDecimals(settings.DocumentTotalDecimals), RequireMode(settings.DisplayRounding));
    }

    /// <summary>
    /// Weighbridge vs calculated kg: deviation percent = |weighed - calculated| / base * 100, where the base and
    /// whether the tolerance itself passes come from the settings (ASSUMPTION-U08-TOLERANCE-BOUNDARY). A
    /// non-positive base, negative kg, negative tolerance or unknown setting is refused, not passed.
    /// </summary>
    public static UnitConversionResultV1<bool> WithinWeighbridgeTolerance(decimal weighedKg, decimal calculatedKg, UnitConversionSettingsV1 settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (weighedKg < 0 || calculatedKg <= 0 || settings.WeighbridgeTolerancePercent < 0)
        {
            return UnitConversionResultV1<bool>.Refused("Weighed kg must not be negative, calculated kg must be positive and the tolerance must not be negative.");
        }

        var baseKg = settings.ToleranceBase switch
        {
            WeighbridgeToleranceBaseV1.Calculated => calculatedKg,
            WeighbridgeToleranceBaseV1.Weighed => weighedKg,
            _ => throw new ArgumentOutOfRangeException(nameof(settings), "Unknown weighbridge tolerance base."),
        };

        if (baseKg <= 0)
        {
            return UnitConversionResultV1<bool>.Refused("The kg the tolerance is measured against must be positive.");
        }

        var deviationPercent = decimal.Abs(weighedKg - calculatedKg) / baseKg * PercentScale;
        return UnitConversionResultV1<bool>.Computed(settings.ToleranceBoundary switch
        {
            WeighbridgeToleranceBoundaryV1.Inclusive => deviationPercent <= settings.WeighbridgeTolerancePercent,
            WeighbridgeToleranceBoundaryV1.Exclusive => deviationPercent < settings.WeighbridgeTolerancePercent,
            _ => throw new ArgumentOutOfRangeException(nameof(settings), "Unknown weighbridge tolerance boundary."),
        });
    }

    private static MidpointRounding RequireMode(MidpointRounding mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "Unknown rounding mode.");
        }

        return mode;
    }

    private static int RequireDecimals(int decimals)
    {
        if (decimals < 0 || decimals > MaxRoundingDecimals)
        {
            throw new ArgumentOutOfRangeException(nameof(decimals), $"Rounding decimals must be between 0 and {MaxRoundingDecimals}.");
        }

        return decimals;
    }
}
