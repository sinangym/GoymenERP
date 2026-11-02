using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// The exchange-rate record keys rates by ISO 4217 code and maps a code to its currency identity by the documented Settings
/// derivation (UUIDv5, namespace money-profiles, name "currency:CODE"); for TRY it must be exactly the published identity.
/// </summary>
public sealed class ExchangeRateCurrencyIdentitiesTests
{
    [Fact]
    public void The_derivation_reproduces_the_published_turkish_lira_identity()
    {
        Assert.Equal(SettingsCurrencyIdentitiesV1.TurkishLira, ExchangeRateCurrencyIdentitiesV1.ForIsoCode("TRY"));
    }

    [Fact]
    public void Codes_have_distinct_stable_version_5_identities()
    {
        var usd = ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD");
        Assert.Equal(usd, ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD"));
        Assert.NotEqual(usd, ExchangeRateCurrencyIdentitiesV1.ForIsoCode("EUR"));
        Assert.Equal(5, usd.Version);
    }

    [Theory]
    [InlineData("usd")]
    [InlineData("US")]
    [InlineData("USDT")]
    [InlineData("U5D")]
    [InlineData("")]
    public void Only_three_upper_case_letters_are_codes(string code)
    {
        Assert.False(ExchangeRateCurrencyIdentitiesV1.IsIsoCode(code));
        Assert.Throws<ArgumentException>(() => ExchangeRateCurrencyIdentitiesV1.ForIsoCode(code));
    }

    [Fact]
    public void The_permission_names_are_the_registered_literals()
    {
        Assert.Equal("settings.exchange-rates.read", SettingsExchangeRatePermissionManifestV1.Read);
        Assert.Equal("settings.exchange-rates.correct", SettingsExchangeRatePermissionManifestV1.Correct);
        Assert.Equal("settings.exchange-rates.import", SettingsExchangeRatePermissionManifestV1.Import);
    }
}
