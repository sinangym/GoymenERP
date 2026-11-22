using System.Numerics;

namespace GoymenERP.BuildingBlocks.Quantities;

/// <summary>
/// SAT-D436 / SAT-D439 (AK-5; golden ADET_KG_FORMUL_GOLDEN_V1 §1, TK3/TK4/TK9): the ONE deterministic kg price division shared by
/// Sales and Purchasing — amount ÷ kilograms in exact integer arithmetic, rounded once to 0,01 of the document currency, half away
/// from zero (no intermediate rounding; no exchange rate). Each module keeps only its own wire states (why a figure is not shown);
/// the figure itself always comes from here. Information only: never a cost and never an amount (SAT-D434/SAT-D435).
/// </summary>
public static class KilogramPriceArithmetic
{
    /// <summary>SAT-D436: a kg price is shown to 0,01 of the document currency.</summary>
    public const int PriceScale = 2;

    /// <summary>
    /// amount ÷ kilograms rounded once to <see cref="PriceScale"/> decimals, half away from zero.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The kilograms are not positive (the caller names that state).</exception>
    /// <exception cref="OverflowException">The rounded figure cannot be held as a decimal (never silently clipped).</exception>
    public static decimal RoundedQuotient(decimal amount, decimal kilograms)
    {
        if (kilograms <= 0m) throw new ArgumentOutOfRangeException(nameof(kilograms));
        var (amountMantissa, amountScale) = Split(amount);
        var (kgMantissa, kgScale) = Split(kilograms);
        // amount / kg × 10^PriceScale = (aM × 10^kS × 10^PriceScale) / (kM × 10^aS)
        var numerator = BigInteger.Abs(amountMantissa) * BigInteger.Pow(10, kgScale + PriceScale);
        var denominator = kgMantissa * BigInteger.Pow(10, amountScale);
        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        if (remainder * 2 >= denominator) quotient++;
        var result = (decimal)quotient / 100m;
        return amountMantissa.Sign < 0 ? -result : result;
    }

    private static (BigInteger Mantissa, int Scale) Split(decimal value)
    {
        var bits = decimal.GetBits(value);
        var mantissa = new BigInteger((uint)bits[0]) | (new BigInteger((uint)bits[1]) << 32) | (new BigInteger((uint)bits[2]) << 64);
        var scale = (bits[3] >> 16) & 0xFF;
        return (bits[3] < 0 ? -mantissa : mantissa, scale);
    }
}
