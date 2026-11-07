using System.Globalization;
using System.Numerics;

namespace GoymenERP.BuildingBlocks.Monetary;

/// <summary>
/// TEK-01 / TEK-03 (Alış planı): the shared commercial line price calculator — the one deterministic place a line's discounted
/// (net) unit price and its line amount are computed for a module that has no calculator of its own yet. Purchasing (A2a-1,
/// ALS-D5) is its first consumer; it applies the same rules as the Sales line calculator (SAT-D11, SAT-D76, delegation No 15),
/// so the screen and any report read the backend's number and never recompute it:
/// <list type="number">
/// <item>the base is the line's unit price (exact, as the person typed it);</item>
/// <item>the discount is taken from the base — a percent of the base (0..100) or an amount (0..base); never both;</item>
/// <item>the net unit price is EXACT and never rounded (SAT-D76);</item>
/// <item>the line amount is quantity × the exact net, multiplied without any intermediate rounding and rounded ONCE, half away
/// from zero (SAT-D11, <see cref="Money.Round"/>'s rule), to the scale the caller passes (the Settings money profile's).</item>
/// </list>
/// The arithmetic is arbitrary-precision: System.Decimal's own operators silently round once a product needs more than 28–29
/// significant digits, so a value is converted back to <see cref="decimal"/> only when it is known exactly; a result that cannot be
/// held exactly is an <see cref="OverflowException"/>, never a rounded amount. Whether a value is allowed (positive price,
/// scale, which unit) is the consumer's rule; this type refuses only what it cannot compute (a negative input, a percent outside
/// 0..100, a discount amount above the base). The Sales calculator is not changed; moving Sales to this type is the Sales
/// lane's follow-up (<c>docs/handoffs/ALIS_SATIS_DEVIR_v1.md</c>, TEK-03).
/// </summary>
public static class CommercialLinePriceCalculator
{
    /// <summary>The exact net unit price: the unit price minus the discount (percent of the base, or an amount), never rounded.</summary>
    public static decimal NetUnitPrice(decimal unitPrice, decimal? discountPercent, decimal? discountAmount) =>
        ToDecimal(ExactNet(unitPrice, discountPercent, discountAmount));

    /// <summary>
    /// The line amount: <paramref name="quantity"/> (in the price unit) × the exact net unit price, rounded once half away from
    /// zero to <paramref name="scale"/>.
    /// </summary>
    public static decimal LineAmount(decimal quantity, decimal unitPrice, decimal? discountPercent, decimal? discountAmount, int scale)
    {
        if (quantity < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Miktar negatif olamaz.");
        }

        if (scale is < 0 or > Money.MaximumScale)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), $"Tutar ölçeği 0 ile {Money.MaximumScale} arasında olmalıdır.");
        }

        return ToDecimal(ExactNet(unitPrice, discountPercent, discountAmount).Multiply(ExactDecimal.Of(quantity)).RoundAwayFromZero(scale));
    }

    /// <summary>The invariant text of a value with no trailing zeros ("41790" / "41790.25"), the wire form of every computed price.</summary>
    public static string Format(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static ExactDecimal ExactNet(decimal unitPrice, decimal? discountPercent, decimal? discountAmount)
    {
        if (unitPrice < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Birim fiyat negatif olamaz.");
        }

        if (discountPercent is not null && discountAmount is not null)
        {
            throw new ArgumentException("İskonto ya yüzde ya tutar olarak verilir; ikisi birden verilmez.", nameof(discountAmount));
        }

        var price = ExactDecimal.Of(unitPrice);
        if (discountPercent is { } percent)
        {
            if (percent is < 0m or > 100m)
            {
                throw new ArgumentOutOfRangeException(nameof(discountPercent), "İskonto yüzdesi 0 ile 100 arasında olmalıdır.");
            }

            return price.Subtract(price.Multiply(ExactDecimal.Of(percent)).DivideBy100());
        }

        if (discountAmount is { } amount)
        {
            if (amount < 0m || amount > unitPrice)
            {
                throw new ArgumentOutOfRangeException(nameof(discountAmount), "İskonto tutarı 0 ile birim fiyat arasında olmalıdır.");
            }

            return price.Subtract(ExactDecimal.Of(amount));
        }

        return price;
    }

    private static decimal ToDecimal(ExactDecimal value) => value.TryToDecimal(out var result)
        ? result
        : throw new OverflowException("The exact amount is not representable by System.Decimal.");

    /// <summary>Arbitrary-precision decimal: a mantissa and a scale, normalized (no trailing zeros).</summary>
    private readonly record struct ExactDecimal(BigInteger Mantissa, int Scale)
    {
        public static ExactDecimal Of(decimal value)
        {
            var text = Format(value);
            var negative = text.StartsWith('-');
            var digits = negative ? text[1..] : text;
            var dot = digits.IndexOf('.', StringComparison.Ordinal);
            var scale = dot < 0 ? 0 : digits.Length - dot - 1;
            var mantissa = BigInteger.Parse(dot < 0 ? digits : digits.Remove(dot, 1), CultureInfo.InvariantCulture);
            return Create(negative ? -mantissa : mantissa, scale);
        }

        public ExactDecimal Subtract(ExactDecimal other)
        {
            var scale = Math.Max(Scale, other.Scale);
            return Create(ScaleTo(Mantissa, Scale, scale) - ScaleTo(other.Mantissa, other.Scale, scale), scale);
        }

        public ExactDecimal Multiply(ExactDecimal other) => Create(Mantissa * other.Mantissa, checked(Scale + other.Scale));

        public ExactDecimal DivideBy100() => Create(Mantissa, checked(Scale + 2));

        /// <summary>SAT-D11: half away from zero (never the banker's rule) to <paramref name="scale"/> decimals, on the exact value.</summary>
        public ExactDecimal RoundAwayFromZero(int scale)
        {
            if (Scale <= scale)
            {
                return this;
            }

            var divisor = BigInteger.Pow(10, Scale - scale);
            var quotient = BigInteger.DivRem(BigInteger.Abs(Mantissa), divisor, out var remainder);
            if (remainder * 2 >= divisor)
            {
                quotient += 1;
            }

            return Create(Mantissa.Sign < 0 ? -quotient : quotient, scale);
        }

        public bool TryToDecimal(out decimal value)
        {
            var negative = Mantissa.Sign < 0;
            var digits = BigInteger.Abs(Mantissa).ToString(CultureInfo.InvariantCulture);
            if (Scale > 0)
            {
                digits = digits.Length <= Scale
                    ? $"0.{new string('0', Scale - digits.Length)}{digits}"
                    : $"{digits[..^Scale]}.{digits[^Scale..]}";
            }

            var text = negative ? $"-{digits}" : digits;
            return decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)
                   && string.Equals(Format(value), text, StringComparison.Ordinal);
        }

        private static ExactDecimal Create(BigInteger mantissa, int scale)
        {
            while (scale > 0 && mantissa % 10 == 0)
            {
                mantissa /= 10;
                scale -= 1;
            }

            return new ExactDecimal(mantissa, scale);
        }

        private static BigInteger ScaleTo(BigInteger mantissa, int currentScale, int targetScale) =>
            currentScale == targetScale ? mantissa : mantissa * BigInteger.Pow(10, targetScale - currentScale);
    }
}
