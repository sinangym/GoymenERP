using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// F-004 (BS-9/BS-13/BS-16/BS-17; URN-D7…D9): the unit conversion policy's parsers are lossless and closed — a value is
/// accepted exactly as written or refused, never rounded, clamped or defaulted — and its code lists map one-to-one onto the
/// typed answer. The numbers below are parser inputs, not policy values.
/// </summary>
public sealed class UnitConversionPolicyRulesV1Tests
{
    [Theory]
    [InlineData("7.85", "7.85")]
    [InlineData("0.5", "0.5")]
    [InlineData("12", "12")]
    [InlineData("999999.999999", "999999.999999")]
    public void A_density_is_accepted_exactly_as_written(string text, string expected)
    {
        Assert.True(UnitConversionPolicyRulesV1.TryParseDensity(text, out var value));
        Assert.Equal(expected, UnitConversionPolicyRulesV1.Format(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0.000")]
    [InlineData("7,85")]
    [InlineData(" 7.85")]
    [InlineData("7.85 ")]
    [InlineData("+7.85")]
    [InlineData("-7.85")]
    [InlineData("07.85")]
    [InlineData("7.")]
    [InlineData(".85")]
    [InlineData("7.8500001")]
    [InlineData("1000000")]
    [InlineData("7e0")]
    public void A_density_that_is_not_lossless_positive_decimal_text_is_refused(string? text)
    {
        Assert.False(UnitConversionPolicyRulesV1.TryParseDensity(text, out var value));
        Assert.Equal(0m, value);
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("0", "0")]
    [InlineData("100", "100")]
    [InlineData("0.0125", "0.0125")]
    public void A_tolerance_percent_inside_the_bounds_is_accepted_exactly(string text, string expected)
    {
        Assert.True(UnitConversionPolicyRulesV1.TryParseTolerancePercent(text, out var value));
        Assert.Equal(expected, UnitConversionPolicyRulesV1.Format(value));
    }

    [Theory]
    [InlineData("100.0001")]
    [InlineData("1000")]
    [InlineData("0.00001")]
    [InlineData("-1")]
    [InlineData("1,5")]
    [InlineData("%1")]
    public void A_tolerance_percent_outside_the_bounds_or_shape_is_refused(string text)
    {
        Assert.False(UnitConversionPolicyRulesV1.TryParseTolerancePercent(text, out _));
    }

    [Fact]
    public void Rounding_decimals_are_the_technical_range_only()
    {
        Assert.True(UnitConversionPolicyRulesV1.IsRoundingDecimals(0));
        Assert.True(UnitConversionPolicyRulesV1.IsRoundingDecimals(UnitConversionPolicyInputLimitsV1.MaximumRoundingDecimals));
        Assert.False(UnitConversionPolicyRulesV1.IsRoundingDecimals(null));
        Assert.False(UnitConversionPolicyRulesV1.IsRoundingDecimals(-1));
        Assert.False(UnitConversionPolicyRulesV1.IsRoundingDecimals(UnitConversionPolicyInputLimitsV1.MaximumRoundingDecimals + 1));
    }

    [Fact]
    public void Every_published_rounding_code_maps_to_the_midpoint_rounding_of_the_same_name_and_nothing_else_parses()
    {
        Assert.Equal(Enum.GetNames<MidpointRounding>().Order(StringComparer.Ordinal),
            UnitConversionRoundingModesV1.All.Order(StringComparer.Ordinal));
        foreach (var code in UnitConversionRoundingModesV1.All)
        {
            Assert.True(UnitConversionPolicyRulesV1.TryParseRounding(code, out var mode));
            Assert.Equal(code, mode.ToString());
        }

        Assert.False(UnitConversionPolicyRulesV1.TryParseRounding(null, out _));
        Assert.False(UnitConversionPolicyRulesV1.TryParseRounding("toeven", out _));
        Assert.False(UnitConversionPolicyRulesV1.TryParseRounding("0", out _));
    }

    [Fact]
    public void Boundary_and_base_codes_map_one_to_one_and_unknown_codes_are_refused()
    {
        Assert.Equal(UnitConversionToleranceBoundariesV1.All.Count, Enum.GetValues<SettingsWeighbridgeToleranceBoundaryV1>().Length);
        foreach (var code in UnitConversionToleranceBoundariesV1.All)
        {
            Assert.True(UnitConversionPolicyRulesV1.TryParseBoundary(code, out var boundary));
            Assert.Equal(code, boundary.ToString());
        }

        Assert.Equal(UnitConversionToleranceBasesV1.All.Count, Enum.GetValues<SettingsWeighbridgeToleranceBaseV1>().Length);
        foreach (var code in UnitConversionToleranceBasesV1.All)
        {
            Assert.True(UnitConversionPolicyRulesV1.TryParseBase(code, out var toleranceBase));
            Assert.Equal(code, toleranceBase.ToString());
        }

        Assert.False(UnitConversionPolicyRulesV1.TryParseBoundary("inclusive", out _));
        Assert.False(UnitConversionPolicyRulesV1.TryParseBoundary(null, out _));
        Assert.False(UnitConversionPolicyRulesV1.TryParseBase("Kg", out _));
        Assert.False(UnitConversionPolicyRulesV1.TryParseBase(string.Empty, out _));
    }

    [Theory]
    [InlineData("7.850000", "7.85")]
    [InlineData("1.0000", "1")]
    [InlineData("0.000100", "0.0001")]
    public void A_stored_fixed_scale_value_reads_back_as_its_canonical_text(string stored, string expected)
    {
        var value = decimal.Parse(stored, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, UnitConversionPolicyRulesV1.Format(value));
        Assert.Equal(expected, UnitConversionPolicyRulesV1.Format(UnitConversionPolicyRulesV1.Canonical(value)));
        Assert.Equal(value, UnitConversionPolicyRulesV1.Canonical(value));
    }
}
