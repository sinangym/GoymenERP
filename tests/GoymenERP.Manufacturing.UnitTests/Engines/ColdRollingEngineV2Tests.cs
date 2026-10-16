using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.ColdRolling;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests.Engines;

public sealed class ColdRollingEngineV2Tests
{
    private static readonly Guid Recipe = Guid.Parse("91000000-0000-4000-8000-000000000001");
    private static readonly Guid FirstLine = Guid.Parse("91000000-0000-4000-8000-000000000002");
    private static readonly Guid SecondLine = Guid.Parse("91000000-0000-4000-8000-000000000003");

    [Fact]
    public void Two_pass_golden_uses_the_same_explicit_wip_mass_without_rounding()
    {
        var result = ColdRollingEngineV2.Roll(new ColdRollingPassInputV2(12000m, 8000m, Spec(
            Line(FirstLine, -7, 2.000m, 1000m, 1.600m, 1000m),
            Line(SecondLine, 0, 1.600m, 1000m, 1.000m, 1000m))));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        var rolled = result.Value!;
        Assert.Equal(("GDC-CR/2", Recipe, "recipe-v3", 12000m, 8000m),
            (rolled.FormulaVersion, rolled.PassRecipeId, rolled.PassRecipeVersion, rolled.WipKg,
                rolled.DensityKgPerCubicMetre));
        Assert.Equal([FirstLine, SecondLine], rolled.Lines.Select(line => line.LineId));
        Assert.Equal([-7, 0], rolled.Lines.Select(line => line.SourceSequence));

        Assert.Equal((750m, 937.5m, 20m, 1.25m),
            (rolled.Lines[0].InputMetres, rolled.Lines[0].OutputMetres,
                rolled.Lines[0].ReductionPercent, rolled.Lines[0].ElongationRatio));
        Assert.Equal((937.5m, 1500m, 37.5m, 1.6m),
            (rolled.Lines[1].InputMetres, rolled.Lines[1].OutputMetres,
                rolled.Lines[1].ReductionPercent, rolled.Lines[1].ElongationRatio));
    }

