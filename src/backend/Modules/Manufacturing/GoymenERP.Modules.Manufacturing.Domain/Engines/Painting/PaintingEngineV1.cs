namespace GoymenERP.Modules.Manufacturing.Domain.Engines.Painting;

/// <summary>PNT-001…004 inputs: packages, pieces per package, the profile length, the profiles on the line at once and the line speed.</summary>
public sealed record PaintRunInputV1(
    int? PackageCount,
    int? PiecesPerPackage,
    decimal? ProfileLengthMm,
    int? SimultaneousProfiles,
    decimal? LineSpeedMetresPerMinute);

/// <summary>PNT-001…004, unrounded.</summary>
public sealed record PaintRunV1(
    string FormulaVersion,
    long TotalPieces,
    decimal ProfileMetres,
    decimal LinePassMetres,
    decimal Minutes);

/// <summary>PNT-005 inputs: the profile's perimeter, its length and the pieces.</summary>
public sealed record PaintAreaInputV1(decimal? PerimeterMm, decimal? ProfileLengthMm, long? Pieces);

public sealed record PaintAreaV1(string FormulaVersion, decimal AreaSquareMetres);

/// <summary>PNT-006 inputs: the area, the dry film thickness, the solids ratio (a fraction) and the scrap factor.</summary>
public sealed record PaintLitresInputV1(
    decimal? AreaSquareMetres,
    decimal? FilmMicrons,
    decimal? SolidsRatio,
    decimal? ScrapFactor);

public sealed record PaintLitresV1(string FormulaVersion, decimal TheoreticalLitres);

/// <summary>
/// The painting engine, version 1: master v3 §28.7 (PNT-001…006) exactly, pure and deterministic, decimal,
/// on the MFG-6 core (DEC-PRDCT-001 order: the fourth engine).
/// <list type="bullet">
/// <item><b>Inputs, never hard-coded</b> (§28.7): the simultaneous profile count, the solids ratio and the
/// scrap factor; also the line speed and the film thickness. Their values are the registry's / the plan's
/// (OPEN-007); a missing one is <c>Unavailable</c> and named.</item>
/// <item><b>Three calls</b>, so one missing input does not hide the others: <see cref="Run"/> (PNT-001…004,
/// count, metres and time), <see cref="Area"/> (PNT-005) and <see cref="Paint"/> (PNT-006).</item>
/// <item><b>Solids ratio</b> is a fraction: above 1 is refused (<c>engine_ratio_above_one</c>), never read as a
/// percentage.</item>
/// <item><b>Theoretical litres only</b> (§28.7): not the cost, not the actual consumption. Colour change,
/// transfer efficiency, paint density, coats, thinner, gun / line loss and rework are separate parameters or
/// realizations — not in version 1.</item>
/// <item><b>Evaluation order:</b> each quantity multiplies first and divides once; PNT-006 is
/// <c>Alan × Mikron × FireKatsayısı / (KatıMaddeOranı × 1000)</c>, the master's formula reordered
/// (algebraically identical). Nothing rounds (§28.1 undecided).</item>
/// </list>
/// </summary>
public static class PaintingEngineV1
{
    public const string FormulaVersion = "GDC-PNT/1 (master v3 §28.7)";

    private const decimal MillimetresPerMetre = 1000m;
    private const decimal SquareMillimetresPerSquareMetre = 1_000_000m;
    /// <summary>1 m² × 1 µm = 10⁻⁶ m³ = 1 mL, so m²·µm / 1000 = litres.</summary>
    private const decimal SquareMetreMicronsPerLitre = 1000m;

    public static EngineResult<PaintRunV1> Run(PaintRunInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = new EngineInputs();
        var packages = inputs.Count(input.PackageCount, "packageCount");
        var perPackage = inputs.Count(input.PiecesPerPackage, "piecesPerPackage");
        var length = inputs.Measure(input.ProfileLengthMm, "profileLengthMm");
        var simultaneous = inputs.Count(input.SimultaneousProfiles, "simultaneousProfiles");
        var speed = inputs.Positive(input.LineSpeedMetresPerMinute, "lineSpeedMetresPerMinute");
        if (!inputs.IsComplete)
        {
            return inputs.Incomplete<PaintRunV1>();
        }

        var pieces = (long)packages * perPackage; // PNT-001, exact
        var profileMetres = pieces * length / MillimetresPerMetre; // PNT-002
        var passMetres = pieces * length / (MillimetresPerMetre * simultaneous); // PNT-003, one division
        return EngineResult<PaintRunV1>.Computed(new PaintRunV1(FormulaVersion, pieces, profileMetres, passMetres,
            pieces * length / (MillimetresPerMetre * simultaneous * speed))); // PNT-004, one division
    }

    public static EngineResult<PaintAreaV1> Area(PaintAreaInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = new EngineInputs();
        var perimeter = inputs.Measure(input.PerimeterMm, "perimeterMm");
        var length = inputs.Measure(input.ProfileLengthMm, "profileLengthMm");
        var pieces = Pieces(inputs, input.Pieces);
        return inputs.IsComplete
            ? EngineResult<PaintAreaV1>.Computed(new PaintAreaV1(FormulaVersion,
                perimeter * length * pieces / SquareMillimetresPerSquareMetre)) // PNT-005
            : inputs.Incomplete<PaintAreaV1>();
    }

    public static EngineResult<PaintLitresV1> Paint(PaintLitresInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = new EngineInputs();
        // The area is PNT-005's computed value: positive, its precision the engine's, not an Ö2 nominal.
        var area = inputs.Positive(input.AreaSquareMetres, "areaSquareMetres");
        var microns = inputs.Positive(input.FilmMicrons, "filmMicrons");
        var solids = inputs.Positive(input.SolidsRatio, "solidsRatio");
        var scrap = inputs.Positive(input.ScrapFactor, "scrapFactor");
        if (input.SolidsRatio is > 1m)
        {
            inputs.Refuse(EngineCodes.RatioAboveOne, "solidsRatio");
        }

        return inputs.IsComplete
            ? EngineResult<PaintLitresV1>.Computed(new PaintLitresV1(FormulaVersion,
                area * microns * scrap / (solids * SquareMetreMicronsPerLitre))) // PNT-006
            : inputs.Incomplete<PaintLitresV1>();
    }

    private static long Pieces(EngineInputs inputs, long? pieces)
    {
        if (pieces is not { } supplied)
        {
            inputs.Count(null, "pieces");
            return 0;
        }

        if (supplied <= 0)
        {
            inputs.Refuse(EngineCodes.CountNotPositive, "pieces");
        }

        return supplied;
    }
}
