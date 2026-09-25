namespace GoymenERP.Modules.Manufacturing.Domain.Engines.ColdRolling;

/// <summary>
/// CR-001…005 inputs: the input coil's mass and width, its input and target thickness, the scrap and
/// the density. The scrap is supplied (a stated 0 is a value); a missing one is unavailable, never 0.
/// </summary>
public sealed record ColdRollingInputV1(
    decimal? InputKg,
    decimal? WidthMm,
    decimal? InputThicknessMm,
    decimal? TargetThicknessMm,
    decimal? ScrapKg,
    decimal? DensityKgPerCubicMetre);

/// <summary>CR-001…005, unrounded.</summary>
public sealed record ColdRollingV1(
    string FormulaVersion,
    decimal InputMetres,
    decimal OutputKg,
    decimal ElongationRatio,
    decimal OutputMetres,
    decimal ReductionPercent);

/// <summary>
/// The cold-rolling engine, version 1: master v3 §28.8 (CR-001…005) exactly, pure and deterministic,
/// decimal throughout, on the same core as Slitting / Winding (DEC-PRDCT-001 order: the second engine).
/// <list type="bullet">
/// <item><b>Density</b> is an input in kg/m³, the same registry parameter as SLT / WND (OPEN-007,
/// DEC-SET-001), never built in. §28.8 writes CR-001 / CR-004 with density in kg/dm³
/// (<c>HamKg × 1000 / (t × En × ρ)</c>); with ρ in kg/m³ that is <c>HamKg × 1 000 000 / (t × En × ρ)</c>,
/// algebraically identical (ρ[kg/dm³] = ρ[kg/m³] / 1000).</item>
/// <item><b>Invariants</b> (§28.8), refused with codes, never clamped: the target thickness is below the
/// input thickness; the scrap is below the input mass (equal or more is refused).</item>
/// <item><b>Evaluation order:</b> each quantity multiplies first and divides once; nothing rounds (§28.1 is
/// not decided).</item>
/// <item><b>Not in version 1</b> (the master keeps them in the process model): multi-pass rolling, the pass
/// plan, tolerance, work hardening and quality results. The width is the coil's width in and out, as §28.8
/// uses one width for both.</item>
/// </list>
/// </summary>
public static class ColdRollingEngineV1
{
    public const string FormulaVersion = "GDC-CR/1 (master v3 §28.8)";

    /// <summary>mm × mm × kg/m³ → kg per metre is × 10⁻⁶; the metres are therefore kg × 10⁶ / (t × W × ρ).</summary>
    private const decimal SquareMillimetresPerSquareMetre = 1_000_000m;

    public static EngineResult<ColdRollingV1> Roll(ColdRollingInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = new EngineInputs();
        var inputKg = inputs.Measure(input.InputKg, "inputKg");
        var width = inputs.Measure(input.WidthMm, "widthMm");
        var inputThickness = inputs.Measure(input.InputThicknessMm, "inputThicknessMm");
        var targetThickness = inputs.Measure(input.TargetThicknessMm, "targetThicknessMm");
        var scrap = inputs.NonNegativeMeasure(input.ScrapKg, "scrapKg");
        var density = inputs.Positive(input.DensityKgPerCubicMetre, "densityKgPerCubicMetre");
        if (!inputs.IsComplete)
        {
            return inputs.Incomplete<ColdRollingV1>();
        }

        if (targetThickness >= inputThickness)
        {
            return EngineResult<ColdRollingV1>.Refused(EngineCodes.TargetThicknessNotBelowInput, "targetThicknessMm");
        }

        if (scrap >= inputKg)
        {
            return EngineResult<ColdRollingV1>.Refused(EngineCodes.ScrapNotBelowInputMass, "scrapKg");
        }

        var outputKg = inputKg - scrap; // CR-002, exact
        return EngineResult<ColdRollingV1>.Computed(new ColdRollingV1(
            FormulaVersion,
            inputKg * SquareMillimetresPerSquareMetre / (inputThickness * width * density), // CR-001
            outputKg,
            inputThickness / targetThickness, // CR-003
            outputKg * SquareMillimetresPerSquareMetre / (targetThickness * width * density), // CR-004
            (inputThickness - targetThickness) * 100m / inputThickness)); // CR-005
    }
}
