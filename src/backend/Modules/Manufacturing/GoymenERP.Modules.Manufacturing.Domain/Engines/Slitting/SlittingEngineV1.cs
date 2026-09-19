namespace GoymenERP.Modules.Manufacturing.Domain.Engines.Slitting;

/// <summary>One slit line: a band width and how many bands of it (SLT-002 inputs).</summary>
public sealed record SlitLineInputV1(decimal? BandWidthMm, int? Count);

/// <summary>SLT-001…007 inputs: the parent coil's mass, its net usable width and the slit lines.</summary>
public sealed record SlittingPlanInputV1(decimal? ParentKg, decimal? NetUsableWidthMm, IReadOnlyList<SlitLineInputV1>? Lines);

public sealed record SlitLineResultV1(decimal BandWidthMm, int Count, decimal UsedWidthMm, decimal Kg);

/// <summary>SLT-001…007, unrounded.</summary>
public sealed record SlittingPlanV1(
    string FormulaVersion,
    decimal KgPerMillimetreWidth,
    IReadOnlyList<SlitLineResultV1> Lines,
    decimal TotalUsedWidthMm,
    decimal RemainingWidthMm,
    decimal PlanKg,
    decimal RemainingKg);

/// <summary>SLT-008/009 inputs: the raw width, one band width and the bands asked for.</summary>
public sealed record BandFitInputV1(decimal? RawWidthMm, decimal? BandWidthMm, int? Count);

/// <summary>SLT-008 theoretical band count and SLT-009 width scrap (knife / edge loss is separate).</summary>
public sealed record BandFitV1(string FormulaVersion, int TheoreticalCount, decimal WidthScrapMm);

/// <summary>
/// SLT-010 inputs. The line time is the PARENT coil's walking metres over the line speed, never the
/// sum of the output bands' metres (master v3 §28.4 invariant). The walking metres come from the
/// parent's mass and its kg/m (density × thickness × width).
/// </summary>
public sealed record LineTimeInputV1(
    decimal? ParentKg,
    decimal? ThicknessMm,
    decimal? ParentWidthMm,
    decimal? DensityKgPerCubicMetre,
    decimal? LineSpeedMetresPerMinute);

public sealed record LineTimeV1(string FormulaVersion, decimal KgPerMetre, decimal WalkingMetres, decimal Minutes);

/// <summary>
/// The slitting engine, version 1: master v3 §28.4 (SLT-001…010) exactly, pure and deterministic.
/// Density and line speed are inputs, never built in: their values are the Settings / Policy
/// Registry's (DEC-SET-001, OPEN-007) and are snapshotted by an approved plan (DEC-PRDCT-008), so a
/// stored plan is never re-computed under a newer value.
/// </summary>
public static class SlittingEngineV1
{
    /// <summary>The formula version a result carries; a changed formula is a new version, never an edit (DEC-PRDCT-004).</summary>
    public const string FormulaVersion = "GDC-SLT/1 (master v3 §28.4)";

    private const decimal MillimetresPerMetre = 1000m;

    public static EngineResult<SlittingPlanV1> Plan(SlittingPlanInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = new EngineInputs();
        var parentKg = inputs.Measure(input.ParentKg, "parentKg");
        var netWidth = inputs.Measure(input.NetUsableWidthMm, "netUsableWidthMm");
        if (input.Lines is not { Count: > 0 } lines)
        {
            inputs.Refuse(EngineCodes.CountNotPositive, "lines");
            return inputs.Incomplete<SlittingPlanV1>();
        }

        var widths = new List<(decimal Width, int Count)>();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index] ?? throw new ArgumentException("A slit line is null.", nameof(input));
            widths.Add((inputs.Measure(line.BandWidthMm, $"lines[{index}].bandWidthMm"),
                inputs.Count(line.Count, $"lines[{index}].count")));
        }

        if (!inputs.IsComplete)
        {
            return inputs.Incomplete<SlittingPlanV1>();
        }

        // SLT-001 per millimetre of NET usable width; SLT-002/003 per line.
        var kgPerMillimetre = parentKg / netWidth;
        var results = widths
            .Select(line => new SlitLineResultV1(line.Width, line.Count, line.Width * line.Count,
                kgPerMillimetre * line.Width * line.Count))
            .ToArray();
        var totalUsed = results.Sum(line => line.UsedWidthMm); // SLT-004
        if (totalUsed > netWidth)
        {
            return EngineResult<SlittingPlanV1>.Refused(EngineCodes.UsedWidthExceedsNetWidth, "lines");
        }

        var planKg = results.Sum(line => line.Kg); // SLT-006
        return EngineResult<SlittingPlanV1>.Computed(new SlittingPlanV1(
            FormulaVersion,
            kgPerMillimetre,
            results,
            totalUsed,
            netWidth - totalUsed, // SLT-005
            planKg,
            parentKg - planKg)); // SLT-007
    }

    public static EngineResult<BandFitV1> BandFit(BandFitInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = new EngineInputs();
        var rawWidth = inputs.Measure(input.RawWidthMm, "rawWidthMm");
        var bandWidth = inputs.Measure(input.BandWidthMm, "bandWidthMm");
        var count = inputs.Count(input.Count, "count");
        if (!inputs.IsComplete)
        {
            return inputs.Incomplete<BandFitV1>();
        }

        var scrap = rawWidth - bandWidth * count; // SLT-009
        if (scrap < 0m)
        {
            return EngineResult<BandFitV1>.Refused(EngineCodes.BandsExceedRawWidth, "count");
        }

        // SLT-008: floor of a positive quotient.
        return EngineResult<BandFitV1>.Computed(new BandFitV1(FormulaVersion, (int)decimal.Floor(rawWidth / bandWidth), scrap));
    }

    public static EngineResult<LineTimeV1> LineTime(LineTimeInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = new EngineInputs();
        var parentKg = inputs.Measure(input.ParentKg, "parentKg");
        var thickness = inputs.Measure(input.ThicknessMm, "thicknessMm");
        var width = inputs.Measure(input.ParentWidthMm, "parentWidthMm");
        var density = inputs.Positive(input.DensityKgPerCubicMetre, "densityKgPerCubicMetre");
        var speed = inputs.Positive(input.LineSpeedMetresPerMinute, "lineSpeedMetresPerMinute");
        if (!inputs.IsComplete)
        {
            return inputs.Incomplete<LineTimeV1>();
        }

        var kgPerMetre = density * (thickness / MillimetresPerMetre) * (width / MillimetresPerMetre);
        var walkingMetres = parentKg / kgPerMetre;
        return EngineResult<LineTimeV1>.Computed(new LineTimeV1(FormulaVersion, kgPerMetre, walkingMetres, walkingMetres / speed)); // SLT-010
    }
}
