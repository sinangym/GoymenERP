using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Quantities;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>Ö2: kg at most 3 decimals; a 4th decimal is refused, never silently cut.</summary>
public sealed class KilogramsTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("10000")]
    [InlineData("0.001")]
    [InlineData("9999.999")]
    [InlineData("123456789.125")]
    public void Up_to_three_decimals_is_kept_exactly(string text)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(value, Kilograms.Of(value).Value);
    }

    [Theory]
    [InlineData("0.0001")]
    [InlineData("1.2345")]
    [InlineData("9999.9995")]
    [InlineData("0.0009")]
    public void A_fourth_decimal_is_refused_not_rounded_or_truncated(string text)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var error = Assert.Throws<InventoryRuleException>(() => Kilograms.Of(value));
        Assert.Equal(InventoryRuleCodes.KilogramPrecisionExceeded, error.Code);
    }

    [Fact]
    public void Trailing_zeros_are_not_extra_precision()
    {
        // 1.2000 is exactly 1.2: accepted, and shown without the padding zeros.
        var kilograms = Kilograms.Of(1.2000m);
        Assert.Equal(1.2m, kilograms.Value);
        Assert.Equal("1.2", kilograms.ToString());
        Assert.Equal(Kilograms.Of(1.2m), kilograms);
    }

    [Fact]
    public void Negative_is_refused_and_a_movement_quantity_must_be_positive()
    {
        Assert.Equal(InventoryRuleCodes.QuantityNotPositive,
            Assert.Throws<InventoryRuleException>(() => Kilograms.Of(-0.001m)).Code);
        Assert.Equal(InventoryRuleCodes.QuantityNotPositive,
            Assert.Throws<InventoryRuleException>(() => Kilograms.Positive(0m)).Code);
        Assert.Equal(0.001m, Kilograms.Positive(0.001m).Value);
    }

    [Fact]
    public void Sums_of_exact_values_stay_exact()
    {
        // Three decimals added never gain a fourth; decimal arithmetic, never a double.
        var total = Kilograms.Of(0.1m) + Kilograms.Of(0.2m) + Kilograms.Of(9999.997m);
        Assert.Equal(10000.297m, total.Value);
    }
}
