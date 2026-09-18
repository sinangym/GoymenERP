using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class QuantityTests
{
    [Theory]
    [InlineData("12.5")]
    [InlineData("12.500")]
    [InlineData("0.001")]
    [InlineData("1000")]
    [InlineData("12.5000")]
    public void Kilograms_keep_up_to_three_decimals_exactly(string text)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var quantity = Quantity.Of(value, QuantityUnit.Kilogram);
        Assert.Equal(value, quantity.Value);
        Assert.True(quantity.Value.Scale <= Quantity.KilogramScale);
    }

    [Theory]
    [InlineData("12.5001")]
    [InlineData("0.0001")]
    public void A_fourth_kilogram_decimal_is_refused_not_rounded(string text)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentException>(() => Quantity.Of(value, QuantityUnit.Kilogram));
    }

    [Fact]
    public void Pieces_are_whole_numbers()
    {
        Assert.Equal(100m, Quantity.Of(100.00m, QuantityUnit.Piece).Value);
        Assert.Throws<ArgumentException>(() => Quantity.Of(100.5m, QuantityUnit.Piece));
    }

    [Fact]
    public void Negative_and_zero_where_positive_is_required_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Quantity.Of(-1m, QuantityUnit.Kilogram));
        Assert.Throws<ArgumentOutOfRangeException>(() => Quantity.Positive(0m, QuantityUnit.Kilogram));
        Assert.True(Quantity.Zero(QuantityUnit.Piece).IsZero);
    }

    [Fact]
    public void Units_never_mix()
    {
        var kg = Quantity.Of(1m, QuantityUnit.Kilogram);
        var piece = Quantity.Of(1m, QuantityUnit.Piece);
        Assert.Throws<InvalidOperationException>(() => kg.Add(piece));
        Assert.Throws<InvalidOperationException>(() => kg.Subtract(piece));
        Assert.Throws<InvalidOperationException>(() => kg.IsGreaterThan(piece));
    }

    [Fact]
    public void Subtracting_more_than_held_is_refused()
    {
        var held = Quantity.Of(40m, QuantityUnit.Kilogram);
        Assert.Throws<InvalidOperationException>(() => held.Subtract(Quantity.Of(40.001m, QuantityUnit.Kilogram)));
        Assert.Equal(0m, held.Subtract(held).Value);
    }

    [Fact]
    public void Equal_values_with_different_scales_are_the_same_quantity()
    {
        Assert.Equal(Quantity.Of(12.5m, QuantityUnit.Kilogram), Quantity.Of(12.500m, QuantityUnit.Kilogram));
    }
}
