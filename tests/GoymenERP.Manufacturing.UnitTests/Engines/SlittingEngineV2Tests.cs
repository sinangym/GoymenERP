using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Slitting;

namespace GoymenERP.Manufacturing.UnitTests.Engines;

public sealed class SlittingEngineV2Tests
{
    [Fact]
    public void Physical_width_golden_allocates_band_trim_and_net_remainder_without_rounding()
    {
        var result = SlittingEngineV2.Allocate(new SlittingMassInputV2(28750m, 1500m, 1480m, 1320m));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        Assert.Equal(
            new SlittingMassAllocationV2(
                "GDC-SLT/2",
                20m,
                new ExactMassV2(25300, 1),
                new ExactMassV2(1150, 3),
                new ExactMassV2(9200, 3)),
            result.Value);
        Assert.Equal(ExactMassV2.FromDecimal(28750m), Sum(result.Value!));
    }

    [Fact]
    public void Zero_trim_keeps_the_common_result_while_v1_remains_historical()
    {
        var v2 = SlittingEngineV2.Allocate(new SlittingMassInputV2(28750m, 1500m, 1500m, 1320m)).Value!;
        var v1 = SlittingEngineV1.Plan(new SlittingPlanInputV1(28750m, 1500m,
            [new SlitLineInputV1(150m, 4), new SlitLineInputV1(240m, 3)])).Value!;

        Assert.Equal(
            (new ExactMassV2(25300, 1), new ExactMassV2(0, 1), new ExactMassV2(3450, 1)),
            (v2.BandKg, v2.TrimKg, v2.NetRemainingKg));
        Assert.Equal(
            (ExactMassV2.FromDecimal(v1.PlanKg), ExactMassV2.FromDecimal(v1.RemainingKg)),
            (v2.BandKg, v2.NetRemainingKg));
        Assert.Equal("GDC-SLT/1 (master v3 §28.4)", SlittingEngineV1.FormulaVersion);
    }

    [Fact]
    public void Trimmed_physical_width_semantics_stay_distinct_from_v1_net_width_semantics()
    {
        var v2 = SlittingEngineV2.Allocate(new SlittingMassInputV2(28750m, 1500m, 1480m, 1320m)).Value!;
        var v1 = SlittingEngineV1.Plan(new SlittingPlanInputV1(28750m, 1480m,
            [new SlitLineInputV1(1320m, 1)])).Value!;

        Assert.Equal(new ExactMassV2(25300, 1), v2.BandKg);
        Assert.Equal(28750m * 1320m / 1480m, v1.PlanKg);
        Assert.NotEqual(ExactMassV2.FromDecimal(v1.PlanKg), v2.BandKg);
    }

    [Theory]
    [InlineData("parentPhysicalKg")]
    [InlineData("physicalWidthMm")]
    [InlineData("netUsableWidthMm")]
    [InlineData("usedBandWidthMm")]
    public void Missing_input_is_unavailable_and_named(string field)
    {
        var result = SlittingEngineV2.Allocate(new SlittingMassInputV2(
            field == "parentPhysicalKg" ? null : 28750m,
            field == "physicalWidthMm" ? null : 1500m,
            field == "netUsableWidthMm" ? null : 1480m,
            field == "usedBandWidthMm" ? null : 1320m));

        Assert.Equal(EngineOutcome.Unavailable, result.Outcome);
        Assert.Equal([field], result.MissingParameters);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("parentPhysicalKg", "0")]
    [InlineData("physicalWidthMm", "-1")]
    [InlineData("netUsableWidthMm", "0")]
    [InlineData("usedBandWidthMm", "-0.001")]
    public void Zero_or_negative_input_is_refused(string field, string invalidText)
    {
        var invalid = decimal.Parse(invalidText, System.Globalization.CultureInfo.InvariantCulture);
        var result = SlittingEngineV2.Allocate(new SlittingMassInputV2(
            field == "parentPhysicalKg" ? invalid : 28750m,
            field == "physicalWidthMm" ? invalid : 1500m,
            field == "netUsableWidthMm" ? invalid : 1480m,
            field == "usedBandWidthMm" ? invalid : 1320m));

        Assert.Equal(
            (EngineOutcome.Refused, EngineCodes.ValueNotPositive, field),
            (result.Outcome, result.RefusalCode, result.RefusedParameter));
    }

    [Theory]
    [InlineData("parentPhysicalKg")]
    [InlineData("physicalWidthMm")]
    [InlineData("netUsableWidthMm")]
    [InlineData("usedBandWidthMm")]
    public void A_fourth_decimal_is_refused_never_rounded(string field)
    {
        var result = SlittingEngineV2.Allocate(new SlittingMassInputV2(
            field == "parentPhysicalKg" ? 28750.0001m : 28750m,
            field == "physicalWidthMm" ? 1500.0001m : 1500m,
            field == "netUsableWidthMm" ? 1480.0001m : 1480m,
            field == "usedBandWidthMm" ? 1320.0001m : 1320m));

        Assert.Equal(
            (EngineOutcome.Refused, EngineCodes.PrecisionExceeded, field),
            (result.Outcome, result.RefusalCode, result.RefusedParameter));
    }