    [Fact]
    public void Source_order_and_arbitrary_signed_sequences_are_preserved_not_sorted()
    {
        var first = Line(FirstLine, int.MaxValue, 2m, 1000m, 1.5m, 1000m);
        var second = Line(SecondLine, int.MinValue, 1.5m, 1000m, 1m, 1000m);
        var source = new[] { first, second };
        var spec = Spec(source);
        source[0] = second;

        var rolled = ColdRollingEngineV2.Roll(new ColdRollingPassInputV2(12000m, 8000m, spec)).Value!;

        Assert.Equal([FirstLine, SecondLine], spec.Lines.Select(line => line.LineId));
        Assert.Equal([int.MaxValue, int.MinValue], rolled.Lines.Select(line => line.SourceSequence));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 7850)]
    public void Elongation_is_exact_geometry_and_independent_of_mass_and_density(decimal wipKg, decimal density)
    {
        var result = ColdRollingEngineV2.Roll(new ColdRollingPassInputV2(
            wipKg,
            density,
            Spec(Line(FirstLine, 0, 5m, 7m, 1m, 5m))));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        Assert.Equal(7m, result.Value!.Lines.Single().ElongationRatio);
    }

    [Fact]
    public void Elongation_uses_direct_cross_section_geometry_not_rounded_lengths()
    {
        var result = ColdRollingEngineV2.Roll(new ColdRollingPassInputV2(
            1m,
            7850m,
            Spec(Line(FirstLine, 0, 2m, 7m, 1m, 5m))));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        Assert.Equal(2.8m, result.Value!.Lines.Single().ElongationRatio);
    }

    [Fact]
    public void Representable_extreme_mass_computes_without_intermediate_overflow()
    {
        var result = ColdRollingEngineV2.Roll(new ColdRollingPassInputV2(
            decimal.MaxValue,
            1_000_000_000m,
            Spec(Line(FirstLine, 0, 2m, 1000m, 1m, 1000m))));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Lines.Single().InputMetres > 0m);
    }

    [Fact]
    public void Representable_tiny_length_computes_without_intermediate_underflow()
    {
        var result = ColdRollingEngineV2.Roll(new ColdRollingPassInputV2(
            1m,
            1_000_000m,
            Spec(Line(FirstLine, 0, 1_000_000_000_000m, 1_000_000_000_000m,
                999_999_999_999m, 1_000_000_000_000m))));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        Assert.Equal(0.000000000000000000000001m, result.Value!.Lines.Single().InputMetres);
    }

    [Fact]
    public void Unrepresentable_arithmetic_is_a_stable_refusal_and_never_throws()
    {
        var overflow = ColdRollingEngineV2.Roll(new ColdRollingPassInputV2(
            1m,
            0.0000000000000000000000000001m,
            Spec(Line(FirstLine, 0, 0.002m, 0.001m, 0.001m, 0.001m))));
        var underflow = ColdRollingEngineV2.Roll(new ColdRollingPassInputV2(
            0.001m,
            decimal.MaxValue,
            Spec(Line(FirstLine, 0, decimal.MaxValue, decimal.MaxValue,
                decimal.MaxValue - 1m, decimal.MaxValue))));

        Assert.All([overflow, underflow], result =>
        {
            Assert.Equal(EngineOutcome.Refused, result.Outcome);
            Assert.Equal(EngineCodes.ArithmeticUnrepresentable, result.RefusalCode);
            Assert.Equal("coldRollingPassArithmetic", result.RefusedParameter);
            Assert.Null(result.Value);
        });
    }

    [Fact]
    public void Missing_wip_density_and_spec_are_named_and_never_defaulted_to_zero()
    {
        var missingWip = ColdRollingEngineV2.Roll(new ColdRollingPassInputV2(null, 8000m, Spec(DefaultLine())));
        var missingDensity = ColdRollingEngineV2.Roll(new ColdRollingPassInputV2(12000m, null, Spec(DefaultLine())));
        var missingSpec = ColdRollingEngineV2.Roll(new ColdRollingPassInputV2(12000m, 8000m, null));
        var all = ColdRollingEngineV2.Roll(new ColdRollingPassInputV2(null, null, null));

        Assert.Equal(["wipKg"], missingWip.MissingParameters);
        Assert.Equal(["densityKgPerCubicMetre"], missingDensity.MissingParameters);
        Assert.Equal(["passSpec"], missingSpec.MissingParameters);
        Assert.Equal(["wipKg", "densityKgPerCubicMetre", "passSpec"], all.MissingParameters);
        Assert.All([missingWip, missingDensity, missingSpec, all], value =>
        {
            Assert.Equal(EngineOutcome.Unavailable, value.Outcome);
            Assert.Null(value.Value);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Wip_mass_must_be_positive(decimal value)
    {
        var result = ColdRollingEngineV2.Roll(new ColdRollingPassInputV2(value, 8000m, Spec(DefaultLine())));

        Assert.Equal((EngineOutcome.Refused, EngineCodes.ValueNotPositive, "wipKg"),
            (result.Outcome, result.RefusalCode, result.RefusedParameter));
    }

    [Fact]
    public void Wip_mass_beyond_three_decimals_is_refused_not_rounded()
    {
        var result = ColdRollingEngineV2.Roll(new ColdRollingPassInputV2(12000.0001m, 8000m, Spec(DefaultLine())));

        Assert.Equal((EngineOutcome.Refused, EngineCodes.PrecisionExceeded, "wipKg"),
            (result.Outcome, result.RefusalCode, result.RefusedParameter));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-8000)]
    public void Density_must_be_explicit_and_positive(decimal value)
    {
        var result = ColdRollingEngineV2.Roll(new ColdRollingPassInputV2(12000m, value, Spec(DefaultLine())));

        Assert.Equal((EngineOutcome.Refused, EngineCodes.ValueNotPositive, "densityKgPerCubicMetre"),
            (result.Outcome, result.RefusalCode, result.RefusedParameter));
    }

    [Fact]
    public void Recipe_identity_version_lines_and_line_identity_are_required()
    {
        Assert.Throws<ArgumentException>(() => new ColdRollingPassSpec(Guid.Empty, "v1", [DefaultLine()]));
        Assert.Throws<ArgumentException>(() => new ColdRollingPassSpec(Recipe, " ", [DefaultLine()]));
        Assert.Throws<ArgumentException>(() => new ColdRollingPassSpec(Recipe, "v1", []));
        Assert.Throws<ArgumentNullException>(() => new ColdRollingPassSpec(Recipe, "v1", null!));
        Assert.Throws<ArgumentException>(() => Spec(
            DefaultLine(),
            Line(FirstLine, 99, 2m, 1000m, 1m, 1000m)));
        Assert.Throws<ArgumentException>(() => Line(Guid.Empty, 0, 2m, 1000m, 1m, 1000m));
    }

    [Theory]
    [InlineData("inputThickness")]
    [InlineData("inputWidth")]
    [InlineData("outputThickness")]
    [InlineData("outputWidth")]
    public void Every_pass_measure_is_positive_and_at_most_three_decimals(string field)
    {
        decimal Pick(string candidate, decimal normal) => field == candidate ? 1.0001m : normal;
        Assert.Throws<ArgumentOutOfRangeException>(() => Line(FirstLine, 0,
            Pick("inputThickness", 2m),
            Pick("inputWidth", 1000m),
            Pick("outputThickness", 1m),
            Pick("outputWidth", 1000m)));

        decimal Zero(string candidate, decimal normal) => field == candidate ? 0m : normal;
        Assert.Throws<ArgumentOutOfRangeException>(() => Line(FirstLine, 0,
            Zero("inputThickness", 2m),
            Zero("inputWidth", 1000m),
            Zero("outputThickness", 1m),
            Zero("outputWidth", 1000m)));
    }

    [Fact]
    public void Every_pass_must_reduce_thickness()
    {
        Assert.Throws<ArgumentException>(() => Line(FirstLine, 0, 1m, 1000m, 1m, 1000m));
        Assert.Throws<ArgumentException>(() => Line(FirstLine, 0, 1m, 1000m, 1.001m, 1000m));
    }

    [Fact]
    public void Adjacent_passes_require_exact_thickness_and_width_continuity()
    {
        var first = Line(FirstLine, 0, 2m, 1000m, 1.600m, 1000m);
        var thicknessDrift = Line(SecondLine, 1, 1.599m, 1000m, 1m, 1000m);
        var widthDrift = Line(SecondLine, 1, 1.600m, 999.999m, 1m, 999.999m);

        Assert.Throws<ArgumentException>(() => Spec(first, thicknessDrift));
        Assert.Throws<ArgumentException>(() => Spec(first, widthDrift));
    }

    [Fact]
    public void Version_one_remains_its_existing_single_pass_scrap_contract()
    {
        var v1 = ColdRollingEngineV1.Roll(new ColdRollingInputV1(12000m, 1000m, 2m, 1.6m, 100m, 8000m));

        Assert.Equal(EngineOutcome.Computed, v1.Outcome);
        Assert.Equal(11900m, v1.Value!.OutputKg);
        Assert.Equal("GDC-CR/1 (master v3 §28.8)", ColdRollingEngineV1.FormulaVersion);
    }

    private static ColdRollingPassSpec Spec(params ColdRollingPassLine[] lines) =>
        new(Recipe, "recipe-v3", lines);

    private static ColdRollingPassLine DefaultLine() =>
        Line(FirstLine, 0, 2m, 1000m, 1m, 1000m);

    private static ColdRollingPassLine Line(
        Guid lineId,
        int sourceSequence,
        decimal inputThicknessMm,
        decimal inputWidthMm,
        decimal outputThicknessMm,
        decimal outputWidthMm) =>
        new(lineId, sourceSequence, inputThicknessMm, inputWidthMm, outputThicknessMm, outputWidthMm);
}
