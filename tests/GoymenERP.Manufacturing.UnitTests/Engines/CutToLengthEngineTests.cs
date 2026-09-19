using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.CutToLength;

namespace GoymenERP.Manufacturing.UnitTests.Engines;

/// <summary>
/// MFG-9 slice 1: the cut-to-length engine (master v3 §28.3, CUT-001…008). The §28.3 golden has no kerf, so
/// it covers CUT-001…006; CUT-007/008 (the master's "hedef davranış" with a mandatory kerf) have no golden
/// and are tested with explicit inputs — none is invented. Density 7850 kg/m³ is a TEST input. References:
/// Python <c>decimal</c> at prec 40.
/// </summary>
public sealed class CutToLengthEngineTests
{
    private const decimal ReferencePrecision = 0.000000000000000001m;
    private const decimal ExampleDensity = 7850m;

    private static readonly CutLineInputV1[] GoldenLines = [new(3000m, 50), new(6000m, 55), new(4500m, 10)];

    [Fact]
    public void Master_golden_28_750_kg_3x1500_as_3000x50_6000x55_4500x10()
    {
        var result = CutToLengthEngineV1.Plan(new CutPlanInputV1(28750m, 3m, 1500m, ExampleDensity, GoldenLines));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        var plan = result.Value!;
        Assert.Equal(CutToLengthEngineV1.FormulaVersion, plan.FormulaVersion);
        Assert.Equal([105.975m, 211.95m, 158.9625m], plan.Lines.Select(line => line.UnitKg)); // CUT-001 (§28.2 MAT-002)
        Assert.Equal([5298.75m, 11657.25m, 1589.625m], plan.Lines.Select(line => line.PlanKg)); // CUT-002
        Assert.Equal([150m, 330m, 45m], plan.Lines.Select(line => line.OutputMetres)); // CUT-003
        Assert.Equal(525m, plan.PlanMetres);
        Assert.Equal(18545.625m, plan.PlanKg);
        Assert.Equal(10204.375m, plan.RemainingKg); // CUT-005, exact
        Near(813.871196036801132342533616m, plan.InputMetres); // CUT-004
        Near(288.871196036801132342533616m, plan.RemainingMetres); // CUT-006
    }

    [Fact]
    public void An_exact_fit_leaves_exactly_zero_and_one_kilogram_more_is_refused_never_a_negative_remainder()
    {
        // The golden lines' own mass as the coil: nothing remains, in kg and in metres (no 1e-25 residue).
        var exact = CutToLengthEngineV1.Plan(new CutPlanInputV1(18545.625m, 3m, 1500m, ExampleDensity, GoldenLines)).Value!;
        Assert.Equal((0m, 0m), (exact.RemainingKg, exact.RemainingMetres));

        var over = CutToLengthEngineV1.Plan(new CutPlanInputV1(18545.624m, 3m, 1500m, ExampleDensity, GoldenLines));
        Assert.Equal((EngineOutcome.Refused, EngineCodes.PlanExceedsInputMass, "lines"),
            (over.Outcome, over.RefusalCode, over.RefusedParameter));
    }

    [Fact]
    public void Cuts_per_raw_length_need_the_kerf_a_missing_kerf_is_unavailable_a_stated_zero_is_a_value()
    {
        var missing = CutToLengthEngineV1.CutsPerRawLength(new CutsPerRawLengthInputV1(6000m, 1000m, null));
        Assert.Equal((EngineOutcome.Unavailable, "kerfMm"), (missing.Outcome, Assert.Single(missing.MissingParameters)));

        // Explicit test inputs (no master golden with kerf): floor(6000 / (1000 + 3)) = 5; with a stated 0 kerf, 6.
        Assert.Equal(5, CutToLengthEngineV1.CutsPerRawLength(new CutsPerRawLengthInputV1(6000m, 1000m, 3m)).Value!.Pieces);
        Assert.Equal(6, CutToLengthEngineV1.CutsPerRawLength(new CutsPerRawLengthInputV1(6000m, 1000m, 0m)).Value!.Pieces);
    }

    [Fact]
    public void Length_scrap_takes_the_pieces_and_the_cuts_as_stated_never_derives_the_cuts()
    {
        // §28.3 does not say whether N pieces take N or N−1 cuts: the caller states it.
        Assert.Equal(985m, CutToLengthEngineV1.LengthScrap(new LengthScrapInputV1(6000m, 1000m, 5, 3m, 5)).Value!.ScrapMm);
        Assert.Equal(988m, CutToLengthEngineV1.LengthScrap(new LengthScrapInputV1(6000m, 1000m, 5, 3m, 4)).Value!.ScrapMm);

        var noCuts = CutToLengthEngineV1.LengthScrap(new LengthScrapInputV1(6000m, 1000m, 5, 3m, null));
        Assert.Equal(["cuts"], noCuts.MissingParameters);
        var noKerf = CutToLengthEngineV1.LengthScrap(new LengthScrapInputV1(6000m, 1000m, 5, null, 5));
        Assert.Equal(["kerfMm"], noKerf.MissingParameters);

        // A stated 0 cuts (one piece left as it came) is a value.
        Assert.Equal(0m, CutToLengthEngineV1.LengthScrap(new LengthScrapInputV1(1000m, 1000m, 1, 3m, 0)).Value!.ScrapMm);
    }

    [Fact]
    public void Cuts_beyond_the_raw_length_are_refused_never_a_negative_scrap()
    {
        // 6 × 1000 + 6 × 3 = 6018 > 6000.
        var over = CutToLengthEngineV1.LengthScrap(new LengthScrapInputV1(6000m, 1000m, 6, 3m, 6));
        Assert.Equal((EngineOutcome.Refused, EngineCodes.CutsExceedRawLength, "pieces"),
            (over.Outcome, over.RefusalCode, over.RefusedParameter));

        Assert.Equal((EngineCodes.ValueNegative, "cuts"),
            (CutToLengthEngineV1.LengthScrap(new LengthScrapInputV1(6000m, 1000m, 5, 3m, -1)).RefusalCode, "cuts"));
    }

    [Fact]
    public void The_plan_names_missing_inputs_and_refuses_wrong_ones()
    {
        Assert.Equal(["inputKg", "thicknessMm", "widthMm", "densityKgPerCubicMetre", "lines[0].targetLengthMm", "lines[0].count"],
            CutToLengthEngineV1.Plan(new CutPlanInputV1(null, null, null, null, [new CutLineInputV1(null, null)])).MissingParameters);
        Assert.Equal((EngineCodes.CountNotPositive, "lines"),
            Refusal(CutToLengthEngineV1.Plan(new CutPlanInputV1(28750m, 3m, 1500m, ExampleDensity, []))));
        Assert.Equal((EngineCodes.PrecisionExceeded, "lines[1].targetLengthMm"),
            Refusal(CutToLengthEngineV1.Plan(new CutPlanInputV1(28750m, 3m, 1500m, ExampleDensity,
                [new CutLineInputV1(3000m, 50), new CutLineInputV1(6000.0001m, 55)]))));
        Assert.Equal((EngineCodes.PrecisionExceeded, "kerfMm"),
            Refusal(CutToLengthEngineV1.CutsPerRawLength(new CutsPerRawLengthInputV1(6000m, 1000m, 3.0001m))));
    }

    private static (string?, string?) Refusal<T>(EngineResult<T> result)
        where T : class => (result.RefusalCode, result.RefusedParameter);

    private static void Near(decimal expected, decimal actual) =>
        Assert.True(Math.Abs(expected - actual) < ReferencePrecision, $"expected {expected} ± {ReferencePrecision}, got {actual}");
}
