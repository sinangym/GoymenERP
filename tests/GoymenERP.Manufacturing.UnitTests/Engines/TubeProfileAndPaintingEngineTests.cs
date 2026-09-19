using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Painting;
using GoymenERP.Modules.Manufacturing.Domain.Engines.TubeProfile;

namespace GoymenERP.Manufacturing.UnitTests.Engines;

/// <summary>
/// MFG-8 slice 1: the tube / profile (master v3 §28.6, TUB-001…004) and painting (§28.7, PNT-001…006)
/// engines. The goldens' own values are TEST inputs, not production parameters; every result here is exact
/// in decimal (verified against Python <c>decimal</c> at prec 40), so the goldens are asserted exactly.
/// </summary>
public sealed class TubeProfileAndPaintingEngineTests
{
    private const decimal ReferencePrecision = 0.000000000000000001m;

    [Fact]
    public void Tube_master_golden_20_packages_of_50_at_6_m_and_30_m_per_minute()
    {
        var run = TubeProfileEngineV1.Run(new TubeRunInputV1(20, 50, 6000m, 30m));

        Assert.Equal(EngineOutcome.Computed, run.Outcome);
        Assert.Equal(new TubeRunV1(TubeProfileEngineV1.FormulaVersion, 1000, 6000m, 200m), run.Value);
    }

    [Fact]
    public void Tube_mass_needs_the_unit_kg_from_the_master_and_never_estimates_it()
    {
        // §28.6: the unit kg is the product / package master's; until Product supplies it, it is unavailable.
        var missing = TubeProfileEngineV1.Mass(new TubeMassInputV1(1000, null));
        Assert.Equal((EngineOutcome.Unavailable, "unitKg"), (missing.Outcome, Assert.Single(missing.MissingParameters)));

        // With an explicit (test) unit mass the total is exact (TUB-004).
        Assert.Equal(12_345m, TubeProfileEngineV1.Mass(new TubeMassInputV1(1000, 12.345m)).Value!.TotalKg);

        // The run itself does not wait for the unit mass.
        Assert.Equal(EngineOutcome.Computed, TubeProfileEngineV1.Run(new TubeRunInputV1(20, 50, 6000m, 30m)).Outcome);

        Assert.Equal(["unitKg", "totalPieces"], TubeProfileEngineV1.Mass(new TubeMassInputV1(null, null)).MissingParameters);
        Assert.Equal((EngineCodes.CountNotPositive, "totalPieces"),
            Refusal(TubeProfileEngineV1.Mass(new TubeMassInputV1(0, 12.345m))));
        Assert.Equal((EngineCodes.PrecisionExceeded, "unitKg"),
            Refusal(TubeProfileEngineV1.Mass(new TubeMassInputV1(1000, 12.3456m))));
    }

    [Fact]
    public void Tube_run_names_missing_inputs_and_refuses_wrong_ones()
    {
        Assert.Equal(["packageCount", "piecesPerPackage", "pieceLengthMm", "lineSpeedMetresPerMinute"],
            TubeProfileEngineV1.Run(new TubeRunInputV1(null, null, null, null)).MissingParameters);
        Assert.Equal((EngineCodes.CountNotPositive, "packageCount"),
            Refusal(TubeProfileEngineV1.Run(new TubeRunInputV1(0, 50, 6000m, 30m))));
        Assert.Equal((EngineCodes.PrecisionExceeded, "pieceLengthMm"),
            Refusal(TubeProfileEngineV1.Run(new TubeRunInputV1(20, 50, 6000.0001m, 30m))));
        Assert.Equal((EngineCodes.ValueNotPositive, "lineSpeedMetresPerMinute"),
            Refusal(TubeProfileEngineV1.Run(new TubeRunInputV1(20, 50, 6000m, 0m))));
    }

