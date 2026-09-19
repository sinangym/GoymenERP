using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Slitting;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Winding;

namespace GoymenERP.Manufacturing.UnitTests.Engines;

/// <summary>
/// The engines never guess: a missing parameter is Unavailable (named), never a default or 0; a broken
/// rule is Refused (coded), never clamped. Reference values are independent 40-digit results
/// (Python <c>decimal</c>, prec 40).
/// </summary>
public sealed class EngineBehaviourTests
{
    private const decimal ReferencePrecision = 0.000000000000000001m;

    [Fact]
    public void A_missing_parameter_is_unavailable_and_named_never_a_default()
    {
        // No density: the engine has none built in (OPEN-007), so the line time is not computed.
        var noDensity = SlittingEngineV1.LineTime(new LineTimeInputV1(13400m, 1.25m, 1240m, null, 120m));
        Assert.Equal(EngineOutcome.Unavailable, noDensity.Outcome);
        Assert.Null(noDensity.Value);
        Assert.Equal(["densityKgPerCubicMetre"], noDensity.MissingParameters);

        var nothing = WindingEngineV1.Target(new WindingTargetInputV1(null, null, 1500m, null, 508m));
        Assert.Equal(EngineOutcome.Unavailable, nothing.Outcome);
        Assert.Equal(["densityKgPerCubicMetre", "thicknessMm", "targetKg"], nothing.MissingParameters);

        var noLineCount = SlittingEngineV1.Plan(new SlittingPlanInputV1(28750m, 1500m, [new SlitLineInputV1(150m, null)]));
        Assert.Equal(["lines[0].count"], noLineCount.MissingParameters);
    }

    [Fact]
    public void A_tolerance_is_never_assumed_zero_when_missing_but_a_stated_zero_is_a_value()
    {
        var missing = WindingEngineV1.Fit(new WindingFitInputV1(424m, null, 424.628m, 1371.14m, 1500m));
        Assert.Equal(EngineOutcome.Unavailable, missing.Outcome);
        Assert.Equal(["toleranceMetres"], missing.MissingParameters);

        var statedZero = WindingEngineV1.Fit(new WindingFitInputV1(424m, 0m, 424.628m, 1371.14m, 1500m));
        Assert.Equal(new WindingFitV1(WindingEngineV1.FormulaVersion, MetresSufficient: false, DiameterFits: true), statedZero.Value);

        var covered = WindingEngineV1.Fit(new WindingFitInputV1(424m, 1m, 424.628m, 1600m, 1500m));
        Assert.Equal(new WindingFitV1(WindingEngineV1.FormulaVersion, MetresSufficient: true, DiameterFits: false), covered.Value);

        var negative = WindingEngineV1.Fit(new WindingFitInputV1(424m, -1m, 424.628m, 1371.14m, 1500m));
        Assert.Equal((EngineOutcome.Refused, EngineCodes.ValueNegative, "toleranceMetres"),
            (negative.Outcome, negative.RefusalCode, negative.RefusedParameter));
    }

    [Fact]
    public void Used_width_beyond_the_net_width_is_refused_not_clamped()
    {
        // Master §28.4: the used width cannot exceed the net width. 150×4 + 240×4 = 1560 > 1500.
        var result = SlittingEngineV1.Plan(new SlittingPlanInputV1(28750m, 1500m,
            [new SlitLineInputV1(150m, 4), new SlitLineInputV1(240m, 4)]));
        Assert.Equal((EngineOutcome.Refused, EngineCodes.UsedWidthExceedsNetWidth), (result.Outcome, result.RefusalCode));

        // Exactly the net width fits: nothing remains.
        var exact = SlittingEngineV1.Plan(new SlittingPlanInputV1(28750m, 1500m, [new SlitLineInputV1(250m, 6)]));
        Assert.Equal((0m, 0m), (exact.Value!.RemainingWidthMm, exact.Value.RemainingKg));
    }

    [Theory]
    [InlineData("parentKg")]
    [InlineData("netUsableWidthMm")]
    [InlineData("bandWidthMm")]
    public void A_fourth_decimal_on_a_kilogram_or_millimetre_is_refused_never_rounded(string field)
    {
        var result = SlittingEngineV1.Plan(new SlittingPlanInputV1(
            field == "parentKg" ? 28750.0001m : 28750.1230m,
            field == "netUsableWidthMm" ? 1500.0005m : 1500m,
            [new SlitLineInputV1(field == "bandWidthMm" ? 150.1234m : 150m, 4)]));

        Assert.Equal((EngineOutcome.Refused, EngineCodes.PrecisionExceeded), (result.Outcome, result.RefusalCode));
        Assert.EndsWith(field, result.RefusedParameter);
    }

