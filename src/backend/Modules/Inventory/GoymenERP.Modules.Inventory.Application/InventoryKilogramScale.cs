using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>The refusal codes the kilogram scale check adds on top of the Settings owner's own codes.</summary>
public static class InventoryScaleCodes
{
    /// <summary>The Settings answer is for another company, another unit, or is not a usable kilogram profile.</summary>
    public const string ProfileUnusable = "inventory_kg_profile_unusable";

    /// <summary>A quantity has more decimals than the company's kilogram profile allows on that date.</summary>
    public const string ScaleExceeded = "inventory_kg_scale_exceeded";
}

/// <summary>The approved kilogram scale for one company on one date, as the Settings owner published it.</summary>
public sealed record InventoryKilogramProfile(Guid CompanyId, Guid ProfileId, long ProfileVersion, int Scale);

/// <summary>Either the approved kilogram profile or a stable refusal code; never a default scale.</summary>
public sealed record InventoryKilogramScaleCheck(InventoryKilogramProfile? Profile, string? Code)
{
    public bool IsAccepted => Profile is not null;
}

/// <summary>
/// Checks movement quantities against the company's approved kilogram unit profile (T02-a,
/// Ö2) through the Settings owner's port. Replaces INV-1's fixed "3 decimals" with the
/// profile's own scale, and never widens it: Ö2 caps kilograms at 3 decimals whatever a
/// profile says, so the applied scale is <c>min(profile scale, 3)</c>.
/// <list type="bullet">
/// <item>Fail-closed: every <see cref="SettingsPrerequisiteException"/> is a refusal carrying the
/// owner's code verbatim (<c>settings_access_denied</c>, <c>settings_provider_unavailable</c>,
/// <see cref="SettingsPrerequisiteCodesV1.UnitProfileNotFound"/>, <c>settings_profile_invalid</c>),
/// so "denied", "unavailable" and "no profile" stay distinct.</item>
/// <item>The answer must be the server-resolved company's, for
/// <see cref="SettingsUnitIdentitiesV1.Kilogram"/>, a kilogram dimension, active and effective on
/// the date; anything else is <see cref="InventoryScaleCodes.ProfileUnusable"/>.</item>
/// <item>More decimals than the applied scale is <see cref="InventoryScaleCodes.ScaleExceeded"/>:
/// refused, never rounded or cut.</item>
/// </list>
/// Which Product's stock unit is kilogram is Product's binding, not checked here.
/// </summary>
public sealed class InventoryKilogramScale(ISettingsPrerequisiteProvider settings)
{
    public async Task<InventoryKilogramScaleCheck> CheckAsync(Guid companyId, DateOnly effectiveOn,
        IEnumerable<Kilograms> quantities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quantities);
        var values = quantities.ToArray();
        if (companyId == Guid.Empty)
        {
            return new(null, InventoryScaleCodes.ProfileUnusable);
        }

        UnitProfileV1 profile;
        try
        {
            profile = await settings.GetUnitProfileAsync(SettingsUnitIdentitiesV1.Kilogram, effectiveOn, cancellationToken);
        }
        catch (SettingsPrerequisiteException exception)
        {
            return new(null, exception.Code);
        }

        if (profile is null || profile.CompanyId != companyId || profile.UnitId != SettingsUnitIdentitiesV1.Kilogram
            || !profile.IsKilogram || profile.Profile is null || profile.Profile.ProfileId == Guid.Empty
            || profile.Profile.Version <= 0 || profile.Scale < 0 || !profile.IsActive
            || effectiveOn < profile.EffectiveFrom
            || (profile.EffectiveThrough is { } through && effectiveOn > through))
        {
            return new(null, InventoryScaleCodes.ProfileUnusable);
        }

        var applied = Math.Min(profile.Scale, Kilograms.MaximumDecimals);
        if (values.Any(quantity => Decimals(quantity.Value) > applied))
        {
            return new(null, InventoryScaleCodes.ScaleExceeded);
        }

        return new(new InventoryKilogramProfile(companyId, profile.Profile.ProfileId, profile.Profile.Version, applied), null);
    }

    /// <summary>Decimals that carry a digit: <see cref="Kilograms"/> already drops trailing zeros.</summary>
    private static int Decimals(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xFF;
}
