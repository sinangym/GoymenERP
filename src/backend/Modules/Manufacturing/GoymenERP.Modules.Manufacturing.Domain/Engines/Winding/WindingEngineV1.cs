namespace GoymenERP.Modules.Manufacturing.Domain.Engines.Winding;

/// <summary>WND-001…004 inputs: one band wound to a target mass on a core of the given inner diameter.</summary>
public sealed record WindingTargetInputV1(
    decimal? DensityKgPerCubicMetre,
    decimal? ThicknessMm,
    decimal? BandWidthMm,
    decimal? TargetKg,
    decimal? InnerDiameterMm);

/// <summary>WND-001…004, unrounded.</summary>
public sealed record WindingTargetV1(
    string FormulaVersion,
    decimal KgPerMetre,
    decimal RequiredMetres,
    decimal OuterDiameterMm,
    decimal WindingHeightMm);

/// <summary>WND-005…007 inputs: the line's maximum outer diameter and a number of coils.</summary>
public sealed record WindingCapacityInputV1(
    decimal? DensityKgPerCubicMetre,
    decimal? ThicknessMm,
    decimal? BandWidthMm,
    decimal? InnerDiameterMm,
    decimal? MaximumOuterDiameterMm,
    int? CoilCount);

/// <summary>WND-005…007, unrounded.</summary>
public sealed record WindingCapacityV1(
    string FormulaVersion,
    decimal KgPerMetre,
    decimal MaximumMetres,
    decimal MaximumSingleCoilKg,
    decimal GroupMaximumKg);

/// <summary>
/// WND-008/009 inputs. The metre tolerance is a policy value (OPEN-004 / OPEN-005, Ö7: no number is
/// approved): it is an input, and a missing one makes the check unavailable, never zero.
/// </summary>
public sealed record WindingFitInputV1(
    decimal? WalkingMetres,
    decimal? ToleranceMetres,
    decimal? RequiredMetres,
    decimal? OuterDiameterMm,
    decimal? MaximumOuterDiameterMm);

public sealed record WindingFitV1(string FormulaVersion, bool MetresSufficient, bool DiameterFits);

/// <summary>
/// The coil winding engine, version 1: master v3 §28.5 (WND-001…009) exactly, pure and
/// deterministic, decimal throughout (π and the square root included). Density is an input, never
/// built in (OPEN-007); a stored plan keeps the values it was approved with (DEC-PRDCT-008).
/// WND-010 (coil break-down into target-mass coils plus a remainder "above tolerance") is not part
/// of this version: its tolerance rule is not stated precisely enough to encode without choosing it.
/// </summary>
public static class WindingEngineV1
{
    public const string FormulaVersion = "GDC-WND/1 (master v3 §28.5)";

    private const decimal MillimetresPerMetre = 1000m;

    public static EngineResult<WindingTargetV1> Target(WindingTargetInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = new EngineInputs();
        var density = inputs.Positive(input.DensityKgPerCubicMetre, "densityKgPerCubicMetre");
        var thickness = inputs.Measure(input.ThicknessMm, "thicknessMm");
        var width = inputs.Measure(input.BandWidthMm, "bandWidthMm");
        var targetKg = inputs.Measure(input.TargetKg, "targetKg");
        var innerDiameter = inputs.Measure(input.InnerDiameterMm, "innerDiameterMm");
        if (!inputs.IsComplete)
        {
            return inputs.Incomplete<WindingTargetV1>();
        }

        var kgPerMetre = KgPerMetre(density, thickness, width); // WND-001
        var requiredMetres = targetKg / kgPerMetre; // WND-002
        // WND-003: sqrt(ID² + 4 × t(mm) × metres × 1000 / π), in millimetres.
        var outerDiameter = EngineMath.Sqrt(
            innerDiameter * innerDiameter + 4m * thickness * requiredMetres * MillimetresPerMetre / EngineMath.Pi);
        return EngineResult<WindingTargetV1>.Computed(new WindingTargetV1(
            FormulaVersion,
            kgPerMetre,
            requiredMetres,
            outerDiameter,
            (outerDiameter - innerDiameter) / 2m)); // WND-004
    }

    public static EngineResult<WindingCapacityV1> Capacity(WindingCapacityInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = new EngineInputs();
        var density = inputs.Positive(input.DensityKgPerCubicMetre, "densityKgPerCubicMetre");
        var thickness = inputs.Measure(input.ThicknessMm, "thicknessMm");
        var width = inputs.Measure(input.BandWidthMm, "bandWidthMm");
        var innerDiameter = inputs.Measure(input.InnerDiameterMm, "innerDiameterMm");
        var maximumDiameter = inputs.Measure(input.MaximumOuterDiameterMm, "maximumOuterDiameterMm");
        var coils = inputs.Count(input.CoilCount, "coilCount");
        if (!inputs.IsComplete)
        {
            return inputs.Incomplete<WindingCapacityV1>();
        }

        if (innerDiameter >= maximumDiameter)
        {
            return EngineResult<WindingCapacityV1>.Refused(EngineCodes.InnerDiameterNotBelowMaximum, "innerDiameterMm");
        }

        var kgPerMetre = KgPerMetre(density, thickness, width);
        // WND-005: ((MaxOD² − ID²) × π) / (4 × t × 1000), in metres.
        var maximumMetres = (maximumDiameter * maximumDiameter - innerDiameter * innerDiameter) * EngineMath.Pi
            / (4m * thickness * MillimetresPerMetre);
        var maximumSingleCoilKg = maximumMetres * kgPerMetre; // WND-006
        return EngineResult<WindingCapacityV1>.Computed(new WindingCapacityV1(
            FormulaVersion, kgPerMetre, maximumMetres, maximumSingleCoilKg, maximumSingleCoilKg * coils)); // WND-007
    }

    public static EngineResult<WindingFitV1> Fit(WindingFitInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = new EngineInputs();
        var walking = inputs.Positive(input.WalkingMetres, "walkingMetres");
        // Zero is a stated "no tolerance"; missing is unavailable, never assumed zero.
        var tolerance = inputs.NonNegative(input.ToleranceMetres, "toleranceMetres");
        var required = inputs.Positive(input.RequiredMetres, "requiredMetres");
        var outerDiameter = inputs.Measure(input.OuterDiameterMm, "outerDiameterMm");
        var maximumDiameter = inputs.Measure(input.MaximumOuterDiameterMm, "maximumOuterDiameterMm");
        if (!inputs.IsComplete)
        {
            return inputs.Incomplete<WindingFitV1>();
        }

        return EngineResult<WindingFitV1>.Computed(new WindingFitV1(
            FormulaVersion,
            walking + tolerance >= required, // WND-008
            outerDiameter <= maximumDiameter)); // WND-009
    }

    private static decimal KgPerMetre(decimal density, decimal thicknessMm, decimal widthMm) =>
        density * (thicknessMm / MillimetresPerMetre) * (widthMm / MillimetresPerMetre);
}
