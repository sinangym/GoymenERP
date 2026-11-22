using System.Numerics;

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

    /// <summary>The real kg price of an order (invoiced amount ÷ weighed kg of invoiced lines) is not wired to invoices yet.</summary>
    public const string RealizationNotAvailable = "RealizationNotAvailable";
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
    public const int Scale = 2;

    public static PurchaseKgPriceResult Line(decimal? amount, decimal? kilograms, string missingKilogramsCode)
    {
        if (amount is null) return PurchaseKgPriceResult.Unavailable(PurchaseKgPriceUnavailableCodes.AmountUnavailable);
        if (kilograms is null) return PurchaseKgPriceResult.Unavailable(missingKilogramsCode);
        if (kilograms.Value <= 0m) return PurchaseKgPriceResult.Unavailable(PurchaseKgPriceUnavailableCodes.ZeroKg);
        return new(RoundedQuotient(amount.Value, kilograms.Value), null);
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

    /// <summary>amount ÷ kilograms rounded once to <see cref="Scale"/> decimals, half away from zero, in exact integer arithmetic.</summary>
    internal static decimal RoundedQuotient(decimal amount, decimal kilograms)
    {
        if (kilograms <= 0m) throw new ArgumentOutOfRangeException(nameof(kilograms));
        var (amountMantissa, amountScale) = Split(amount);
        var (kgMantissa, kgScale) = Split(kilograms);
        // amount / kg × 10^Scale = (aM × 10^kS × 10^Scale) / (kM × 10^aS)
        var numerator = BigInteger.Abs(amountMantissa) * BigInteger.Pow(10, kgScale + Scale);
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