    [Fact]
    public void Width_ordering_violations_are_refused_without_clamping()
    {
        var netBeyondPhysical = SlittingEngineV2.Allocate(new SlittingMassInputV2(28750m, 1500m, 1501m, 1320m));
        Assert.Equal(
            (EngineOutcome.Refused, SlittingEngineV2.NetWidthExceedsPhysicalWidthCode, "netUsableWidthMm"),
            (netBeyondPhysical.Outcome, netBeyondPhysical.RefusalCode, netBeyondPhysical.RefusedParameter));

        var usedBeyondNet = SlittingEngineV2.Allocate(new SlittingMassInputV2(28750m, 1500m, 1480m, 1481m));
        Assert.Equal(
            (EngineOutcome.Refused, EngineCodes.UsedWidthExceedsNetWidth, "usedBandWidthMm"),
            (usedBeyondNet.Outcome, usedBeyondNet.RefusalCode, usedBeyondNet.RefusedParameter));
    }

    [Fact]
    public void One_third_components_follow_each_locked_formula_and_conserve_exactly()
    {
        var result = SlittingEngineV2.Allocate(new SlittingMassInputV2(1m, 3m, 2m, 1m));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        var allocation = result.Value!;
        Assert.Equal(new ExactMassV2(1, 3), allocation.BandKg);
        Assert.Equal(new ExactMassV2(1, 3), allocation.TrimKg);
        Assert.Equal(new ExactMassV2(1, 3), allocation.NetRemainingKg);
        Assert.Equal(new ExactMassV2(1, 1), Sum(allocation));
        Assert.All(
            [allocation.BandKg, allocation.TrimKg, allocation.NetRemainingKg],
            value => Assert.True(value.Numerator.Sign >= 0));
    }

    [Fact]
    public void Used_width_equal_to_net_width_has_canonical_zero_net_remainder()
    {
        var allocation = SlittingEngineV2.Allocate(new SlittingMassInputV2(10m, 9m, 8m, 8m)).Value!;

        Assert.Equal(new ExactMassV2(80, 9), allocation.BandKg);
        Assert.Equal(new ExactMassV2(10, 9), allocation.TrimKg);
        Assert.Equal(new ExactMassV2(0, 1), allocation.NetRemainingKg);
        Assert.Equal(new ExactMassV2(10, 1), Sum(allocation));
    }

    [Fact]
    public void Scale_valid_decimal_18_3_values_do_not_overflow_an_intermediate()
    {
        const decimal maximumAcceptedValue = 999999999999999.999m;

        var exception = Record.Exception(() => SlittingEngineV2.Allocate(new SlittingMassInputV2(
            maximumAcceptedValue,
            maximumAcceptedValue,
            maximumAcceptedValue,
            maximumAcceptedValue)));

        Assert.Null(exception);
        var allocation = SlittingEngineV2.Allocate(new SlittingMassInputV2(
            maximumAcceptedValue,
            maximumAcceptedValue,
            maximumAcceptedValue,
            maximumAcceptedValue)).Value!;
        Assert.Equal(
            (ExactMassV2.FromDecimal(maximumAcceptedValue), new ExactMassV2(0, 1), new ExactMassV2(0, 1)),
            (allocation.BandKg, allocation.TrimKg, allocation.NetRemainingKg));
        Assert.Equal(ExactMassV2.FromDecimal(maximumAcceptedValue), Sum(allocation));
    }

    [Fact]
    public void Decimal_max_value_is_accepted_without_overflow_or_precision_loss()
    {
        var result = SlittingEngineV2.Allocate(new SlittingMassInputV2(
            decimal.MaxValue,
            decimal.MaxValue,
            decimal.MaxValue,
            decimal.MaxValue));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        Assert.Equal(ExactMassV2.FromDecimal(decimal.MaxValue), result.Value!.BandKg);
        Assert.Equal(new ExactMassV2(0, 1), result.Value.TrimKg);
        Assert.Equal(new ExactMassV2(0, 1), result.Value.NetRemainingKg);
        Assert.Equal(ExactMassV2.FromDecimal(decimal.MaxValue), Sum(result.Value));
    }

    [Fact]
    public void Exact_mass_is_reduced_sign_normalized_and_value_equal()
    {
        Assert.Equal(new ExactMassV2(1, 2), new ExactMassV2(2, 4));
        Assert.Equal(new ExactMassV2(-1, 2), new ExactMassV2(1, -2));
        Assert.Equal(new ExactMassV2(0, 1), new ExactMassV2(0, -999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExactMassV2(1, 0));
    }

    [Fact]
    public void Same_input_returns_the_same_exact_allocation()
    {
        var input = new SlittingMassInputV2(28750.125m, 1500m, 1480m, 1320m);

        var first = SlittingEngineV2.Allocate(input);
        var second = SlittingEngineV2.Allocate(input);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("1", "1", "1", "1")]
    [InlineData("1000", "3", "2", "1")]
    [InlineData("28750.125", "1500", "1480", "1320")]
    [InlineData("999999.999", "2500.125", "2400.125", "1.001")]
    public void Every_computed_allocation_conserves_the_physical_mass_exactly(
        string physicalKgText,
        string physicalWidthText,
        string netWidthText,
        string usedWidthText)
    {
        static decimal Value(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        var result = SlittingEngineV2.Allocate(new SlittingMassInputV2(
            Value(physicalKgText),
            Value(physicalWidthText),
            Value(netWidthText),
            Value(usedWidthText)));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        var allocation = result.Value!;
        Assert.Equal(ExactMassV2.FromDecimal(Value(physicalKgText)), Sum(allocation));
        Assert.True(allocation.BandKg.Numerator.Sign > 0);
        Assert.True(allocation.TrimKg.Numerator.Sign >= 0);
        Assert.True(allocation.NetRemainingKg.Numerator.Sign >= 0);
    }

    private static ExactMassV2 Sum(SlittingMassAllocationV2 allocation) =>
        allocation.BandKg + allocation.TrimKg + allocation.NetRemainingKg;
}
