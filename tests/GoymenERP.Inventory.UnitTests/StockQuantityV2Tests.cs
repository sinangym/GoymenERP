using GoymenERP.Modules.Inventory.Domain.Quantities;

namespace GoymenERP.Inventory.UnitTests;

public sealed class StockQuantityV2Tests
{
    private static readonly Guid UnitId = Guid.Parse("95000000-0000-4000-8000-000000000001");
    private static readonly Guid ProfileId = Guid.Parse("95000000-0000-4000-8000-000000000002");

    [Theory]
    [InlineData("12.345", StockQuantityDimensionV2.Mass, 3)]
    [InlineData("100.25", StockQuantityDimensionV2.Volume, 2)]
    [InlineData("7", StockQuantityDimensionV2.Count, 0)]
    public void Positive_quantity_keeps_value_and_profile_context_exactly(
        string text, StockQuantityDimensionV2 dimension, int scale)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var quantity = StockQuantityV2.Positive(value, UnitId, ProfileId, 7, dimension, scale);

        Assert.Equal(value, quantity.Value);
        Assert.Equal(UnitId, quantity.UnitId);
        Assert.Equal(ProfileId, quantity.ProfileId);
        Assert.Equal(7, quantity.ProfileVersion);
        Assert.Equal(dimension, quantity.Dimension);
        Assert.Equal(scale, quantity.Scale);
        Assert.Equal(text, quantity.ToString());
    }

    [Fact]
    public void Trailing_zeroes_are_canonical_not_extra_scale()
    {
        var quantity = StockQuantityV2.Positive(12.3000m, UnitId, ProfileId, 1,
            StockQuantityDimensionV2.Volume, 1);

        Assert.Equal(12.3m, quantity.Value);
        Assert.Equal("12.3", quantity.ToString());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.1")]
    public void Quantity_must_be_positive(string text)
    {
        var error = Assert.Throws<StockQuantityRuleExceptionV2>(() => Quantity(
            decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(StockQuantityRuleCodesV2.QuantityNotPositive, error.Code);
    }

    [Fact]
    public void Profile_scale_is_enforced_without_rounding_or_truncation()
    {
        var error = Assert.Throws<StockQuantityRuleExceptionV2>(() => Quantity(1.234m, scale: 2));

        Assert.Equal(StockQuantityRuleCodesV2.ScaleExceeded, error.Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(29)]
    public void Profile_scale_must_be_a_decimal_scale(int scale)
    {
        var error = Assert.Throws<StockQuantityRuleExceptionV2>(() => Quantity(1m, scale: scale));

        Assert.Equal(StockQuantityRuleCodesV2.InvalidScale, error.Code);
    }

    [Fact]
    public void Unit_and_profile_identifiers_are_required()
    {
        var missingUnit = Assert.Throws<StockQuantityRuleExceptionV2>(() =>
            StockQuantityV2.Positive(1m, Guid.Empty, ProfileId, 1, StockQuantityDimensionV2.Mass, 3));
        var missingProfile = Assert.Throws<StockQuantityRuleExceptionV2>(() =>
            StockQuantityV2.Positive(1m, UnitId, Guid.Empty, 1, StockQuantityDimensionV2.Mass, 3));

        Assert.Equal(StockQuantityRuleCodesV2.InvalidReference, missingUnit.Code);
        Assert.Equal(StockQuantityRuleCodesV2.InvalidReference, missingProfile.Code);
    }

    [Fact]
    public void Profile_version_must_be_positive()
    {
        var error = Assert.Throws<StockQuantityRuleExceptionV2>(() =>
            StockQuantityV2.Positive(1m, UnitId, ProfileId, 0, StockQuantityDimensionV2.Mass, 3));

        Assert.Equal(StockQuantityRuleCodesV2.InvalidProfile, error.Code);
    }

    [Fact]
    public void Undefined_dimension_is_rejected()
    {
        var error = Assert.Throws<StockQuantityRuleExceptionV2>(() =>
            StockQuantityV2.Positive(1m, UnitId, ProfileId, 1, (StockQuantityDimensionV2)0, 3));

        Assert.Equal(StockQuantityRuleCodesV2.InvalidDimension, error.Code);
    }

    [Fact]
    public void Same_context_quantities_add_exactly()
    {
        var sum = StockQuantityV2.Add(Quantity(0.1m), Quantity(0.2m));

        Assert.Equal(0.3m, sum.Value);
    }

    [Theory]
    [InlineData("unit")]
    [InlineData("profile")]
    [InlineData("version")]
    [InlineData("dimension")]
    [InlineData("scale")]
    public void Aggregation_rejects_any_context_or_dimension_mismatch(string mismatch)
    {
        var left = Quantity(1m);
        var right = StockQuantityV2.Positive(2m,
            mismatch == "unit" ? Guid.NewGuid() : UnitId,
            mismatch == "profile" ? Guid.NewGuid() : ProfileId,
            mismatch == "version" ? 2 : 1,
            mismatch == "dimension" ? StockQuantityDimensionV2.Volume : StockQuantityDimensionV2.Mass,
            mismatch == "scale" ? 2 : 3);

        var error = Assert.Throws<StockQuantityRuleExceptionV2>(() => StockQuantityV2.Add(left, right));
        Assert.Equal(StockQuantityRuleCodesV2.ContextMismatch, error.Code);
    }

    [Fact]
    public void Existing_kilogram_contract_remains_stricter_and_unchanged()
    {
        Assert.Equal(12.345m, Kilograms.Positive(12.345m).Value);
        Assert.Equal(InventoryRuleCodes.KilogramPrecisionExceeded,
            Assert.Throws<InventoryRuleException>(() => Kilograms.Positive(12.3456m)).Code);

        var volume = StockQuantityV2.Positive(12.3456m, UnitId, ProfileId, 1,
            StockQuantityDimensionV2.Volume, 4);
        Assert.Equal(12.3456m, volume.Value);
    }

    private static StockQuantityV2 Quantity(decimal value, int scale = 3) =>
        StockQuantityV2.Positive(value, UnitId, ProfileId, 1, StockQuantityDimensionV2.Mass, scale);
}
