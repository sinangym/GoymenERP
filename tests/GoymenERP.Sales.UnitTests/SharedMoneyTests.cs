using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// TEK-01: the shared <see cref="Money"/> and <see cref="ExchangeRate"/> value objects. The scale is always
/// the caller's (the Settings money profile's); these tests pass it explicitly. SENTETIK identities.
/// </summary>
public sealed class SharedMoneyTests
{
    private static readonly Guid Try = Guid.Parse("10000000-0000-4000-8000-0000000000a1");
    private static readonly Guid Usd = Guid.Parse("10000000-0000-4000-8000-0000000000a2");

    private static string CodeOf(Action action) => Assert.Throws<MoneyRuleException>(action).Code;

    [Theory]
    [InlineData("12.34", 2)]
    [InlineData("12.3400", 2)]
    [InlineData("12", 0)]
    [InlineData("0.123456", 6)]
    [InlineData("-5.10", 2)]
    public void An_amount_within_the_profile_scale_is_kept_exactly(string text, int scale)
    {
        var amount = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var money = Money.Of(amount, Try, scale);
        Assert.Equal((amount, Try, scale), (money.Amount, money.CurrencyId, money.Scale));
        Assert.True(Money.TryOf(amount, Try, scale, out var tried));
        Assert.Equal(money, tried);
    }

    [Theory]
    [InlineData("12.345", 2)]
    [InlineData("0.5", 0)]
    [InlineData("0.1234567", 6)]
    public void More_significant_decimals_than_the_scale_are_refused_never_rounded(string text, int scale)
    {
        var amount = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(MoneyRuleCodes.ScaleExceeded, CodeOf(() => Money.Of(amount, Try, scale)));
        Assert.False(Money.TryOf(amount, Try, scale, out _));
        Assert.False(Money.FitsScale(amount, scale));
    }

    [Fact]
    public void The_scale_and_currency_must_be_real()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Of(1m, Try, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Of(1m, Try, Money.MaximumScale + 1));
        Assert.Throws<ArgumentException>(() => Money.Of(1m, Guid.Empty, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Round(1m, Try, -1));
        Assert.Equal(0, Money.Of(0m, Try, Money.MaximumScale).Amount);
    }

    [Theory]
    [InlineData("2.345", 2, "2.35")]
    [InlineData("2.344", 2, "2.34")]
    [InlineData("-2.345", 2, "-2.35")]
    [InlineData("2.5", 0, "3")]
    [InlineData("3.5", 0, "4")]
    [InlineData("-0.5", 0, "-1")]
    [InlineData("41351.592275", 2, "41351.59")]
    [InlineData("10.125", 2, "10.13")]
    public void Rounding_is_half_away_from_zero_SAT_D11_not_bankers(string exact, int scale, string rounded)
    {
        var value = decimal.Parse(exact, System.Globalization.CultureInfo.InvariantCulture);
        var expected = decimal.Parse(rounded, System.Globalization.CultureInfo.InvariantCulture);
        var money = Money.Round(value, Try, scale);
        Assert.Equal((expected, scale), (money.Amount, money.Scale));
    }

    [Fact]
    public void Equal_amounts_are_equal_whatever_their_trailing_zeros()
    {
        Assert.Equal(Money.Of(12.5m, Try, 2), Money.Of(12.50m, Try, 2));
        Assert.Equal(Money.Of(12.5m, Try, 2).GetHashCode(), Money.Of(12.500m, Try, 2).GetHashCode());
        Assert.NotEqual(Money.Of(12.5m, Try, 2), Money.Of(12.5m, Usd, 2));
        Assert.Equal("12.50 " + Try, Money.Of(12.5m, Try, 2).ToString());
    }

    [Fact]
    public void Arithmetic_needs_one_currency_and_one_scale_and_comparison_one_currency()
    {
        var a = Money.Of(10.25m, Try, 2);
        var b = Money.Of(0.75m, Try, 2);
        Assert.Equal(Money.Of(11m, Try, 2), a.Add(b));
        Assert.Equal(Money.Of(9.5m, Try, 2), a.Subtract(b));
        Assert.Equal(Money.Of(-9.5m, Try, 2), b.Subtract(a));
        Assert.True(b.Subtract(a).IsNegative);
        Assert.Equal(Money.Of(-10.25m, Try, 2), a.Negate());
        Assert.True(a.IsGreaterThan(b));
        Assert.True(b.IsLessThan(a));
        Assert.True(Money.Zero(Try, 2).IsZero);
        // A different scale of one currency compares by value but is never summed silently.
        Assert.Equal(0, Money.Of(12.5m, Try, 2).CompareTo(Money.Of(12.5m, Try, 6)));
        Assert.Equal(MoneyRuleCodes.ScaleMismatch, CodeOf(() => a.Add(Money.Of(1m, Try, 6))));
        Assert.Equal(MoneyRuleCodes.CurrencyMismatch, CodeOf(() => a.Add(Money.Of(1m, Usd, 2))));
        Assert.Equal(MoneyRuleCodes.CurrencyMismatch, CodeOf(() => a.Subtract(Money.Of(1m, Usd, 2))));
        Assert.Equal(MoneyRuleCodes.CurrencyMismatch, CodeOf(() => a.CompareTo(Money.Of(1m, Usd, 2))));
    }

    [Fact]
    public void An_exchange_rate_converts_exactly_then_rounds_once_to_the_target_scale()
    {
        var rate = ExchangeRate.Of(Usd, Try, 34.5678m);
        var usd = Money.Of(100.25m, Usd, 2);

        Assert.Equal(3465.421950m, rate.ConvertExact(usd));
        var converted = rate.Convert(usd, 2);
        Assert.Equal((3465.42m, Try, 2), (converted.Amount, converted.CurrencyId, converted.Scale));
        // Half away from zero at the target scale: 0.5 USD × 1.01 = 0.505 TRY → 0.51.
        Assert.Equal(0.51m, ExchangeRate.Of(Usd, Try, 1.01m).Convert(Money.Of(0.5m, Usd, 2), 2).Amount);
        Assert.Equal(-0.51m, ExchangeRate.Of(Usd, Try, 1.01m).Convert(Money.Of(-0.5m, Usd, 2), 2).Amount);
        Assert.Equal(0m, rate.ConvertExact(Money.Zero(Usd, 2)));
    }

    [Fact]
    public void An_exchange_rate_is_positive_between_two_currencies_and_converts_only_its_source_currency()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExchangeRate.Of(Usd, Try, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExchangeRate.Of(Usd, Try, -1m));
        Assert.Throws<ArgumentException>(() => ExchangeRate.Of(Try, Try, 1m));
        Assert.Throws<ArgumentException>(() => ExchangeRate.Of(Guid.Empty, Try, 1m));
        Assert.Equal(MoneyRuleCodes.ExchangeRateCurrencyMismatch,
            CodeOf(() => ExchangeRate.Of(Usd, Try, 34m).ConvertExact(Money.Of(1m, Try, 2))));
    }

    [Fact]
    public void A_conversion_that_System_Decimal_cannot_hold_exactly_is_refused_not_rounded()
    {
        var rate = ExchangeRate.Of(Usd, Try, 0.0000000000000001m);
        Assert.Throws<OverflowException>(() => rate.ConvertExact(Money.Of(0.0000000000001m, Usd, 13)));
        Assert.Throws<OverflowException>(() => ExchangeRate.Of(Usd, Try, 2m).ConvertExact(Money.Of(decimal.MaxValue, Usd, 0)));
    }
}
