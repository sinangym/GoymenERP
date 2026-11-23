using System.Numerics;
using GoymenERP.BuildingBlocks.Quantities;

namespace GoymenERP.Modules.Purchasing.Domain.Orders;

/// <summary>Why a kg price is not shown (golden <c>ADET_KG_FORMUL_GOLDEN_V1</c> K13–K18).</summary>
public static class PurchaseKgPriceUnavailableCodes
{
    /// <summary>K13 / TK7: no recorded theoretical kg (before approval, or no kilogram source).</summary>
    public const string TheoreticalKgUnavailable = "TheoreticalKgUnavailable";

    /// <summary>K14: the invoiced line has no weighed kilograms.</summary>
    public const string WeighedKgUnavailable = "WeighedKgUnavailable";

    /// <summary>K15: the relevant kilograms are zero.</summary>
    public const string ZeroKg = "ZeroKg";

    /// <summary>The line has no amount (unpriced, "fiyat bekliyor").</summary>
    public const string AmountUnavailable = "AmountUnavailable";

    /// <summary>K17 / K18 (N2 open): the document level is shown only while every line is a goods line with its kg.</summary>
    public const string NotAllLinesHaveKg = "NotAllLinesHaveKg";

    /// <summary>
    /// The real kg price was not read for this answer: a command response carries the order without its invoice realization
    /// (the order read does). Never a zero or an estimate.
    /// </summary>
    public const string RealizationNotAvailable = "RealizationNotAvailable";

    /// <summary>K01 / TK6: nothing is invoiced yet, so there is no real kg price.</summary>
    public const string NotInvoiced = "NotInvoiced";
}

/// <summary>One kg price: the value at 0.01 (half away from zero) or why it is not shown.</summary>
public sealed record PurchaseKgPriceResult(decimal? Value, string? UnavailableCode)
{
    public static PurchaseKgPriceResult Unavailable(string code) => new(null, code);
}

/// <summary>One line's inputs: KDV-exclusive net amount (TK1), its kilograms (theoretical or weighed), and whether it is a goods line.</summary>
public sealed record PurchaseKgPriceLine(decimal? Amount, decimal? Kilograms, bool IsGoodsLine = true);

/// <summary>
/// AK-5 (SAT-D436, SAT-D439; golden <c>ADET_KG_FORMUL_GOLDEN_V1</c> §1): the kg price indicator — amount ÷ kilograms, computed exactly and
/// rounded once to 0.01 of the document currency, half away from zero (TK3: no intermediate rounding, the 2-decimal kg display never
/// enters the denominator). A document level is Σ amount ÷ Σ kg, never an average of line prices (TK4), shown only while every line
/// is a goods line with its kilograms (fail-closed interim rule, N2 open). It is information only: it is not a cost and never changes
/// an amount (SAT-D434/SAT-D435). The currency is the document's; no exchange rate is used (SAT-D439).
/// </summary>
public static class PurchaseKgPrice
{
    /// <summary>The display scale of a kg price (SAT-D436: 0,01 per kg; TK9: of the document currency).</summary>
    public const int Scale = KilogramPriceArithmetic.PriceScale;

    public static PurchaseKgPriceResult Line(decimal? amount, decimal? kilograms, string missingKilogramsCode)
    {
        if (amount is null) return PurchaseKgPriceResult.Unavailable(PurchaseKgPriceUnavailableCodes.AmountUnavailable);
        if (kilograms is null) return PurchaseKgPriceResult.Unavailable(missingKilogramsCode);
        if (kilograms.Value <= 0m) return PurchaseKgPriceResult.Unavailable(PurchaseKgPriceUnavailableCodes.ZeroKg);
        return new(RoundedQuotient(amount.Value, kilograms.Value), null);
    }

    /// <summary>
    /// The same rule over exact rationals (TK3/TK4/TK6): a partial invoice's kilogram share (ALS-D121) and an apportioned amount can
    /// be non-terminating, so they are summed exactly and the quotient is rounded once.
    /// </summary>
    public static PurchaseKgPriceResult Exact(PurchaseExactRatio? amount, PurchaseExactRatio? kilograms, string missingKilogramsCode)
    {
        if (amount is null) return PurchaseKgPriceResult.Unavailable(PurchaseKgPriceUnavailableCodes.AmountUnavailable);
        if (kilograms is null) return PurchaseKgPriceResult.Unavailable(missingKilogramsCode);
        if (kilograms.Value.Sign <= 0) return PurchaseKgPriceResult.Unavailable(PurchaseKgPriceUnavailableCodes.ZeroKg);
        return new(amount.Value.DivideBy(kilograms.Value).Round(Scale), null);
    }

