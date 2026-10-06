using System.Globalization;
using System.Numerics;

namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// AT-018 / master §10.2 "Fiyat": the list price, the ordered adjustments and the net shown apart - the one deterministic
/// calculator the API, the screen and any report read (the screen never recomputes it). In order:
/// <list type="number">
/// <item>the base is the line's unit price (the reference's list price by default, or the person's overwrite);</item>
/// <item>the discount is taken from the base - a percent of the base, or an amount;</item>
/// <item>the maturity surcharge (vade farkı) is added to the discounted price - a percent of it, or an amount.</item>
/// </list>
/// Exact decimal arithmetic, NEVER rounded: rounding to the money scale belongs to the total/invoice policy, which is not
/// decided here (T28). A line without a unit price has no net.
/// </summary>
public static class SalesLinePriceCalculator
{
    public static decimal? NetUnitPrice(CommercialDraftFields commercial)
    {
        ArgumentNullException.ThrowIfNull(commercial);
        if (commercial.UnitPrice is not { } priceText)
        {
            return null;
        }

        var price = ExactDecimal.Parse(priceText);
        var discount = commercial.DiscountPercent is { } percent
            ? price.Multiply(ExactDecimal.Parse(percent)).DivideBy100()
            : commercial.DiscountAmount is { } amount ? ExactDecimal.Parse(amount) : ExactDecimal.Zero;
        var discounted = price.Subtract(discount);
        var surcharge = commercial.SurchargePercent is { } surchargePercent
            ? discounted.Multiply(ExactDecimal.Parse(surchargePercent)).DivideBy100()
            : commercial.SurchargeAmount is { } surchargeAmount ? ExactDecimal.Parse(surchargeAmount) : ExactDecimal.Zero;
        var net = discounted.Add(surcharge);
        return net.TryToDecimal(out var result)
            ? result
            : throw new OverflowException("The exact net price is not representable by System.Decimal.");
    }

    /// <summary>The net as exact text with no trailing zeros ("41790" / "41790.25"); null without a price.</summary>
    public static string? NetUnitPriceText(CommercialDraftFields commercial) =>
        NetUnitPrice(commercial) is { } net ? Format(net) : null;

    public static string Format(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

    public static decimal Parse(string text) => TryParseExact(text, out var parsed)
        ? parsed
        : throw new OverflowException("Decimal text is not exactly representable by System.Decimal.");

    /// <summary>Plain decimal text must round-trip canonically; <see cref="decimal.TryParse(string, out decimal)"/> may round excess precision.</summary>
    public static bool TryParseExact(string text, out decimal parsed)
    {
        parsed = default;
        if (string.IsNullOrEmpty(text)) return false;

        var dot = -1;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '.')
            {
                if (dot >= 0) return false;
                dot = index;
            }
            else if (character is < '0' or > '9')
            {
                return false;
            }
        }

        if (dot is 0 || dot == text.Length - 1) return false;
        var canonical = Canonical(text, dot);
        return decimal.TryParse(canonical, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out parsed)
            && string.Equals(Format(parsed), canonical, StringComparison.Ordinal);
    }

    private static string Canonical(string text, int dot)
    {
        var integer = dot < 0 ? text : text[..dot];
        var fraction = dot < 0 ? string.Empty : text[(dot + 1)..];
        integer = integer.TrimStart('0');
        if (integer.Length == 0) integer = "0";
        fraction = fraction.TrimEnd('0');
        return fraction.Length == 0 ? integer : $"{integer}.{fraction}";
    }

    /// <summary>
    /// Arbitrary-precision decimal arithmetic for the formula's intermediate values. Conversion to System.Decimal happens
    /// only once, after the exact net is known, so multiplication or division can never silently round a business value.
    /// </summary>
    private readonly record struct ExactDecimal(BigInteger Mantissa, int Scale)
    {
        public static ExactDecimal Zero { get; } = new(BigInteger.Zero, 0);

        public static ExactDecimal Parse(string text)
        {
            var dot = text.IndexOf('.');
            var scale = dot < 0 ? 0 : text.Length - dot - 1;
            var digits = dot < 0 ? text : text.Remove(dot, 1);
            return Create(BigInteger.Parse(digits, CultureInfo.InvariantCulture), scale);
        }

        public ExactDecimal Add(ExactDecimal other)
        {
            var scale = Math.Max(Scale, other.Scale);
            return Create(ScaleTo(Mantissa, Scale, scale) + ScaleTo(other.Mantissa, other.Scale, scale), scale);
        }

        public ExactDecimal Subtract(ExactDecimal other)
        {
            var scale = Math.Max(Scale, other.Scale);
            return Create(ScaleTo(Mantissa, Scale, scale) - ScaleTo(other.Mantissa, other.Scale, scale), scale);
        }

        public ExactDecimal Multiply(ExactDecimal other) => Create(Mantissa * other.Mantissa, checked(Scale + other.Scale));

        public ExactDecimal DivideBy100() => Create(Mantissa, checked(Scale + 2));

        public bool TryToDecimal(out decimal value) => TryParseExact(ToText(), out value);

        private string ToText()
        {
            var negative = Mantissa.Sign < 0;
            var digits = BigInteger.Abs(Mantissa).ToString(CultureInfo.InvariantCulture);
            if (Scale > 0)
            {
                digits = digits.Length <= Scale
                    ? $"0.{new string('0', Scale - digits.Length)}{digits}"
                    : $"{digits[..^Scale]}.{digits[^Scale..]}";
            }
            return negative ? $"-{digits}" : digits;
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

    /// <summary>Whether two exact texts are the same value ("42000.50" = "42000.5").</summary>
    public static bool SameValue(string left, string right) => Parse(left) == Parse(right);
}
