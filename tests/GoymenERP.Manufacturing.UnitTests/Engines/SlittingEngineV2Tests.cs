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
                25300m,
                383.33333333333333333333333333m,
                3066.6666666666666666666666667m),
            result.Value);
        Assert.Equal(28750m, result.Value!.BandKg + result.Value.TrimKg + result.Value.NetRemainingKg);
    }

    [Fact]
    public void Zero_trim_keeps_the_common_result_while_v1_remains_historical()
    {
        var v2 = SlittingEngineV2.Allocate(new SlittingMassInputV2(28750m, 1500m, 1500m, 1320m)).Value!;
        var v1 = SlittingEngineV1.Plan(new SlittingPlanInputV1(28750m, 1500m,
            [new SlitLineInputV1(150m, 4), new SlitLineInputV1(240m, 3)])).Value!;

        Assert.Equal((25300m, 0m, 3450m), (v2.BandKg, v2.TrimKg, v2.NetRemainingKg));
        Assert.Equal((v1.PlanKg, v1.RemainingKg), (v2.BandKg, v2.NetRemainingKg));
        Assert.Equal("GDC-SLT/1 (master v3 §28.4)", SlittingEngineV1.FormulaVersion);
    }

    [Fact]
    public void Trimmed_physical_width_semantics_stay_distinct_from_v1_net_width_semantics()
    {
        var v2 = SlittingEngineV2.Allocate(new SlittingMassInputV2(28750m, 1500m, 1480m, 1320m)).Value!;
        var v1 = SlittingEngineV1.Plan(new SlittingPlanInputV1(28750m, 1480m,
            [new SlitLineInputV1(1320m, 1)])).Value!;

        Assert.Equal(25300m, v2.BandKg);
        Assert.Equal(28750m * 1320m / 1480m, v1.PlanKg);
        Assert.NotEqual(v1.PlanKg, v2.BandKg);
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
    public void Repeating_fraction_components_follow_each_locked_formula_and_conserve_exactly()
    {
        var result = SlittingEngineV2.Allocate(new SlittingMassInputV2(10m, 6m, 2m, 1m));

        Assert.Equal(EngineOutcome.Computed, result.Outcome);
        var allocation = result.Value!;
        Assert.Equal(10m * 1m / 6m, allocation.BandKg);
        Assert.Equal(10m * 4m / 6m, allocation.TrimKg);
        Assert.Equal(10m * 1m / 6m, allocation.NetRemainingKg);
        Assert.Equal(10m, allocation.BandKg + allocation.TrimKg + allocation.NetRemainingKg);
        Assert.All([allocation.BandKg, allocation.TrimKg, allocation.NetRemainingKg], value => Assert.True(value >= 0m));
    }

    [Fact]
    public void Used_width_equal_to_net_width_never_creates_a_negative_decimal_residue()
    {
        var allocation = SlittingEngineV2.Allocate(new SlittingMassInputV2(10m, 9m, 8m, 8m)).Value!;

        Assert.Equal(10m * 8m / 9m, allocation.BandKg);
        Assert.Equal(10m * 1m / 9m, allocation.TrimKg);
        Assert.Equal(0m, allocation.NetRemainingKg);
        Assert.Equal(10m, allocation.BandKg + allocation.TrimKg + allocation.NetRemainingKg);
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
        Assert.Equal((maximumAcceptedValue, 0m, 0m),
            (allocation.BandKg, allocation.TrimKg, allocation.NetRemainingKg));
        Assert.Equal(maximumAcceptedValue, allocation.BandKg + allocation.TrimKg + allocation.NetRemainingKg);
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
        Assert.Equal(Value(physicalKgText), allocation.BandKg + allocation.TrimKg + allocation.NetRemainingKg);
        Assert.True(allocation.BandKg > 0m);
        Assert.True(allocation.TrimKg >= 0m);
        Assert.True(allocation.NetRemainingKg >= 0m);
    }
}