    public static PurchaseKgPriceResult Document(IReadOnlyList<PurchaseKgPriceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0 || lines.Any(line => line is null || !line.IsGoodsLine || line.Kilograms is null))
            return PurchaseKgPriceResult.Unavailable(PurchaseKgPriceUnavailableCodes.NotAllLinesHaveKg);
        if (lines.Any(line => line.Amount is null))
            return PurchaseKgPriceResult.Unavailable(PurchaseKgPriceUnavailableCodes.AmountUnavailable);
        var kilograms = lines.Sum(line => line.Kilograms!.Value);
        if (kilograms <= 0m) return PurchaseKgPriceResult.Unavailable(PurchaseKgPriceUnavailableCodes.ZeroKg);
        return new(RoundedQuotient(lines.Sum(line => line.Amount!.Value), kilograms), null);
    }

    /// <summary>
    /// amount ÷ kilograms rounded once to <see cref="Scale"/> decimals, half away from zero — the shared deterministic division
    /// (<see cref="KilogramPriceArithmetic"/>, the same one Sales uses).
    /// </summary>
    internal static decimal RoundedQuotient(decimal amount, decimal kilograms) => KilogramPriceArithmetic.RoundedQuotient(amount, kilograms);
}

/// <summary>
/// An exact non-negative-denominator rational for the kg price indicator's sums (TK3: no intermediate rounding). It is an
/// information value only; no cost or amount is ever written from it.
/// </summary>
public readonly struct PurchaseExactRatio
{
    private readonly BigInteger numerator;
    private readonly BigInteger denominator;

    private PurchaseExactRatio(BigInteger numerator, BigInteger denominator)
    {
        this.numerator = numerator;
        this.denominator = denominator;
    }

    private BigInteger Denominator => denominator.IsZero ? BigInteger.One : denominator;

    public static PurchaseExactRatio Zero => new(BigInteger.Zero, BigInteger.One);

    public int Sign => numerator.Sign;

    public static PurchaseExactRatio Of(decimal value)
    {
        var bits = decimal.GetBits(value);
        var mantissa = new BigInteger((uint)bits[0]) | (new BigInteger((uint)bits[1]) << 32) | (new BigInteger((uint)bits[2]) << 64);
        return new(bits[3] < 0 ? -mantissa : mantissa, BigInteger.Pow(10, (bits[3] >> 16) & 0xFF));
    }

    public PurchaseExactRatio Add(PurchaseExactRatio other) =>
        new(numerator * other.Denominator + other.numerator * Denominator, Denominator * other.Denominator);

    public PurchaseExactRatio Subtract(PurchaseExactRatio other) =>
        new(numerator * other.Denominator - other.numerator * Denominator, Denominator * other.Denominator);

    public PurchaseExactRatio Multiply(decimal value)
    {
        var factor = Of(value);
        return new(numerator * factor.numerator, Denominator * factor.Denominator);
    }

    /// <exception cref="ArgumentOutOfRangeException">The divisor is not positive.</exception>
    public PurchaseExactRatio Divide(decimal value) => value > 0m
        ? DivideBy(Of(value))
        : throw new ArgumentOutOfRangeException(nameof(value), "Bölen pozitif olmalıdır.");

    /// <exception cref="ArgumentOutOfRangeException">The divisor is not positive.</exception>
    public PurchaseExactRatio DivideBy(PurchaseExactRatio divisor) => divisor.Sign > 0
        ? new(numerator * divisor.Denominator, Denominator * divisor.numerator)
        : throw new ArgumentOutOfRangeException(nameof(divisor), "Bölen pozitif olmalıdır.");

    public int CompareTo(PurchaseExactRatio other) => (numerator * other.Denominator).CompareTo(other.numerator * Denominator);

    /// <summary>Rounded once to <paramref name="scale"/> decimals, half away from zero.</summary>
    public decimal Round(int scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, 28);
        var scaled = BigInteger.Abs(numerator) * BigInteger.Pow(10, scale);
        var quotient = BigInteger.DivRem(scaled, Denominator, out var remainder);
        if (remainder * 2 >= Denominator) quotient++;
        var result = (decimal)quotient / (decimal)BigInteger.Pow(10, scale);
        return numerator.Sign < 0 ? -result : result;
    }
}
