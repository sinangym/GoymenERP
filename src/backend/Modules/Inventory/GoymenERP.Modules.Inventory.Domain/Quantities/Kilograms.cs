using System.Globalization;

namespace GoymenERP.Modules.Inventory.Domain.Quantities;

/// <summary>
/// A physical mass in kilograms, exact to at most 3 decimals (Ö2, Sinan's decision of
/// 2026-09-18: kg at most 3 decimals, no silent digit cutting).
///
/// A value that needs a 4th decimal is REFUSED, never rounded or truncated: a cut value
/// would claim a stock the scale never weighed. Trailing zeros are not extra precision
/// (1.2000 is 1.2), so they are accepted and normalised away.
///
/// Whether a Product's stock unit really is kilogram is the Settings owner's answer (T02-a);
/// this type only guarantees the kilogram arithmetic itself.
/// </summary>
public readonly record struct Kilograms : IComparable<Kilograms>
{
    public const int MaximumDecimals = 3;

    private Kilograms(decimal value) => Value = value;

    public decimal Value { get; }

    public static Kilograms Zero { get; } = new(0m);

    /// <summary>Any non-negative kilogram amount with at most 3 decimals.</summary>
    public static Kilograms Of(decimal value)
    {
        if (value < 0m)
        {
            throw new InventoryRuleException(
                InventoryRuleCodes.QuantityNotPositive, "Kilogram miktarı negatif olamaz.");
        }

        if (decimal.Round(value, MaximumDecimals, MidpointRounding.ToZero) != value)
        {
            throw new InventoryRuleException(
                InventoryRuleCodes.KilogramPrecisionExceeded,
                "Kilogram miktarı en fazla 3 ondalık basamak içerebilir; fazla basamak kesilmez, reddedilir.");
        }

        // Normalise trailing zeros so 1.2000 and 1.2 are the same value and the same text.
        return new(value / 1.000000000000000000000000000000000m);
    }

    /// <summary>A movement quantity: strictly positive.</summary>
    public static Kilograms Positive(decimal value)
    {
        var kilograms = Of(value);
        if (kilograms.Value == 0m)
        {
            throw new InventoryRuleException(
                InventoryRuleCodes.QuantityNotPositive, "Hareket miktarı sıfırdan büyük olmalıdır.");
        }

        return kilograms;
    }

    public static Kilograms operator +(Kilograms left, Kilograms right) => new(left.Value + right.Value);

    public int CompareTo(Kilograms other) => Value.CompareTo(other.Value);

    public static bool operator <(Kilograms left, Kilograms right) => left.Value < right.Value;

    public static bool operator >(Kilograms left, Kilograms right) => left.Value > right.Value;

    public static bool operator <=(Kilograms left, Kilograms right) => left.Value <= right.Value;

    public static bool operator >=(Kilograms left, Kilograms right) => left.Value >= right.Value;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
