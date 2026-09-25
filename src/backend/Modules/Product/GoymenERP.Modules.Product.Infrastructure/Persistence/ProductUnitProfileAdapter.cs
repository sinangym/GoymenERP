using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// The owner's exact profile for a Product unit reference, and whether it can be used on
/// the requested date. <see cref="IsUsable"/> is false when the profile is inactive,
/// outside its effective window, or not kilogram where kilogram is required. Those are
/// business answers about a well-formed profile; corrupt or foreign provider data is
/// never returned here.
/// </summary>
public sealed record ProductUnitProfileRead(UnitProfileV1 Profile, bool IsUsable);

/// <summary>
/// Validates the Settings-owned unit profile for a Product reference. This is not
/// Product activation, conversion validation or sales eligibility. The provider
/// must authorize each call; no profile cache or successful fallback is supplied.
///
/// Two failure kinds are kept apart. A profile that is malformed, foreign or
/// self-contradictory (window ending before it starts) is provider data the caller
/// cannot act on: <c>settings_profile_invalid</c>. A well-formed profile that is inactive,
/// not effective on the date, or not kilogram where required is returned with
/// <see cref="ProductUnitProfileRead.IsUsable"/> false, so the caller reports it as a
/// rejection of the reference rather than as an unavailable provider.
/// </summary>
public sealed class ProductUnitProfileAdapter(ISettingsPrerequisiteProvider provider, ICompanyContext companyContext)
{
    public async Task<ProductUnitProfileRead> ReadAsync(Guid unitId, DateOnly effectiveOn, bool requiresKilogram,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var companyId = RequireCompany();
        if (unitId == Guid.Empty) throw new SettingsPrerequisiteException("settings_profile_invalid");

        UnitProfileV1 profile;
        try
        {
            profile = await provider.GetUnitProfileAsync(unitId, effectiveOn, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            RequireSameCompany(companyId);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        RequireSameCompany(companyId);
        if (profile is null || profile.CompanyId != companyId || profile.UnitId != unitId
            || profile.Profile is null || profile.Profile.ProfileId == Guid.Empty || profile.Profile.Version <= 0
            || string.IsNullOrWhiteSpace(profile.UnitCode) || string.IsNullOrWhiteSpace(profile.DimensionCode)
            || profile.Scale < 0
            || (profile.EffectiveThrough is DateOnly reversed && reversed < profile.EffectiveFrom))
        {
            throw new SettingsPrerequisiteException("settings_profile_invalid");
        }

        var usable = profile.IsActive
            && effectiveOn >= profile.EffectiveFrom
            && (profile.EffectiveThrough is not DateOnly through || effectiveOn <= through)
            && (!requiresKilogram || profile.IsKilogram);

        // Preserve the owner's exact profile. Symbol/dimension equality is not
        // conversion evidence, and neither the scale nor kilogram identity is inferred.
        return new(profile, usable);
    }

    private Guid RequireCompany()
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
            throw new SettingsPrerequisiteException("settings_access_denied");
        return companyContext.CompanyId;
    }

    private void RequireSameCompany(Guid expected)
    {
        if (RequireCompany() != expected) throw new SettingsPrerequisiteException("settings_access_denied");
    }
}
