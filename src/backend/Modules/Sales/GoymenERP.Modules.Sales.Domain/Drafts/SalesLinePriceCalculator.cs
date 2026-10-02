using System.Globalization;

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

        var price = Parse(priceText);
        var discount = commercial.DiscountPercent is { } percent ? checked(price * Parse(percent)) / 100m
            : commercial.DiscountAmount is { } amount ? Parse(amount)
            : 0m;
        var discounted = checked(price - discount);
        var surcharge = commercial.SurchargePercent is { } surchargePercent ? checked(discounted * Parse(surchargePercent)) / 100m
            : commercial.SurchargeAmount is { } surchargeAmount ? Parse(surchargeAmount)
            : 0m;
        return checked(discounted + surcharge);
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

    /// <summary>Whether two exact texts are the same value ("42000.50" = "42000.5").</summary>
    public static bool SameValue(string left, string right) => Parse(left) == Parse(right);
}
