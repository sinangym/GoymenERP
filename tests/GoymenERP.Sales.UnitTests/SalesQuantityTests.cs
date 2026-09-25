using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Quantities;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesQuantityTests
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
    public void Units_never_mix_and_say_so_by_code()
    {
        var kg = Quantity.Of(1m, QuantityUnit.Kilogram);
        var piece = Quantity.Of(1m, QuantityUnit.Piece);
        Assert.Equal(SalesRuleCodes.UnitMismatch, Assert.Throws<SalesRuleException>(() => kg.Add(piece)).Code);
        Assert.Equal(SalesRuleCodes.UnitMismatch, Assert.Throws<SalesRuleException>(() => kg.Subtract(piece)).Code);
        Assert.Equal(SalesRuleCodes.UnitMismatch, Assert.Throws<SalesRuleException>(() => kg.IsGreaterThan(piece)).Code);
    }

    [Fact]
    public void Subtracting_more_than_held_is_refused_by_code()
    {
        var held = Quantity.Of(40m, QuantityUnit.Kilogram);
        Assert.Equal(SalesRuleCodes.QuantityBelowZero,
            Assert.Throws<SalesRuleException>(() => held.Subtract(Quantity.Of(40.001m, QuantityUnit.Kilogram))).Code);
        Assert.Equal(0m, held.Subtract(held).Value);
    }

    [Fact]
    public void Equal_values_with_different_scales_are_the_same_quantity_and_print_the_same()
    {
        var a = Quantity.Of(12.5m, QuantityUnit.Kilogram);
        var b = Quantity.Of(12.500m, QuantityUnit.Kilogram);
        Assert.Equal(a, b);
        Assert.Equal("12.5 Kilogram", b.ToString());
    }

    [Theory]
    [InlineData("12.5000", 1)]
    [InlineData("12.5001", 4)]
    [InlineData("1000", 0)]
    [InlineData("0.001", 3)]
    public void Significant_decimals_ignore_trailing_zeros(string text, int expected)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, Quantity.SignificantDecimals(value));
    }
}