    /// <summary>
    /// §28.7's golden (10×50 pieces, 6 m, perimeter 160 mm, 20 m/min, 80 µm, solids 0,55, factor 1,10 →
    /// 480 m², 76,800 L, 50 min). The golden does NOT state EşZamanlıProfilAdedi: 50 min follows only with 3
    /// (3000 m / 3 / 20 m/min). 3 is DERIVED from the master's result, used here as a test input and never
    /// a default; the next test measures PNT-004 with an explicitly stated count.
    /// </summary>
    [Fact]
    public void Painting_master_golden_with_the_simultaneous_count_derived_from_its_50_minutes()
    {
        var run = PaintingEngineV1.Run(new PaintRunInputV1(10, 50, 6000m, SimultaneousProfiles: 3, 20m));
        Assert.Equal(new PaintRunV1(PaintingEngineV1.FormulaVersion, 500, 3000m, 1000m, 50m), run.Value);

        var area = PaintingEngineV1.Area(new PaintAreaInputV1(160m, 6000m, 500)).Value!;
        Assert.Equal(480m, area.AreaSquareMetres);

        var litres = PaintingEngineV1.Paint(new PaintLitresInputV1(area.AreaSquareMetres, 80m, 0.55m, 1.10m)).Value!;
        Assert.Equal(76.8m, litres.TheoreticalLitres);
    }

    [Fact]
    public void Painting_time_with_an_explicitly_stated_simultaneous_count()
    {
        var two = PaintingEngineV1.Run(new PaintRunInputV1(10, 50, 6000m, SimultaneousProfiles: 2, 20m)).Value!;
        Assert.Equal((1500m, 75m), (two.LinePassMetres, two.Minutes));

        // A non-terminating time is exact to the decimal's precision (no rounding).
        var slow = PaintingEngineV1.Run(new PaintRunInputV1(10, 50, 6000m, SimultaneousProfiles: 3, 7m)).Value!;
        Assert.True(Math.Abs(142.857142857142857142857142857m - slow.Minutes) < ReferencePrecision);
    }

    [Fact]
    public void Painting_inputs_are_never_hard_coded_a_missing_one_is_named()
    {
        Assert.Equal(["simultaneousProfiles"],
            PaintingEngineV1.Run(new PaintRunInputV1(10, 50, 6000m, null, 20m)).MissingParameters);
        Assert.Equal(["solidsRatio", "scrapFactor"],
            PaintingEngineV1.Paint(new PaintLitresInputV1(480m, 80m, null, null)).MissingParameters);
        Assert.Equal(["perimeterMm", "profileLengthMm", "pieces"],
            PaintingEngineV1.Area(new PaintAreaInputV1(null, null, null)).MissingParameters);
    }

    [Fact]
    public void A_solids_ratio_above_one_is_refused_never_read_as_a_percentage()
    {
        Assert.Equal((EngineCodes.RatioAboveOne, "solidsRatio"),
            Refusal(PaintingEngineV1.Paint(new PaintLitresInputV1(480m, 80m, 55m, 1.10m))));
        // Exactly 1 (all solids) is a value.
        Assert.Equal(42.24m, PaintingEngineV1.Paint(new PaintLitresInputV1(480m, 80m, 1m, 1.10m)).Value!.TheoreticalLitres);
        Assert.Equal((EngineCodes.ValueNotPositive, "solidsRatio"),
            Refusal(PaintingEngineV1.Paint(new PaintLitresInputV1(480m, 80m, 0m, 1.10m))));
    }

    [Fact]
    public void Painting_refuses_wrong_counts_and_nominal_precision()
    {
        Assert.Equal((EngineCodes.CountNotPositive, "simultaneousProfiles"),
            Refusal(PaintingEngineV1.Run(new PaintRunInputV1(10, 50, 6000m, 0, 20m))));
        Assert.Equal((EngineCodes.PrecisionExceeded, "perimeterMm"),
            Refusal(PaintingEngineV1.Area(new PaintAreaInputV1(160.0001m, 6000m, 500))));
        Assert.Equal((EngineCodes.CountNotPositive, "pieces"),
            Refusal(PaintingEngineV1.Area(new PaintAreaInputV1(160m, 6000m, 0))));
    }

    private static (string?, string?) Refusal<T>(EngineResult<T> result)
        where T : class => (result.RefusalCode, result.RefusedParameter);
}
