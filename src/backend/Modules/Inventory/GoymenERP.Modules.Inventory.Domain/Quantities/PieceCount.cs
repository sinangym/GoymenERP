using System.Globalization;

namespace GoymenERP.Modules.Inventory.Domain.Quantities;

/// <summary>
/// KLN-D119 (Sinan, 2026-11-21, <c>docs/decisions/SINAN_KARARLARI_2026-11-21_TREN_KAPANIS.md</c>): the number of pieces a
/// stock label of a piece-sold/-bought product carries next to its kilograms. A whole, non-negative number (the Settings
/// piece profile has scale 0); a negative or fractional count does not exist and is refused, never rounded.
/// <para>
/// A label either tracks pieces from its first stock (a receipt or a split/merge output that names them) or it never does:
/// labels stocked before AK-2 stay piece-less (no backfill), and a movement that does not know the count of a piece-tracking
/// label is refused (fail-closed, <see cref="InventoryRuleCodes.PieceCountRequired"/>).
/// </para>
/// </summary>
public readonly record struct PieceCount : IComparable<PieceCount>
{
    private PieceCount(int value) => Value = value;

    public int Value { get; }

    public static PieceCount Zero { get; } = new(0);

    /// <summary>Any non-negative whole piece count.</summary>
    public static PieceCount Of(int value) => value < 0
        ? throw new InventoryRuleException(InventoryRuleCodes.InvalidPieceCount, "Adet negatif olamaz.")
        : new(value);

    /// <summary>A piece count read from a decimal source (a request or a stored row): whole and non-negative, else refused.</summary>
    public static PieceCount Of(decimal value) =>
        value < 0m || decimal.Truncate(value) != value || value > int.MaxValue
            ? throw new InventoryRuleException(InventoryRuleCodes.InvalidPieceCount, "Adet sıfır veya pozitif tam sayı olmalıdır.")
            : new((int)value);

    /// <summary>A label's or output's count: strictly positive.</summary>
    public static PieceCount Positive(int value)
    {
        var count = Of(value);
        return count.Value == 0
            ? throw new InventoryRuleException(InventoryRuleCodes.InvalidPieceCount, "Adet sıfırdan büyük olmalıdır.")
            : count;
    }

    public static PieceCount operator +(PieceCount left, PieceCount right) => new(checked(left.Value + right.Value));

    public int CompareTo(PieceCount other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
