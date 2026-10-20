using System.Globalization;

namespace GoymenERP.Modules.Inventory.Domain.Quantities;

/// <summary>
/// The physical dimension of a stock quantity. The concrete unit remains an explicit Settings-owned
/// <see cref="StockQuantityV2.UnitId"/>; this enum never supplies a conversion or a default unit.
/// </summary>
public enum StockQuantityDimensionV2
{
    Mass = 1,
    Volume = 2,
    Count = 3,
}

public static class StockQuantityRuleCodesV2
{
    public const string InvalidReference = "inventory_stock_quantity_reference_invalid";
    public const string InvalidProfile = "inventory_stock_quantity_profile_invalid";
    public const string InvalidDimension = "inventory_stock_quantity_dimension_invalid";
    public const string InvalidScale = "inventory_stock_quantity_scale_invalid";
    public const string QuantityNotPositive = "inventory_stock_quantity_not_positive";
    public const string ScaleExceeded = "inventory_stock_quantity_scale_exceeded";
    public const string ContextMismatch = "inventory_stock_quantity_context_mismatch";
}

public sealed class StockQuantityRuleExceptionV2(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// A positive stock quantity in one explicit Settings unit/profile revision. Decimal arithmetic is
/// retained exactly. Digits outside the profile scale are refused, never rounded or truncated.
/// </summary>
public readonly record struct StockQuantityV2
{
    private StockQuantityV2(
        decimal value,
        Guid unitId,
        Guid profileId,
        long profileVersion,
        StockQuantityDimensionV2 dimension,
        int scale)
    {
        Value = value;
        UnitId = unitId;
        ProfileId = profileId;
        ProfileVersion = profileVersion;
        Dimension = dimension;
        Scale = scale;
    }

    public decimal Value { get; }
    public Guid UnitId { get; }
    public Guid ProfileId { get; }
    public long ProfileVersion { get; }
    public StockQuantityDimensionV2 Dimension { get; }
    public int Scale { get; }

    public static StockQuantityV2 Positive(
        decimal value,
        Guid unitId,
        Guid profileId,
        long profileVersion,
        StockQuantityDimensionV2 dimension,
        int scale)
    {
        if (unitId == Guid.Empty || profileId == Guid.Empty)
        {
            throw Refused(StockQuantityRuleCodesV2.InvalidReference,
                "Birim ve profil kimlikleri açıkça verilmelidir.");
        }

        if (profileVersion <= 0)
        {
            throw Refused(StockQuantityRuleCodesV2.InvalidProfile,
                "Birim profil sürümü pozitif olmalıdır.");
        }

        if (!Enum.IsDefined(dimension))
        {
            throw Refused(StockQuantityRuleCodesV2.InvalidDimension,
                "Stok miktarı boyutu tanımlı olmalıdır.");
        }

        if (scale is < 0 or > 28)
        {
            throw Refused(StockQuantityRuleCodesV2.InvalidScale,
                "Birim profil ölçeği 0 ile 28 arasında olmalıdır.");
        }

        if (value <= 0m)
        {
            throw Refused(StockQuantityRuleCodesV2.QuantityNotPositive,
                "Stok miktarı sıfırdan büyük olmalıdır.");
        }

        var canonical = Canonical(value);
        if (DecimalScale(canonical) > scale)
        {
            throw Refused(StockQuantityRuleCodesV2.ScaleExceeded,
                "Stok miktarı profil ölçeğini aşamaz; fazla basamak kesilmez veya yuvarlanmaz.");
        }

        return new(canonical, unitId, profileId, profileVersion, dimension, scale);
    }

    /// <summary>Adds quantities only inside the same exact unit/profile/dimension context.</summary>
    public static StockQuantityV2 Add(StockQuantityV2 left, StockQuantityV2 right)
    {
        if (left.UnitId != right.UnitId
            || left.ProfileId != right.ProfileId
            || left.ProfileVersion != right.ProfileVersion
            || left.Dimension != right.Dimension
            || left.Scale != right.Scale)
        {
            throw Refused(StockQuantityRuleCodesV2.ContextMismatch,
                "Farklı birim, profil sürümü veya boyuttaki stok miktarları toplanamaz.");
        }

        return Positive(checked(left.Value + right.Value), left.UnitId, left.ProfileId,
            left.ProfileVersion, left.Dimension, left.Scale);
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    private static decimal Canonical(decimal value) =>
        value / 1.0000000000000000000000000000m;

    private static int DecimalScale(decimal value) =>
        (decimal.GetBits(value)[3] >> 16) & 0x7F;

    private static StockQuantityRuleExceptionV2 Refused(string code, string message) => new(code, message);
}
