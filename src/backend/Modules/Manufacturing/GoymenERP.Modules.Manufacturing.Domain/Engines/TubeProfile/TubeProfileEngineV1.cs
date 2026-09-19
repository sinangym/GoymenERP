namespace GoymenERP.Modules.Manufacturing.Domain.Engines.TubeProfile;

/// <summary>TUB-001…003 inputs: packages, pieces per package, the piece length and the line speed.</summary>
public sealed record TubeRunInputV1(
    int? PackageCount,
    int? PiecesPerPackage,
    decimal? PieceLengthMm,
    decimal? LineSpeedMetresPerMinute);

/// <summary>TUB-001…003, unrounded.</summary>
public sealed record TubeRunV1(string FormulaVersion, long TotalPieces, decimal TotalMetres, decimal RunMinutes);

/// <summary>TUB-004 inputs: the pieces (TUB-001) and the unit mass from the product / package master.</summary>
public sealed record TubeMassInputV1(long? TotalPieces, decimal? UnitKg);

public sealed record TubeMassV1(string FormulaVersion, decimal TotalKg);

/// <summary>
/// The tube / profile engine, version 1: master v3 §28.6 (TUB-001…004) exactly, pure and deterministic,
/// decimal, on the MFG-6 core (DEC-PRDCT-001 order: the third engine).
/// <list type="bullet">
/// <item><b>Two calls.</b> TUB-001…003 (<see cref="Run"/>) need no unit mass; TUB-004 (<see cref="Mass"/>) needs
/// the unit kg, which comes from the product / package master (§28.6) — until Product supplies it, it is
/// <c>Unavailable(unitKg)</c>, never a geometric estimate (the master puts the estimate at a lower priority
/// and this engine does not choose it), and never 0. Keeping the calls apart means a missing unit mass never
/// hides the length and time.</item>
/// <item><b>TUB-003 is the running time only.</b> Set-up, die change, planned stops and cleaning are not part
/// of it (§28.6: the capacity calendar adds them).</item>
/// <item>The line speed is an input (the registry's per-work-centre value, OPEN-007), never built in.</item>
/// </list>
/// </summary>
public static class TubeProfileEngineV1
{
    public const string FormulaVersion = "GDC-TUB/1 (master v3 §28.6)";

    private const decimal MillimetresPerMetre = 1000m;

    public static EngineResult<TubeRunV1> Run(TubeRunInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = new EngineInputs();
        var packages = inputs.Count(input.PackageCount, "packageCount");
        var perPackage = inputs.Count(input.PiecesPerPackage, "piecesPerPackage");
        var length = inputs.Measure(input.PieceLengthMm, "pieceLengthMm");
        var speed = inputs.Positive(input.LineSpeedMetresPerMinute, "lineSpeedMetresPerMinute");
        if (!inputs.IsComplete)
        {
            return inputs.Incomplete<TubeRunV1>();
        }

        var pieces = (long)packages * perPackage; // TUB-001, exact
        var metres = pieces * length / MillimetresPerMetre; // TUB-002
        return EngineResult<TubeRunV1>.Computed(new TubeRunV1(FormulaVersion, pieces, metres, metres / speed)); // TUB-003
    }

    public static EngineResult<TubeMassV1> Mass(TubeMassInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = new EngineInputs();
        var unitKg = inputs.Measure(input.UnitKg, "unitKg");
        if (input.TotalPieces is not { } pieces)
        {
            inputs.Count(null, "totalPieces");
            return inputs.Incomplete<TubeMassV1>();
        }

        if (pieces <= 0)
        {
            inputs.Refuse(EngineCodes.CountNotPositive, "totalPieces");
        }

        return inputs.IsComplete
            ? EngineResult<TubeMassV1>.Computed(new TubeMassV1(FormulaVersion, unitKg * pieces)) // TUB-004
            : inputs.Incomplete<TubeMassV1>();
    }
}
