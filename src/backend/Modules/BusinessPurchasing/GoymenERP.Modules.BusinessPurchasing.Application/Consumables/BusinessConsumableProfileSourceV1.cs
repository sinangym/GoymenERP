using GoymenERP.Modules.BusinessPurchasing.Domain.Consumables;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.BusinessPurchasing.Application.Consumables;

/// <summary>
/// Producer-owned read seam for a company-scoped consumable profile. Implementations must
/// authorize the read and revalidate the exact card version before releasing a snapshot.
/// </summary>
public interface IBusinessConsumableProfileSourceV1
{
    Task<BusinessConsumableProfileSnapshotV1?> ReadAsync(
        BusinessConsumableProfileSelectionV1 selection,
        CancellationToken cancellationToken = default);
}

public sealed record BusinessConsumableProfileSelectionV1
{
    public BusinessConsumableProfileSelectionV1(
        Guid companyId,
        Guid consumableId,
        string expectedConsumableVersion,
        DateOnly effectiveOn)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("A company identity is required.", nameof(companyId));
        if (consumableId == Guid.Empty)
            throw new ArgumentException("A consumable identity is required.", nameof(consumableId));
        if (string.IsNullOrWhiteSpace(expectedConsumableVersion))
            throw new ArgumentException("An expected consumable version is required.", nameof(expectedConsumableVersion));
        if (effectiveOn == default)
            throw new ArgumentOutOfRangeException(nameof(effectiveOn), "An effective date is required.");

        CompanyId = companyId;
        ConsumableId = consumableId;
        ExpectedConsumableVersion = expectedConsumableVersion;
        EffectiveOn = effectiveOn;
    }

    public Guid CompanyId { get; }
    public Guid ConsumableId { get; }
    public string ExpectedConsumableVersion { get; }
    public DateOnly EffectiveOn { get; }
}

/// <summary>
/// Immutable producer snapshot bound to the exact requested card version and Settings profile.
/// It is source data only and does not authorize or record an Inventory effect.
/// </summary>
public sealed record BusinessConsumableProfileSnapshotV1
{
    public BusinessConsumableProfileSnapshotV1(
        BusinessConsumableProfileSelectionV1 selection,
        BusinessConsumableCardV1 card,
        UnitProfileV1 unitProfile)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(unitProfile);

        if (card.CompanyId != selection.CompanyId)
            throw SourceMismatch("The consumable card belongs to another company.");
        if (card.ConsumableId != selection.ConsumableId)
            throw SourceMismatch("The consumable card does not match the requested identity.");
        if (!string.Equals(card.Version, selection.ExpectedConsumableVersion, StringComparison.Ordinal))
            throw SourceMismatch("The consumable card version is stale.");
        if (unitProfile.CompanyId != selection.CompanyId)
            throw SourceMismatch("The Settings unit profile belongs to another company.");
        if (unitProfile.Profile is null
            || unitProfile.UnitId != card.UnitProfile.UnitId
            || unitProfile.Profile.ProfileId != card.UnitProfile.ProfileId
            || unitProfile.Profile.Version != card.UnitProfile.ProfileVersion)
        {
            throw SourceMismatch("The Settings unit profile does not match the card reference.");
        }
        if (!unitProfile.IsActive)
            throw SourceMismatch("The Settings unit profile is inactive.");
        if (selection.EffectiveOn < unitProfile.EffectiveFrom
            || unitProfile.EffectiveThrough is { } through && selection.EffectiveOn > through)
        {
            throw SourceMismatch("The Settings unit profile is not effective on the requested date.");
        }

        Selection = selection;
        Card = card;
        UnitProfile = unitProfile;
    }

    public BusinessConsumableProfileSelectionV1 Selection { get; }
    public BusinessConsumableCardV1 Card { get; }
    public UnitProfileV1 UnitProfile { get; }

    private static InvalidOperationException SourceMismatch(string message) => new(message);
}
