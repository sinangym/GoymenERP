namespace GoymenERP.Modules.Manufacturing.Domain.Engines.CutToLength;

/// <summary>One cut-to-length line: a target length and how many pieces of it.</summary>
public sealed record CutLineInputV1(decimal? TargetLengthMm, int? Count);

/// <summary>CUT-001…006 inputs: the coil's mass, thickness and width, the density and the lines.</summary>
public sealed record CutPlanInputV1(
    decimal? InputKg,
    decimal? ThicknessMm,
    decimal? WidthMm,
    decimal? DensityKgPerCubicMetre,
    IReadOnlyList<CutLineInputV1>? Lines);

public sealed record CutLineResultV1(decimal TargetLengthMm, int Count, decimal UnitKg, decimal PlanKg, decimal OutputMetres);

/// <summary>CUT-001…006, unrounded.</summary>
public sealed record CutPlanV1(
    string FormulaVersion,
    IReadOnlyList<CutLineResultV1> Lines,
    decimal InputMetres,
    decimal PlanKg,
    decimal PlanMetres,
    decimal RemainingKg,
    decimal RemainingMetres);

/// <summary>CUT-007 inputs: a raw (sheet) length, a target length and the kerf.</summary>
public sealed record CutsPerRawLengthInputV1(decimal? RawLengthMm, decimal? TargetLengthMm, decimal? KerfMm);

public sealed record CutsPerRawLengthV1(string FormulaVersion, int Pieces);

/// <summary>
/// CUT-008 inputs. The pieces (KesimAdedi) and the cuts (KesimSayısı) are both STATED by the caller:
/// §28.3 does not define how many cuts N pieces take (N or N−1), so this engine never derives it.
/// </summary>
public sealed record LengthScrapInputV1(
    decimal? RawLengthMm,
    decimal? TargetLengthMm,
    int? Pieces,
    decimal? KerfMm,
    int? Cuts);

public sealed record LengthScrapV1(string FormulaVersion, decimal ScrapMm);

/// <summary>
/// The cut-to-length engine, version 1: master v3 §28.3 (CUT-001…008) exactly, pure and deterministic,
/// decimal, on the MFG-6 core (DEC-PRDCT-001 order: the fifth engine).
/// <list type="bullet">
/// <item><b>CUT-001</b> is §28.2 MAT-002, <c>t × en × boy × ρ[kg/dm³] / 1 000 000</c>, with ρ the registry's
/// one density in kg/m³ (shared with WND / CR): <c>t × W × L × ρ[kg/m³] / 10⁹</c>, algebraically identical.
/// CUT-004 likewise is <c>HamKg × 10⁶ / (t × W × ρ[kg/m³])</c>.</item>
/// <item><b>Negative remainders are refused</b> (§28.3: no normal record, an automatic block): the planned
/// kg above the input kg (<c>engine_plan_exceeds_input_mass</c>; the same condition as planned metres above
/// the coil's metres, checked once, exactly) and the cuts above the raw length
/// (<c>engine_cuts_exceed_raw_length</c>). The authorized exception is Business's and is not encoded. A zero
/// remainder is a value.</item>
/// <item><b>CUT-007/008 are the master's target behaviour</b> with a MANDATORY, versioned kerf (KesimPayı,
/// by material / machine / saw profile, OPEN-007): a missing kerf is <c>Unavailable(kerfMm)</c>; a stated 0
/// is a value. The master gives no golden with kerf ("yeni sürümde ayrıca hesaplanır"), so none is invented.</item>
/// <item><b>CUT-008's cuts are stated</b>, never derived from the pieces (§28.3 leaves N vs N−1 open).</item>
/// <item>Each quantity multiplies first and divides once; nothing rounds (§28.1 undecided).</item>
/// </list>
/// </summary>
public static class CutToLengthEngineV1
{
    public const string FormulaVersion = "GDC-CUT/1 (master v3 §28.3)";

    private const decimal MillimetresPerMetre = 1000m;

    /// <summary>mm × mm × mm × kg/m³ → kg is × 10⁻⁹.</summary>
    private const decimal CubicMillimetresPerCubicMetre = 1_000_000_000m;

    /// <summary>mm × mm × kg/m³ → kg per metre is × 10⁻⁶; the metres are kg × 10⁶ / (t × W × ρ).</summary>
    private const decimal SquareMillimetresPerSquareMetre = 1_000_000m;

