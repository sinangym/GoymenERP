using GoymenERP.Modules.Product.Domain.UnitConversion;
using Xunit;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductUnitConversionCalculatorV1Tests
{
    private static readonly UnitConversionSettingsV1 Settings = new(3, 2, 1m);

    [Fact]
    public void A_sheet_of_2mm_by_1000mm_by_2000mm_at_density_7_85_weighs_31_4_kg()
    {
        var result = ProductUnitConversionCalculatorV1.SheetKgPerPiece(2m, 1000m, 2000m, 7.85m);

        Assert.Equal(UnitConversionOutcomeV1.Computed, result.Outcome);
        Assert.Equal(31.4m, result.Value);
    }

    [Fact]
    public void A_coil_of_2mm_by_1000mm_at_density_7_85_weighs_15_7_kg_per_metre()
    {
        var result = ProductUnitConversionCalculatorV1.CoilKgPerMetre(2m, 1000m, 7.85m);

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
        var result = ProductUnitConversionCalculatorV1.SheetKgPerPiece(ToDecimal(t), ToDecimal(w), ToDecimal(l), ToDecimal(d));

        Assert.Equal(UnitConversionOutcomeV1.NoSource, result.Outcome);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData(0.0, 1000.0, 2000.0, 7.85)]
    [InlineData(2.0, -1.0, 2000.0, 7.85)]
    [InlineData(2.0, 1000.0, 2000.0, 0.0)]
    public void A_non_positive_sheet_input_is_refused(double t, double w, double l, double d)
    {
        var result = ProductUnitConversionCalculatorV1.SheetKgPerPiece((decimal)t, (decimal)w, (decimal)l, (decimal)d);

        Assert.Equal(UnitConversionOutcomeV1.Refused, result.Outcome);
        Assert.Null(result.Value);
    }

    [Fact]
    public void A_missing_or_non_positive_coil_input_is_NoSource_or_refused()
    {
        Assert.Equal(UnitConversionOutcomeV1.NoSource, ProductUnitConversionCalculatorV1.CoilKgPerMetre(null, 1000m, 7.85m).Outcome);
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.CoilKgPerMetre(2m, 0m, 7.85m).Outcome);
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
    public void Kg_to_whole_units_rounds_down_and_never_up()
    {
        // 99.99 kg at 10 kg per piece is 9 pieces, not 10.
        var result = ProductUnitConversionCalculatorV1.KgToWholeUnits(99.99m, 10m);

        Assert.Equal(9L, result.Value);
        Assert.Equal(10L, ProductUnitConversionCalculatorV1.KgToWholeUnits(100m, 10m).Value);
        Assert.Equal(UnitConversionOutcomeV1.NoSource, ProductUnitConversionCalculatorV1.KgToWholeUnits(1m, null).Outcome);
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.KgToWholeUnits(1m, 0m).Outcome);
    }

    [Fact]
    public void Rounding_uses_only_the_company_settings_decimals()
    {
        Assert.Equal(1.235m, ProductUnitConversionCalculatorV1.RoundCoefficient(1.2345m, Settings));
        Assert.Equal(12.35m, ProductUnitConversionCalculatorV1.RoundDocumentTotal(12.345m, Settings));
        Assert.Equal(1.2m, ProductUnitConversionCalculatorV1.RoundCoefficient(1.2345m, new UnitConversionSettingsV1(1, 2, 1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductUnitConversionCalculatorV1.RoundCoefficient(1m, new UnitConversionSettingsV1(13, 2, 1m)));
    }

    [Fact]
    public void The_weighbridge_check_is_inclusive_at_the_tolerance_and_refuses_bad_inputs()
    {
        Assert.True(ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(101m, 100m, Settings).Value);
        Assert.True(ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(99m, 100m, Settings).Value);
        Assert.False(ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(101.01m, 100m, Settings).Value);
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(100m, 0m, Settings).Outcome);
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(100m, 100m, new UnitConversionSettingsV1(3, 2, -1m)).Outcome);
    }

    private static decimal? ToDecimal(double? value) => value is null ? null : (decimal)value.Value;
}
