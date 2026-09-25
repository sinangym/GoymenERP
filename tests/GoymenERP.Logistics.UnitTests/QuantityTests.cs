using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Quantities;

namespace GoymenERP.Logistics.UnitTests;

public sealed class QuantityTests
{
    [Theory]
    [InlineData("3800")]
    [InlineData("0.2")]
    [InlineData("12.500")]
    public void Kilograms_keep_up_to_three_decimals_exactly(string text)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(value, Quantity.Of(value, QuantityUnit.Kilogram).Value);
    }

    [Fact]
    public void A_fourth_kilogram_decimal_and_a_fractional_piece_are_refused_not_rounded()
    {
        Assert.Throws<ArgumentException>(() => Quantity.Of(12.5001m, QuantityUnit.Kilogram));
        Assert.Throws<ArgumentException>(() => Quantity.Of(1.5m, QuantityUnit.Piece));
        Assert.Throws<ArgumentOutOfRangeException>(() => Quantity.Of(-1m, QuantityUnit.Kilogram));
    }

    [Fact]
    public void Units_never_mix_and_subtraction_cannot_go_negative_by_code()
    {
        var kg = Quantity.Of(4m, QuantityUnit.Kilogram);
        Assert.Equal(LogisticsRuleCodes.UnitMismatch,
            Assert.Throws<LogisticsRuleException>(() => kg.Add(Quantity.Of(1m, QuantityUnit.Piece))).Code);
        Assert.Equal(LogisticsRuleCodes.QuantityBelowZero,
            Assert.Throws<LogisticsRuleException>(() => kg.Subtract(Quantity.Of(4.001m, QuantityUnit.Kilogram))).Code);
        Assert.True(kg.Subtract(kg).IsZero);
    }

    [Fact]
    public void Equal_values_with_different_scales_are_the_same_quantity_and_print_the_same()
    {
        Assert.Equal(Quantity.Of(12.5m, QuantityUnit.Kilogram), Quantity.Of(12.500m, QuantityUnit.Kilogram));
        Assert.Equal("12.5 Kilogram", Quantity.Of(12.500m, QuantityUnit.Kilogram).ToString());
        Assert.Equal(1, Quantity.SignificantDecimals(12.5000m));
        Assert.Equal(4, Quantity.SignificantDecimals(12.5001m));
    }
}