    public static EngineResult<CutPlanV1> Plan(CutPlanInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = new EngineInputs();
        var inputKg = inputs.Measure(input.InputKg, "inputKg");
        var thickness = inputs.Measure(input.ThicknessMm, "thicknessMm");
        var width = inputs.Measure(input.WidthMm, "widthMm");
        var density = inputs.Positive(input.DensityKgPerCubicMetre, "densityKgPerCubicMetre");
        if (input.Lines is not { Count: > 0 } lines)
        {
            inputs.Refuse(EngineCodes.CountNotPositive, "lines");
            return inputs.Incomplete<CutPlanV1>();
        }

        var measured = new List<(decimal Length, int Count)>();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index] ?? throw new ArgumentException("A cut line is null.", nameof(input));
            measured.Add((inputs.Measure(line.TargetLengthMm, $"lines[{index}].targetLengthMm"),
                inputs.Count(line.Count, $"lines[{index}].count")));
        }

        if (!inputs.IsComplete)
        {
            return inputs.Incomplete<CutPlanV1>();
        }

        var results = measured
            .Select(line => new CutLineResultV1(
                line.Length,
                line.Count,
                thickness * width * line.Length * density / CubicMillimetresPerCubicMetre, // CUT-001
                thickness * width * line.Length * density * line.Count / CubicMillimetresPerCubicMetre, // CUT-002
                line.Length * line.Count / MillimetresPerMetre)) // CUT-003
            .ToArray();
        // The plan kg is EXACT in decimal (every line divides a finite product by 10⁹, which terminates), so
        // the mass check is exact. The metres of the same coil are the same quantity divided by t × W × ρ, so
        // "plan metres ≤ input metres" is the same condition; it is not checked a second time, where CUT-004's
        // often non-terminating quotient could refuse an exact fit by a 10⁻²⁵ residue (the #340 lesson).
        var planKg = results.Sum(line => line.PlanKg);
        var planMetres = results.Sum(line => line.OutputMetres);
        if (planKg > inputKg)
        {
            return EngineResult<CutPlanV1>.Refused(EngineCodes.PlanExceedsInputMass, "lines");
        }

        var remainingKg = inputKg - planKg; // CUT-005, exact, ≥ 0
        var coilSection = thickness * width * density;
        return EngineResult<CutPlanV1>.Computed(new CutPlanV1(
            FormulaVersion,
            results,
            inputKg * SquareMillimetresPerSquareMetre / coilSection, // CUT-004
            planKg,
            planMetres,
            remainingKg,
            // CUT-006 is HamToplamMetre − ΣPlanMetre; evaluated as the remaining kg's metres (algebraically
            // identical, as ΣPlanMetre = ΣPlanKg × 10⁶ / (t × W × ρ)), so an exact fit leaves exactly 0 and the
            // remainder is never negative.
            remainingKg * SquareMillimetresPerSquareMetre / coilSection));
    }

    public static EngineResult<CutsPerRawLengthV1> CutsPerRawLength(CutsPerRawLengthInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = new EngineInputs();
        var raw = inputs.Measure(input.RawLengthMm, "rawLengthMm");
        var target = inputs.Measure(input.TargetLengthMm, "targetLengthMm");
        var kerf = inputs.NonNegativeMeasure(input.KerfMm, "kerfMm");
        return inputs.IsComplete
            ? EngineResult<CutsPerRawLengthV1>.Computed(new CutsPerRawLengthV1(FormulaVersion,
                (int)decimal.Floor(raw / (target + kerf)))) // CUT-007
            : inputs.Incomplete<CutsPerRawLengthV1>();
    }

    public static EngineResult<LengthScrapV1> LengthScrap(LengthScrapInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = new EngineInputs();
        var raw = inputs.Measure(input.RawLengthMm, "rawLengthMm");
        var target = inputs.Measure(input.TargetLengthMm, "targetLengthMm");
        var pieces = inputs.Count(input.Pieces, "pieces");
        var kerf = inputs.NonNegativeMeasure(input.KerfMm, "kerfMm");
        var cuts = CutCount(inputs, input.Cuts);
        if (!inputs.IsComplete)
        {
            return inputs.Incomplete<LengthScrapV1>();
        }

        var scrap = raw - target * pieces - kerf * cuts; // CUT-008, exact
        return scrap < 0m
            ? EngineResult<LengthScrapV1>.Refused(EngineCodes.CutsExceedRawLength, "pieces")
            : EngineResult<LengthScrapV1>.Computed(new LengthScrapV1(FormulaVersion, scrap));
    }

    /// <summary>The cuts may be a stated 0 (a single piece left as it came) but never negative, and never derived.</summary>
    private static int CutCount(EngineInputs inputs, int? cuts)
    {
        if (cuts is not { } stated)
        {
            inputs.NonNegative(null, "cuts");
            return 0;
        }

        if (stated < 0)
        {
            inputs.Refuse(EngineCodes.ValueNegative, "cuts");
        }

        return stated;
    }
}
