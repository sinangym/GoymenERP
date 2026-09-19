using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.ColdRolling;

namespace GoymenERP.Manufacturing.UnitTests.Engines;

/// <summary>
/// MFG-7 slice 1: the cold-rolling engine (master v3 §28.8, CR-001…005). The golden example is asserted at
/// the master's printed precision (three decimals, within 0.0005; not a rounding policy) and against an
/// independent 40-digit reference (Python <c>decimal</c>, prec 40) within 1e-18. The density 7850 kg/m³
/// (= 7,85 kg/dm³, the master's MAT-001) and the scrap 0 kg are the TEST inputs the example implies, not
/// production parameters.
/// </summary>
public sealed class ColdRollingEngineTests
{
    private const decimal MasterPrecision = 0.0005m;
    private const decimal ReferencePrecision = 0.000000000000000001m;
    private const decimal ExampleDensity = 7850m;

    [Fact]
    public void Master_golden_28_750_kg_1500_mm_from_2_00_to_1_50_mm()
    {
        var result = ColdRollingEngineV1.Roll(new ColdRollingInputV1(28750m, 1500m, 2.00m, 1.50m, 0m, ExampleDensity));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        var rolled = result.Value!;
        Assert.Equal(ColdRollingEngineV1.FormulaVersion, rolled.FormulaVersion);
        Near(1220.807m, rolled.InputMetres, MasterPrecision); // CR-001
        Near(1627.742m, rolled.OutputMetres, MasterPrecision); // CR-004
        Near(1.333m, rolled.ElongationRatio, MasterPrecision); // CR-003
        Assert.Equal(25m, rolled.ReductionPercent); // CR-005, exact
        Assert.Equal(28750m, rolled.OutputKg); // CR-002, exact

        Near(1220.806794055201698513800425m, rolled.InputMetres, ReferencePrecision);
        Near(1627.742392073602264685067233m, rolled.OutputMetres, ReferencePrecision);
        Near(1.333333333333333333333333333m, rolled.ElongationRatio, ReferencePrecision);
    }

    [Fact]
    public void Scrap_leaves_the_output_mass_exactly_and_the_output_metres_follow_it()
    {
        var rolled = ColdRollingEngineV1.Roll(new ColdRollingInputV1(28750m, 1500m, 2m, 1.5m, 250.5m, ExampleDensity)).Value!;

        Assert.Equal(28499.5m, rolled.OutputKg);
        Near(1613.559801840056617126680821m, rolled.OutputMetres, ReferencePrecision);
        // The input side does not depend on the scrap.
        Near(1220.806794055201698513800425m, rolled.InputMetres, ReferencePrecision);
    }

    [Fact]
    public void A_missing_parameter_is_unavailable_and_named_a_stated_zero_scrap_is_a_value()
    {
        var noDensity = ColdRollingEngineV1.Roll(new ColdRollingInputV1(28750m, 1500m, 2m, 1.5m, 0m, null));
        Assert.Equal((EngineOutcome.Unavailable, "densityKgPerCubicMetre"),
            (noDensity.Outcome, Assert.Single(noDensity.MissingParameters)));

        var noScrap = ColdRollingEngineV1.Roll(new ColdRollingInputV1(28750m, 1500m, 2m, 1.5m, null, ExampleDensity));
        Assert.Equal(["scrapKg"], noScrap.MissingParameters);

        var nothing = ColdRollingEngineV1.Roll(new ColdRollingInputV1(null, null, null, null, null, null));
        Assert.Equal(["inputKg", "widthMm", "inputThicknessMm", "targetThicknessMm", "scrapKg", "densityKgPerCubicMetre"],
            nothing.MissingParameters);
    }

    [Theory]
    [InlineData(1.5, 1.5)] // equal: nothing is rolled
    [InlineData(1.5, 2.0)] // thicker: cold rolling only thins
    public void A_target_thickness_not_below_the_input_is_refused(double input, double target)
    {
        var result = ColdRollingEngineV1.Roll(new ColdRollingInputV1(28750m, 1500m, (decimal)input, (decimal)target, 0m, ExampleDensity));

        Assert.Equal((EngineOutcome.Refused, EngineCodes.TargetThicknessNotBelowInput, "targetThicknessMm"),
            (result.Outcome, result.RefusalCode, result.RefusedParameter));
    }

    [Theory]
    [InlineData(28750)] // equal to the input mass
    [InlineData(30000)] // more than the input mass
    public void Scrap_equal_to_or_above_the_input_mass_is_refused(int scrapKg)
    {
        var result = ColdRollingEngineV1.Roll(new ColdRollingInputV1(28750m, 1500m, 2m, 1.5m, scrapKg, ExampleDensity));

        Assert.Equal((EngineOutcome.Refused, EngineCodes.ScrapNotBelowInputMass, "scrapKg"),
            (result.Outcome, result.RefusalCode, result.RefusedParameter));

        // Just below the input mass is still a (very small) output.
        Assert.Equal(0.001m, ColdRollingEngineV1.Roll(new ColdRollingInputV1(28750m, 1500m, 2m, 1.5m, 28749.999m, ExampleDensity))
            .Value!.OutputKg);
    }

    [Theory]
    [InlineData("inputKg")]
    [InlineData("widthMm")]
    [InlineData("inputThicknessMm")]
    [InlineData("targetThicknessMm")]
    [InlineData("scrapKg")]
    public void A_fourth_decimal_on_a_kilogram_or_millimetre_is_refused_never_rounded(string field)
    {
        var result = ColdRollingEngineV1.Roll(new ColdRollingInputV1(
            field == "inputKg" ? 28750.0001m : 28750m,
            field == "widthMm" ? 1500.0001m : 1500m,
            field == "inputThicknessMm" ? 2.0001m : 2m,
            field == "targetThicknessMm" ? 1.5001m : 1.5m,
            field == "scrapKg" ? 0.0001m : 0m,
            ExampleDensity));

        Assert.Equal((EngineOutcome.Refused, EngineCodes.PrecisionExceeded, field),
            (result.Outcome, result.RefusalCode, result.RefusedParameter));
    }

    [Fact]
    public void Non_positive_inputs_and_negative_scrap_are_refused()
    {
        Assert.Equal((EngineCodes.ValueNotPositive, "widthMm"),
            Refusal(ColdRollingEngineV1.Roll(new ColdRollingInputV1(28750m, 0m, 2m, 1.5m, 0m, ExampleDensity))));
        Assert.Equal((EngineCodes.ValueNotPositive, "densityKgPerCubicMetre"),
            Refusal(ColdRollingEngineV1.Roll(new ColdRollingInputV1(28750m, 1500m, 2m, 1.5m, 0m, -7850m))));
        Assert.Equal((EngineCodes.ValueNegative, "scrapKg"),
            Refusal(ColdRollingEngineV1.Roll(new ColdRollingInputV1(28750m, 1500m, 2m, 1.5m, -1m, ExampleDensity))));
    }

    private static (string?, string?) Refusal(EngineResult<ColdRollingV1> result) => (result.RefusalCode, result.RefusedParameter);

    private static void Near(decimal expected, decimal actual, decimal precision) =>
        Assert.True(Math.Abs(expected - actual) < precision, $"expected {expected} ± {precision}, got {actual}");
}
