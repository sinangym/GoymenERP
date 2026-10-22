using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// The accepted treatment of one consumable by an Inventory operation. A direct-expense card is
/// deliberately represented so callers cannot silently turn it into a stock item.
/// </summary>
public enum ConsumableInventoryRepresentationV1
{
    StockTracked = 1,
    DirectExpense = 2,
}

/// <summary>
/// Inventory-owned consumer seam for an İşletme Alımları consumable snapshot. It is not a card
/// writer, authority grant, unit catalogue, conversion service or stock movement. The future
/// producer remains responsible for resolving the server company and the exact stored version.
/// Missing and foreign-company cards use the same null result; unavailable or corrupt producers
/// fail closed rather than returning a positive snapshot.
/// </summary>
public interface IConsumableStockProfileSourceV1
{
    Task<ConsumableStockProfileSnapshotV1?> ReadAsync(
        ConsumableStockProfileSelectionV1 selection,
        CancellationToken cancellationToken = default);
}

public sealed record ConsumableStockProfileSelectionV1(
    Guid ConsumableId,
    string ExpectedConsumableVersion,
    DateOnly EffectiveOn);

/// <summary>
/// Immutable company/version fact returned by the consumable owner. Lifecycle is intentionally an
/// opaque owner code: this contract carries it but does not invent lifecycle states. UnitProfile is
/// the exact Settings-owned profile effective for the selection date; it is never defaulted.
/// </summary>
public sealed record ConsumableStockProfileSnapshotV1
{
    private const int MaximumTokenLength = 64;

    public ConsumableStockProfileSnapshotV1(
        Guid companyId,
        Guid consumableId,
        string consumableVersion,
        string lifecycleCode,
        ConsumableInventoryRepresentationV1 representation,
        UnitProfileV1 unitProfile,
        DateOnly effectiveOn)
    {
        if (companyId == Guid.Empty)
        {
            throw Refused(ConsumableStockProfileCodesV1.InvalidCompany,
                "Şirket kimliği boş olamaz.");
        }

        if (consumableId == Guid.Empty)
        {
            throw Refused(ConsumableStockProfileCodesV1.InvalidConsumable,
                "Sarf kartı kimliği boş olamaz.");
        }

        RequireToken(consumableVersion, ConsumableStockProfileCodesV1.InvalidVersion,
            "Sarf kartı sürümü kanonik olmalıdır.");
        RequireToken(lifecycleCode, ConsumableStockProfileCodesV1.InvalidLifecycle,
            "Sarf kartı yaşam döngüsü kodu kanonik olmalıdır.");

        if (!Enum.IsDefined(representation))
        {
            throw Refused(ConsumableStockProfileCodesV1.InvalidRepresentation,
                "Sarf kartı stok veya doğrudan gider olarak açıkça temsil edilmelidir.");
        }

        ValidateProfile(companyId, unitProfile, effectiveOn);

        CompanyId = companyId;
        ConsumableId = consumableId;
        ConsumableVersion = consumableVersion;
        LifecycleCode = lifecycleCode;
        Representation = representation;
        UnitProfile = unitProfile;
        EffectiveOn = effectiveOn;
    }

    public Guid CompanyId { get; }
    public Guid ConsumableId { get; }
    public string ConsumableVersion { get; }
    public string LifecycleCode { get; }
    public ConsumableInventoryRepresentationV1 Representation { get; }
    public UnitProfileV1 UnitProfile { get; }
    public DateOnly EffectiveOn { get; }

    private static void ValidateProfile(Guid companyId, UnitProfileV1? profile, DateOnly effectiveOn)
    {
        if (profile is null
            || profile.CompanyId != companyId
            || profile.UnitId == Guid.Empty
            || profile.Profile is null
            || profile.Profile.ProfileId == Guid.Empty
            || profile.Profile.Version <= 0
            || profile.Scale is < 0 or > 28)
        {
            throw Refused(ConsumableStockProfileCodesV1.InvalidUnitProfile,
                "Sarf kartı birim profili aynı şirkete ait, sürümlü ve geçerli olmalıdır.");
        }

        RequireToken(profile.UnitCode, ConsumableStockProfileCodesV1.InvalidUnitProfile,
            "Birim kodu kanonik olmalıdır.");
        RequireToken(profile.DimensionCode, ConsumableStockProfileCodesV1.InvalidUnitProfile,
            "Birim boyutu kodu kanonik olmalıdır.");

        if (!profile.IsActive)
        {
            throw Refused(ConsumableStockProfileCodesV1.InactiveUnitProfile,
                "Sarf kartı birim profili aktif olmalıdır.");
        }

        if (effectiveOn == default
            || effectiveOn < profile.EffectiveFrom
            || (profile.EffectiveThrough is not null && effectiveOn > profile.EffectiveThrough.Value))
        {
            throw Refused(ConsumableStockProfileCodesV1.UnitProfileNotEffective,
                "Sarf kartı birim profili işlem tarihinde geçerli olmalıdır.");
        }
    }

    private static void RequireToken(string? value, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumTokenLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl))
        {
            throw Refused(code, message);
        }
    }

    private static ConsumableStockProfileContractExceptionV1 Refused(string code, string message) =>
        new(code, message);
}

public static class ConsumableStockProfileCodesV1
{
    public const string InvalidCompany = "inventory_consumable_stock_profile_company_invalid";
    public const string InvalidConsumable = "inventory_consumable_stock_profile_consumable_invalid";
    public const string InvalidVersion = "inventory_consumable_stock_profile_version_invalid";
    public const string InvalidLifecycle = "inventory_consumable_stock_profile_lifecycle_invalid";
    public const string InvalidRepresentation = "inventory_consumable_stock_profile_representation_invalid";
    public const string InvalidUnitProfile = "inventory_consumable_stock_profile_unit_invalid";
    public const string InactiveUnitProfile = "inventory_consumable_stock_profile_unit_inactive";
    public const string UnitProfileNotEffective = "inventory_consumable_stock_profile_unit_not_effective";
}

public sealed class ConsumableStockProfileContractExceptionV1(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
