using GoymenERP.Modules.Product.Domain.UnitConversion;
using Xunit;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductUnitConversionCalculatorV1Tests
{
    private static readonly UnitConversionSettingsV1 Settings = new(
        3,
        2,
        1m,
        MidpointRounding.AwayFromZero,
        MidpointRounding.ToNegativeInfinity,
        WeighbridgeToleranceBoundaryV1.Inclusive,
        WeighbridgeToleranceBaseV1.Calculated);

    private static readonly DensityV1 Steel = DensityV1.FromGramsPerCubicCentimetre(7.85m);

    [Fact]
    public void A_sheet_of_2mm_by_1000mm_by_2000mm_at_density_7_85_weighs_31_4_kg()
    {
        var result = ProductUnitConversionCalculatorV1.SheetKgPerPiece(2m, 1000m, 2000m, Steel);

        Assert.Equal(UnitConversionOutcomeV1.Computed, result.Outcome);
        Assert.Equal(31.4m, result.Value);
    }

    [Fact]
    public void A_coil_of_2mm_by_1000mm_at_density_7_85_weighs_15_7_kg_per_metre()
    {
        var result = ProductUnitConversionCalculatorV1.CoilKgPerMetre(2m, 1000m, Steel);

        Assert.Equal(UnitConversionOutcomeV1.Computed, result.Outcome);
        Assert.Equal(15.7m, result.Value);
    }

    [Theory]
    [InlineData(null, 1000.0, 2000.0, 7.85)]
    [InlineData(2.0, null, 2000.0, 7.85)]
    [InlineData(2.0, 1000.0, null, 7.85)]
    [InlineData(2.0, 1000.0, 2000.0, null)]
    public void A_missing_sheet_input_is_NoSource_never_zero(double? t, double? w, double? l, double? d)
    {
        var result = ProductUnitConversionCalculatorV1.SheetKgPerPiece(ToDecimal(t), ToDecimal(w), ToDecimal(l), d is null ? null : DensityV1.FromGramsPerCubicCentimetre((decimal)d.Value));

        Assert.Equal(UnitConversionOutcomeV1.NoSource, result.Outcome);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData(0.0, 1000.0, 2000.0)]
    [InlineData(2.0, -1.0, 2000.0)]
    [InlineData(2.0, 1000.0, 0.0)]
    public void A_non_positive_sheet_input_is_refused(double t, double w, double l)
    {
        var result = ProductUnitConversionCalculatorV1.SheetKgPerPiece((decimal)t, (decimal)w, (decimal)l, Steel);

        Assert.Equal(UnitConversionOutcomeV1.Refused, result.Outcome);
        Assert.Null(result.Value);
    }

    [Fact]
    public void A_missing_or_non_positive_coil_input_is_NoSource_or_refused()
    {
        Assert.Equal(UnitConversionOutcomeV1.NoSource, ProductUnitConversionCalculatorV1.CoilKgPerMetre(null, 1000m, Steel).Outcome);
        Assert.Equal(UnitConversionOutcomeV1.NoSource, ProductUnitConversionCalculatorV1.CoilKgPerMetre(2m, null, Steel).Outcome);
        Assert.Equal(UnitConversionOutcomeV1.NoSource, ProductUnitConversionCalculatorV1.CoilKgPerMetre(2m, 1000m, null).Outcome);
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.CoilKgPerMetre(0m, 1000m, Steel).Outcome);
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.CoilKgPerMetre(2m, 0m, Steel).Outcome);
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.CoilKgPerMetre(-2m, 1000m, Steel).Outcome);
    }

    [Fact]
    public void Units_to_kg_keeps_full_precision_and_a_missing_coefficient_is_NoSource()
    {
        var computed = ProductUnitConversionCalculatorV1.UnitsToKg(3m, 1.23456m);

        Assert.Equal(3.70368m, computed.Value);
        Assert.Equal(UnitConversionOutcomeV1.NoSource, ProductUnitConversionCalculatorV1.UnitsToKg(3m, null).Outcome);
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.UnitsToKg(-1m, 1m).Outcome);
    }

    [Fact]
    public void Kg_to_whole_units_rounds_down_with_the_default_setting_and_never_up()
    {
        // 99.99 kg at 10 kg per piece is 9 pieces, not 10.
        var result = ProductUnitConversionCalculatorV1.KgToWholeUnits(99.99m, 10m, Settings);

        Assert.Equal(9L, result.Value);
        Assert.Equal(10L, ProductUnitConversionCalculatorV1.KgToWholeUnits(100m, 10m, Settings).Value);
        Assert.Equal(UnitConversionOutcomeV1.NoSource, ProductUnitConversionCalculatorV1.KgToWholeUnits(1m, null, Settings).Outcome);
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.KgToWholeUnits(1m, 0m, Settings).Outcome);
    }

    [Fact]
    public void Kg_to_whole_units_keeps_an_exact_count_and_follows_the_rounding_setting()
    {
        // 30 kg at 0.3 kg per piece is exactly 100: floor must not lose a piece.
        Assert.Equal(100L, ProductUnitConversionCalculatorV1.KgToWholeUnits(30m, 0.3m, Settings).Value);
        Assert.Equal(99L, ProductUnitConversionCalculatorV1.KgToWholeUnits(29.999999m, 0.3m, Settings).Value);
        var up = Settings with { WholeUnitRounding = MidpointRounding.ToPositiveInfinity };
        Assert.Equal(10L, ProductUnitConversionCalculatorV1.KgToWholeUnits(99.99m, 10m, up).Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductUnitConversionCalculatorV1.KgToWholeUnits(
            1m, 1m, Settings with { WholeUnitRounding = (MidpointRounding)99 }));
    }

    [Fact]
    public void Too_large_inputs_are_refused_not_thrown()
    {
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.SheetKgPerPiece(decimal.MaxValue, decimal.MaxValue, 1m, Steel).Outcome);
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.CoilKgPerMetre(decimal.MaxValue, decimal.MaxValue, Steel).Outcome);
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.UnitsToKg(decimal.MaxValue, 2m).Outcome);
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.KgToWholeUnits(decimal.MaxValue, 1m, Settings).Outcome);
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(decimal.MaxValue, 0.0001m, Settings).Outcome);
    }

    [Fact]
    public void Rounding_uses_only_the_company_settings_decimals()
    {
        Assert.Equal(1.235m, ProductUnitConversionCalculatorV1.RoundCoefficient(1.2345m, Settings));
        Assert.Equal(12.35m, ProductUnitConversionCalculatorV1.RoundDocumentTotal(12.345m, Settings));
        Assert.Equal(1.2m, ProductUnitConversionCalculatorV1.RoundCoefficient(1.2345m, Settings with { CoefficientDecimals = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductUnitConversionCalculatorV1.RoundCoefficient(1m, Settings with { CoefficientDecimals = 13 }));
    }

    [Fact]
    public void The_rounding_mode_comes_from_the_settings_and_an_unknown_mode_is_refused()
    {
        var toEven = Settings with { DisplayRounding = MidpointRounding.ToEven };
        Assert.Equal(12.34m, ProductUnitConversionCalculatorV1.RoundDocumentTotal(12.345m, toEven));
        Assert.Equal(12.35m, ProductUnitConversionCalculatorV1.RoundDocumentTotal(12.345m, Settings));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductUnitConversionCalculatorV1.RoundDocumentTotal(1m, Settings with { DisplayRounding = (MidpointRounding)99 }));
    }

    [Fact]
    public void An_inclusive_calculated_base_weighbridge_check_passes_at_the_tolerance_and_refuses_bad_inputs()
    {
        Assert.True(ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(101m, 100m, Settings).Value);
        Assert.True(ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(99m, 100m, Settings).Value);
        Assert.False(ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(101.01m, 100m, Settings).Value);
        Assert.True(ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(100.99m, 100m, Settings).Value);
        // A short weighing is compared by its absolute deviation (BS-17 signed formula, |deviation| <= tolerance).
        Assert.False(ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(98.99m, 100m, Settings).Value);
        Assert.True(ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(99.01m, 100m, Settings).Value);
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(100m, 0m, Settings).Outcome);
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(100m, 100m, Settings with { WeighbridgeTolerancePercent = -1m }).Outcome);
    }

    [Fact]
    public void An_exclusive_boundary_fails_a_deviation_equal_to_the_tolerance()
    {
        var exclusive = Settings with { ToleranceBoundary = WeighbridgeToleranceBoundaryV1.Exclusive };

        Assert.False(ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(101m, 100m, exclusive).Value);
        Assert.True(ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(100.99m, 100m, exclusive).Value);
    }

    [Fact]
    public void A_weighed_base_measures_the_deviation_against_the_weighed_kg_and_refuses_zero_weighed()
    {
        var weighed = Settings with { ToleranceBase = WeighbridgeToleranceBaseV1.Weighed };

        // 99 vs 100: 1/99 = 1.0101 percent of the weighed kg, over 1 percent; against the calculated kg it is exactly 1.
        Assert.False(ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(99m, 100m, weighed).Value);
        Assert.True(ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(99m, 100m, Settings).Value);
        // 101 vs 100: 1/101 = 0.990 percent of the weighed kg, inside.
        Assert.True(ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(101m, 100m, weighed).Value);
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(0m, 100m, weighed).Outcome);
    }

    [Fact]
    public void An_unknown_tolerance_boundary_or_base_is_refused_not_defaulted()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(
            100m, 100m, Settings with { ToleranceBoundary = (WeighbridgeToleranceBoundaryV1)0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(
            100m, 100m, Settings with { ToleranceBase = (WeighbridgeToleranceBaseV1)0 }));
    }

    [Fact]
    public void Density_in_kg_per_cubic_metre_converts_to_the_same_value_and_a_non_positive_density_is_refused()
    {
        var fromKgPerCubicMetre = DensityV1.FromKilogramsPerCubicMetre(7850m);

        Assert.Equal(Steel, fromKgPerCubicMetre);
        Assert.Equal(31.4m, ProductUnitConversionCalculatorV1.SheetKgPerPiece(2m, 1000m, 2000m, fromKgPerCubicMetre).Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => DensityV1.FromGramsPerCubicCentimetre(0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => DensityV1.FromKilogramsPerCubicMetre(-1m));
    }

    [Fact]
    public void A_default_density_that_bypassed_the_factory_is_refused_never_computed_as_zero_kg()
    {
        var uninitialised = default(DensityV1);

        var sheet = ProductUnitConversionCalculatorV1.SheetKgPerPiece(2m, 1000m, 2000m, uninitialised);
        var coil = ProductUnitConversionCalculatorV1.CoilKgPerMetre(2m, 1000m, new DensityV1());

        Assert.Equal(UnitConversionOutcomeV1.Refused, sheet.Outcome);
        Assert.Null(sheet.Value);
        Assert.Equal(UnitConversionOutcomeV1.Refused, coil.Outcome);
        Assert.Null(coil.Value);
    }

    [Fact]
    public void A_short_weighing_outside_the_tolerance_fails_on_the_weighed_base_too()
    {
        var weighed = Settings with { ToleranceBase = WeighbridgeToleranceBaseV1.Weighed };

        // 98 vs 100: 2/98 = 2.04 percent of the weighed kg; the absolute deviation fails a short weighing.
        var shortWeighing = ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(98m, 100m, weighed);

        Assert.Equal(UnitConversionOutcomeV1.Computed, shortWeighing.Outcome);
        Assert.False(shortWeighing.Value);
    }

    [Fact]
    public void A_one_third_coefficient_round_trips_units_to_kg_and_back_without_losing_a_unit()
    {
        var oneThird = 1m / 3m;

        var kg = ProductUnitConversionCalculatorV1.UnitsToKg(3m, oneThird);
        var units = ProductUnitConversionCalculatorV1.KgToWholeUnits(kg.Value!.Value, oneThird, Settings);
        var fromOneKg = ProductUnitConversionCalculatorV1.KgToWholeUnits(1m, oneThird, Settings);

        Assert.Equal(UnitConversionOutcomeV1.Computed, kg.Outcome);
        Assert.Equal(3L, units.Value);
        Assert.Equal(3L, fromOneKg.Value);
        Assert.Equal(0.333m, ProductUnitConversionCalculatorV1.RoundCoefficient(oneThird, Settings));
    }

    private static decimal? ToDecimal(double? value) => value is null ? null : (decimal)value.Value;
}