    [Fact]
    public void Non_positive_values_counts_and_an_empty_plan_are_refused()
    {
        Assert.Equal(EngineCodes.ValueNotPositive,
            SlittingEngineV1.Plan(new SlittingPlanInputV1(0m, 1500m, [new SlitLineInputV1(150m, 4)])).RefusalCode);
        Assert.Equal(EngineCodes.CountNotPositive,
            SlittingEngineV1.Plan(new SlittingPlanInputV1(28750m, 1500m, [new SlitLineInputV1(150m, 0)])).RefusalCode);
        Assert.Equal((EngineCodes.CountNotPositive, "lines"),
            (SlittingEngineV1.Plan(new SlittingPlanInputV1(28750m, 1500m, [])).RefusalCode,
                SlittingEngineV1.Plan(new SlittingPlanInputV1(28750m, 1500m, [])).RefusedParameter));
        Assert.Equal(EngineCodes.ValueNotPositive,
            SlittingEngineV1.LineTime(new LineTimeInputV1(13400m, 1.25m, 1240m, 7850m, 0m)).RefusalCode);
    }

    [Fact]
    public void Theoretical_band_count_and_width_scrap_SLT_008_009()
    {
        var fit = SlittingEngineV1.BandFit(new BandFitInputV1(1500m, 240m, 6));
        Assert.Equal(new BandFitV1(SlittingEngineV1.FormulaVersion, 6, 60m), fit.Value);

        var partial = SlittingEngineV1.BandFit(new BandFitInputV1(1500m, 240m, 5));
        Assert.Equal((6, 300m), (partial.Value!.TheoreticalCount, partial.Value.WidthScrapMm));

        var tooMany = SlittingEngineV1.BandFit(new BandFitInputV1(1500m, 240m, 7));
        Assert.Equal((EngineOutcome.Refused, EngineCodes.BandsExceedRawWidth), (tooMany.Outcome, tooMany.RefusalCode));
    }

    [Fact]
    public void Winding_capacity_WND_005_007_on_the_golden_band()
    {
        var result = WindingEngineV1.Capacity(new WindingCapacityInputV1(7850m, 3m, 1500m, 508m, 1500m, 2));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        var capacity = result.Value!;
        Near(521.487625335086532020860334m, capacity.MaximumMetres);
        Near(18421.550364961931743636891305m, capacity.MaximumSingleCoilKg);
        Near(36843.100729923863487273782611m, capacity.GroupMaximumKg);

        var inverted = WindingEngineV1.Capacity(new WindingCapacityInputV1(7850m, 3m, 1500m, 1500m, 1500m, 2));
        Assert.Equal((EngineOutcome.Refused, EngineCodes.InnerDiameterNotBelowMaximum), (inverted.Outcome, inverted.RefusalCode));
    }

    [Fact]
    public void A_supplied_wrong_value_is_answered_before_a_missing_one()
    {
        // A refusal names what the caller must correct; missing parameters alone are "veri eksik".
        var result = WindingEngineV1.Target(new WindingTargetInputV1(null, -3m, 1500m, 15000m, 508m));
        Assert.Equal((EngineOutcome.Refused, EngineCodes.ValueNotPositive, "thicknessMm"),
            (result.Outcome, result.RefusalCode, result.RefusedParameter));
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("1", "1")]
    [InlineData("144", "12")]
    [InlineData("0.0625", "0.25")]
    [InlineData("1880046.18", null)]
    public void The_decimal_square_root_is_exact_where_exact_and_a_fixed_point_otherwise(string value, string? root)
    {
        var number = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var result = EngineMath.Sqrt(number);
        if (root is not null)
        {
            Assert.Equal(decimal.Parse(root, System.Globalization.CultureInfo.InvariantCulture), result);
        }

        Near(number, result * result, 0.000000000000000000001m * Math.Max(1m, number));
        Assert.Throws<ArgumentOutOfRangeException>(() => EngineMath.Sqrt(-1m));
    }

    [Fact]
    public void The_square_root_of_two_matches_an_independent_reference()
    {
        Near(1.414213562373095048801688724m, EngineMath.Sqrt(2m));
    }

    private static void Near(decimal expected, decimal actual, decimal precision = ReferencePrecision) =>
        Assert.True(Math.Abs(expected - actual) < precision, $"expected {expected} ± {precision}, got {actual}");
}
