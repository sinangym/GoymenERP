using System.Globalization;
using System.Numerics;
using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Accounting.UnitTests;

public sealed class ExactMoneyRoundingTests
{
    private static readonly Guid Currency = new("24355a72-efb6-46ee-a6f6-3bc797b73047");

    [Theory]
    [InlineData(1, 3, 2, "0.33")]
    [InlineData(-1, 3, 2, "-0.33")]
    [InlineData(2, 3, 2, "0.67")]
    [InlineData(-2, 3, 2, "-0.67")]
    [InlineData(1, 8, 2, "0.13")]
    [InlineData(-1, 8, 2, "-0.13")]
    [InlineData(1, 4, 2, "0.25")]
    [InlineData(0, 3, 2, "0")]
    public void Exact_ratios_use_the_same_signed_half_away_from_zero_rule(int numerator, int denominator, int scale, string expected)
    {
        var value = Money.Round(new BigInteger(numerator), new BigInteger(denominator), Currency, scale);
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), value.Amount);
        Assert.Equal(Currency, value.CurrencyId); Assert.Equal(scale, value.Scale);
    }

    [Fact]
    public void A_ratio_just_below_or_above_a_half_is_not_rounded_to_the_half_by_decimal_division()
    {
        var denominator = BigInteger.Pow(10, 50); var half = denominator / 2;
        Assert.Equal(0m, Money.Round(half - 1, denominator, Currency, 0).Amount);
        Assert.Equal(1m, Money.Round(half, denominator, Currency, 0).Amount);
        Assert.Equal(1m, Money.Round(half + 1, denominator, Currency, 0).Amount);
        Assert.Equal(0m, Money.Round(-half + 1, denominator, Currency, 0).Amount);
        Assert.Equal(-1m, Money.Round(-half, denominator, Currency, 0).Amount);
        Assert.Equal(-1m, Money.Round(-half - 1, denominator, Currency, 0).Amount);
    }

    [Fact]
    public void Representable_extremes_and_trailing_zeros_are_exact_and_overflow_is_refused()
    {
        var maximum = (BigInteger.One << 96) - 1;
        Assert.Equal(decimal.MaxValue, Money.Round(maximum * 10, 10, Currency, 28).Amount);
        Assert.Equal(decimal.MinValue, Money.Round(-maximum, BigInteger.One, Currency, 0).Amount);
        Assert.Equal(0.0000000000000000000000000001m,
            Money.Round(BigInteger.One, BigInteger.Pow(10, 28), Currency, 28).Amount);
        Assert.Throws<OverflowException>(() => Money.Round(maximum + 1, BigInteger.One, Currency, 0));
        Assert.Throws<OverflowException>(() => Money.Round((maximum * 2) + 1, 2, Currency, 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(29)]
    public void A_scale_outside_decimal_bounds_is_refused(int scale) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Round(BigInteger.One, BigInteger.One, Currency, scale));

    [Fact]
    public void A_missing_currency_or_nonpositive_denominator_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Money.Round(BigInteger.One, BigInteger.One, Guid.Empty, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Round(BigInteger.One, BigInteger.Zero, Currency, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Round(BigInteger.One, -BigInteger.One, Currency, 2));
    }
}
