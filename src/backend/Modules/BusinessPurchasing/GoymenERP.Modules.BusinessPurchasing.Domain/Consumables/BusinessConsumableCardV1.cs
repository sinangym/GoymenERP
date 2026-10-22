namespace GoymenERP.Modules.BusinessPurchasing.Domain.Consumables;

public enum BusinessConsumableInventoryRepresentationV1
{
    StockTracked = 1,
    DirectExpense = 2,
}

/// <summary>
/// Immutable reference to the exact Settings unit profile selected for a consumable card.
/// The BusinessPurchasing domain preserves the reference but does not own the profile.
/// </summary>
public sealed record BusinessConsumableUnitProfileReferenceV1
{
    public BusinessConsumableUnitProfileReferenceV1(Guid unitId, Guid profileId, long profileVersion)
    {
        if (unitId == Guid.Empty)
            throw new ArgumentException("A unit identity is required.", nameof(unitId));
        if (profileId == Guid.Empty)
            throw new ArgumentException("A unit profile identity is required.", nameof(profileId));
        if (profileVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(profileVersion), "A positive unit profile version is required.");

        UnitId = unitId;
        ProfileId = profileId;
        ProfileVersion = profileVersion;
    }

    public Guid UnitId { get; }
    public Guid ProfileId { get; }
    public long ProfileVersion { get; }
}

/// <summary>
/// Canonical İşletme Alımları consumable identity and stock representation.
/// This card does not create a stock, ledger, purchasing or financial effect.
/// </summary>
public sealed record BusinessConsumableCardV1
{
    public BusinessConsumableCardV1(
        Guid companyId,
        Guid consumableId,
        string version,
        string lifecycleCode,
        BusinessConsumableInventoryRepresentationV1 inventoryRepresentation,
        BusinessConsumableUnitProfileReferenceV1 unitProfile)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("A company identity is required.", nameof(companyId));
        if (consumableId == Guid.Empty)
            throw new ArgumentException("A consumable identity is required.", nameof(consumableId));
        RequireToken(version, nameof(version));
        RequireToken(lifecycleCode, nameof(lifecycleCode));
        if (inventoryRepresentation is not BusinessConsumableInventoryRepresentationV1.StockTracked
            and not BusinessConsumableInventoryRepresentationV1.DirectExpense)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inventoryRepresentation),
                "A supported consumable inventory representation is required.");
        }

        CompanyId = companyId;
        ConsumableId = consumableId;
        Version = version;
        LifecycleCode = lifecycleCode;
        InventoryRepresentation = inventoryRepresentation;
        UnitProfile = unitProfile ?? throw new ArgumentNullException(nameof(unitProfile));
    }

    public Guid CompanyId { get; }
    public Guid ConsumableId { get; }
    public string Version { get; }
    public string LifecycleCode { get; }
    public BusinessConsumableInventoryRepresentationV1 InventoryRepresentation { get; }
    public BusinessConsumableUnitProfileReferenceV1 UnitProfile { get; }

    private static void RequireToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A non-empty token is required.", parameterName);
    }
}
