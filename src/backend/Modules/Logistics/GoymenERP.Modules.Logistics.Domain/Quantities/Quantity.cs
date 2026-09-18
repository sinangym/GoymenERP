namespace GoymenERP.Modules.Logistics.Domain.Quantities;

/// <summary>Unit of a shipped quantity. Weight is kilograms, count is whole pieces (Ö2).</summary>
public enum QuantityUnit
{
    Kilogram = 1,
    Piece = 2,
}

/// <summary>
/// An exact non-negative quantity in one unit. Kilograms carry at most three decimals and
/// pieces are whole numbers (Ö2). The value is never rounded or truncated: extra digits are
/// refused, only trailing zeros are dropped, so equal quantities also print equally. This is
/// the same rule Purchasing applies; it is repeated here because a Domain project must not
/// reference another module's Domain. A Logistics quantity is a share, not a physical stock
/// quantity; Inventory owns those. Which Settings unit a line is in, and that unit's approved
/// scale, are checked by the Application layer's verifier against the Settings profile.
/// </summary>
public readonly record struct Quantity : IComparable<Quantity>
{
    public const int KilogramScale = 3;

    private Quantity(decimal value, QuantityUnit unit)
    {
        Value = value;
        Unit = unit;
    }

    public decimal Value { get; }
    public QuantityUnit Unit { get; }
    public bool IsZero => Value == 0m;

    public static Quantity Of(decimal value, QuantityUnit unit)
    {
        if (!Enum.IsDefined(unit))
        {
            throw new ArgumentException("Miktar birimi geçersiz.", nameof(unit));
        }

        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Miktar negatif olamaz.");
        }

        var scale = AllowedScale(unit);
        var withinScale = decimal.Round(value, scale);
        if (withinScale != value)
        {
            throw new ArgumentException(unit == QuantityUnit.Piece
                ? "Adet tam sayı olmalıdır."
                : $"Kilogram en fazla {scale} ondalık taşıyabilir.", nameof(value));
        }

        return new Quantity(Normalize(withinScale), unit);
    }

    public static Quantity Positive(decimal value, QuantityUnit unit)
    {
        var quantity = Of(value, unit);
        if (quantity.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Miktar sıfırdan büyük olmalıdır.");
        }

        return quantity;
    }

    public static Quantity Zero(QuantityUnit unit) => Of(0m, unit);

    public static int AllowedScale(QuantityUnit unit) => unit == QuantityUnit.Piece ? 0 : KilogramScale;

    /// <summary>The number of decimals that carry a non-zero digit: 12.5000 → 1, 12.5001 → 4, 1000 → 0.</summary>
    public static int SignificantDecimals(decimal value) => Normalize(value).Scale;

    private static decimal Normalize(decimal value) => value / 1.0000000000000000000000000000m;

    public Quantity Add(Quantity other)
    {
        RequireSameUnit(other);
        return new Quantity(Value + other.Value, Unit);
    }

    public Quantity Subtract(Quantity other)
    {
        RequireSameUnit(other);
        if (other.Value > Value)
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.QuantityBelowZero,
                "Çıkarılan miktar eldeki miktardan büyük olamaz.");
        }

        return new Quantity(Value - other.Value, Unit);
    }

    public int CompareTo(Quantity other)
    {
        RequireSameUnit(other);
        return Value.CompareTo(other.Value);
    }

    public bool IsGreaterThan(Quantity other) => CompareTo(other) > 0;

    public void RequireSameUnit(Quantity other)
    {
        if (other.Unit != Unit)
        {
            throw new LogisticsRuleException(LogisticsRuleCodes.UnitMismatch,
                "Miktar birimleri farklı; kg ile adet birlikte işlenemez.");
        }
    }

    public override string ToString() => $"{Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} {Unit}";
}
