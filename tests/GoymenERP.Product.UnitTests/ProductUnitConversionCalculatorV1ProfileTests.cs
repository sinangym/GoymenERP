using GoymenERP.Modules.Product.Domain.UnitConversion;
using Xunit;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductUnitConversionCalculatorV1ProfileTests
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
    public void A_round_tube_of_60_3mm_by_3mm_at_density_7_85_weighs_4_239_kg_per_metre_after_coefficient_rounding()
    {
        var result = ProductUnitConversionCalculatorV1.RoundTubeKgPerMetre(60.3m, 3m, Steel);

        Assert.Equal(UnitConversionOutcomeV1.Computed, result.Outcome);
        Assert.Equal(4.2393122506m, decimal.Round(result.Value!.Value, 10));
        Assert.Equal(4.239m, ProductUnitConversionCalculatorV1.RoundCoefficient(result.Value.Value, Settings));
    }

    [Fact]
    public void A_wall_of_exactly_half_the_diameter_is_a_solid_bar_and_a_thicker_wall_is_refused()
    {
        var solid = ProductUnitConversionCalculatorV1.RoundTubeKgPerMetre(10m, 5m, Steel);
        var impossible = ProductUnitConversionCalculatorV1.RoundTubeKgPerMetre(10m, 5.001m, Steel);

        Assert.Equal(UnitConversionOutcomeV1.Computed, solid.Outcome);
        Assert.Equal(0.6165375583m, decimal.Round(solid.Value!.Value, 10));
        Assert.Equal(UnitConversionOutcomeV1.Refused, impossible.Outcome);
        Assert.Null(impossible.Value);
    }

    [Theory]
    [InlineData(null, 3.0, true)]
    [InlineData(60.3, null, true)]
    [InlineData(60.3, 3.0, false)]
    public void A_missing_round_tube_input_is_NoSource_never_zero(double? outer, double? wall, bool withDensity)
    {
        var result = ProductUnitConversionCalculatorV1.RoundTubeKgPerMetre(
            (decimal?)outer,
            (decimal?)wall,
            withDensity ? Steel : null);

        Assert.Equal(UnitConversionOutcomeV1.NoSource, result.Outcome);
        Assert.Null(result.Value);
    }

    [Fact]
    public void A_non_positive_or_uninitialised_round_tube_input_is_refused()
    {
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.RoundTubeKgPerMetre(0m, 3m, Steel).Outcome);
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.RoundTubeKgPerMetre(60.3m, -1m, Steel).Outcome);
        Assert.Equal(UnitConversionOutcomeV1.Refused, ProductUnitConversionCalculatorV1.RoundTubeKgPerMetre(60.3m, 3m, default(DensityV1)).Outcome);
    }

    [Fact]
    public void A_huge_round_tube_input_is_refused_as_overflow_not_thrown()
    {
        var result = ProductUnitConversionCalculatorV1.RoundTubeKgPerMetre(decimal.MaxValue, decimal.MaxValue / 4m, Steel);

        Assert.Equal(UnitConversionOutcomeV1.Refused, result.Outcome);
        Assert.Null(result.Value);
    }

    [Fact]
    public void A_profile_piece_of_6000mm_at_4_239_kg_per_metre_weighs_25_434_kg_and_chains_to_whole_units()
    {
        var piece = ProductUnitConversionCalculatorV1.ProfileKgPerPiece(4.239m, 6000m);

        Assert.Equal(UnitConversionOutcomeV1.Computed, piece.Outcome);
        Assert.Equal(25.434m, piece.Value);

        var units = ProductUnitConversionCalculatorV1.KgToWholeUnits(100m, piece.Value, Settings);
        Assert.Equal(UnitConversionOutcomeV1.Computed, units.Outcome);
        Assert.Equal(3L, units.Value);
    }

    [Theory]
    [InlineData(null, 6000.0, false)]
    [InlineData(4.239, null, false)]
    [InlineData(0.0, 6000.0, true)]
    [InlineData(4.239, -1.0, true)]
    public void A_missing_profile_piece_input_is_NoSource_and_a_non_positive_one_is_refused(double? kgPerMetre, double? lengthMm, bool refused)
    {
        var result = ProductUnitConversionCalculatorV1.ProfileKgPerPiece((decimal?)kgPerMetre, (decimal?)lengthMm);

        Assert.Equal(refused ? UnitConversionOutcomeV1.Refused : UnitConversionOutcomeV1.NoSource, result.Outcome);
        Assert.Null(result.Value);
    }
}
