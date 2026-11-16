using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

public sealed class CollateralTypeRulesV1Tests
{
    [Theory]
    [InlineData("0", "0")]
    [InlineData("100.000", "100")]
    [InlineData("0005.2500", "5.25")]
    [InlineData("99.99999999999999999999999999999999999999999999999999999999999", "99.99999999999999999999999999999999999999999999999999999999999")]
    public void Rates_are_exact_normalized_and_never_rounded(string value, string expected)
    {
        Assert.True(CollateralTypeRulesV1.TryRate(value, out var rate));
        Assert.Equal(expected, rate);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" ")] [InlineData("-1")] [InlineData("+1")]
    [InlineData("100.0000000000000000000000000000000000000000000000000000000001")]
    [InlineData("101")] [InlineData("1e1")] [InlineData("1,5")] [InlineData(".5")] [InlineData("5.")] [InlineData("1.2.3")]
    public void Invalid_or_out_of_range_rates_are_refused(string? value) => Assert.False(CollateralTypeRulesV1.TryRate(value, out _));

    [Fact]
    public void Only_the_six_decided_collateral_classes_are_eligible()
    {
        Assert.Equal(6, Enum.GetValues<SettingsCollateralKindV1>().Length);
        Assert.All(Enum.GetValues<SettingsCollateralKindV1>(), kind => Assert.True(CollateralTypeRulesV1.IsKind(kind)));
        Assert.False(CollateralTypeRulesV1.IsKind(null));
        Assert.False(CollateralTypeRulesV1.IsKind((SettingsCollateralKindV1)0));
        Assert.False(CollateralTypeRulesV1.IsKind((SettingsCollateralKindV1)7));
    }
}
