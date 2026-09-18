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
    public void Units_never_mix_and_subtraction_cannot_go_negative()
    {
        var kg = Quantity.Of(4m, QuantityUnit.Kilogram);
        Assert.Throws<InvalidOperationException>(() => kg.Add(Quantity.Of(1m, QuantityUnit.Piece)));
        Assert.Throws<InvalidOperationException>(() => kg.Subtract(Quantity.Of(4.001m, QuantityUnit.Kilogram)));
        Assert.True(kg.Subtract(kg).IsZero);
    }
}
