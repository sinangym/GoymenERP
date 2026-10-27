using System.Globalization;

namespace GoymenERP.BuildingBlocks.Quantities;

/// <summary>Unit of a business quantity. Weight is kilograms, count is whole pieces (Ö2).</summary>
public enum QuantityUnit
{
    Kilogram = 1,
    Piece = 2,
}

/// <summary>
/// The refusals of the module that owns a quantity, for the two rules that compare two
/// quantities: different units, and a subtraction below zero. The rule itself lives once in
/// <see cref="Quantity{TRefusals}"/> (TEK-02); only the stable refusal code is the owner's, so a
/// Sales quantity is still refused with <c>sales_unit_mismatch</c> and a Purchasing quantity with
/// <c>purchasing_unit_mismatch</c>. The message is the shared one and is passed through verbatim.
/// </summary>
public interface IQuantityRuleRefusals
{
    static abstract Exception UnitMismatch(string message);

    static abstract Exception BelowZero(string message);
}

/// <summary>
/// An exact non-negative quantity in one unit, the one shared rule Sales, Purchasing and Logistics
/// use (TEK-02; previously three copies). Kilograms carry at most three decimals and pieces are
/// whole numbers (Ö2). The value is never rounded or truncated: extra digits are refused, only
/// trailing zeros are dropped, so "12.5000" is 12.5 kg and "12.5001" is an error. This is an
/// ordered, purchased or shipped share, not a physical stock quantity; Inventory owns those.
/// Which Settings unit a line is in, and that unit's approved scale, come from the Settings
/// profile the owning module checks; this type only holds the arithmetic rule.
/// <para>
/// <typeparamref name="TRefusals"/> names the owning module, so each module's quantities stay
/// their own type (a Sales quantity is never added to a Purchasing one by accident) and keep the
/// module's refusal codes.
/// </para>
/// </summary>
public readonly record struct Quantity<TRefusals> : IComparable<Quantity<TRefusals>>
    where TRefusals : IQuantityRuleRefusals
{
    /// <summary>Ö2: kilograms carry at most three decimals.</summary>
    public const int KilogramScale = 3;

    private const string UnitMismatchMessage = "Miktar birimleri farklı; kg ile adet birlikte işlenemez.";
    private const string BelowZeroMessage = "Çıkarılan miktar eldeki miktardan büyük olamaz.";

    private Quantity(decimal value, QuantityUnit unit)
    {
        Value = value;
        Unit = unit;
    }

    public decimal Value { get; }
    public QuantityUnit Unit { get; }
    public bool IsZero => Value == 0m;

    public static Quantity<TRefusals> Of(decimal value, QuantityUnit unit)
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

        return new Quantity<TRefusals>(Normalize(withinScale), unit);
    }

    public static Quantity<TRefusals> Positive(decimal value, QuantityUnit unit)
    {
        var quantity = Of(value, unit);
        if (quantity.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Miktar sıfırdan büyük olmalıdır.");
        }

        return quantity;
    }

    public static Quantity<TRefusals> Zero(QuantityUnit unit) => Of(0m, unit);

    public static int AllowedScale(QuantityUnit unit) => unit == QuantityUnit.Piece ? 0 : KilogramScale;

    /// <summary>The number of decimals that carry a non-zero digit: 12.5000 → 1, 12.5001 → 4, 1000 → 0.</summary>
    public static int SignificantDecimals(decimal value) => Normalize(value).Scale;

    /// <summary>Drops trailing zeros without changing the value, so equal quantities also print equally (Ö2: no padding zeros).</summary>
    private static decimal Normalize(decimal value) => value / 1.0000000000000000000000000000m;

    public Quantity<TRefusals> Add(Quantity<TRefusals> other)
    {
        RequireSameUnit(other);
        return new Quantity<TRefusals>(Value + other.Value, Unit);
    }

    public Quantity<TRefusals> Subtract(Quantity<TRefusals> other)
    {
        RequireSameUnit(other);
        if (other.Value > Value)
        {
            throw TRefusals.BelowZero(BelowZeroMessage);
        }

        return new Quantity<TRefusals>(Value - other.Value, Unit);
    }

    public int CompareTo(Quantity<TRefusals> other)
    {
        RequireSameUnit(other);
        return Value.CompareTo(other.Value);
    }

    public bool IsGreaterThan(Quantity<TRefusals> other) => CompareTo(other) > 0;

    public void RequireSameUnit(Quantity<TRefusals> other)
    {
        if (other.Unit != Unit)
        {
            throw TRefusals.UnitMismatch(UnitMismatchMessage);
        }
    }

    public override string ToString() => $"{Value.ToString(CultureInfo.InvariantCulture)} {Unit}";
}
