using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// Validates the Settings-owned unit profile for a Product reference. This is not
/// Product activation, conversion validation or sales eligibility. The provider
/// must authorize each call; no profile cache or successful fallback is supplied.
/// </summary>
public sealed class ProductUnitProfileAdapter(ISettingsPrerequisiteProvider provider, ICompanyContext companyContext)
{
    public async Task<UnitProfileV1> ReadAsync(Guid unitId, DateOnly effectiveOn, bool requiresKilogram,
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
            || profile.Scale < 0 || !profile.IsActive
            || effectiveOn < profile.EffectiveFrom
            || (profile.EffectiveThrough is DateOnly through && (through < profile.EffectiveFrom || effectiveOn > through))
            || (requiresKilogram && !profile.IsKilogram))
        {
            throw new SettingsPrerequisiteException("settings_profile_invalid");
        }

        // Preserve the owner's exact profile. Symbol/dimension equality is not
        // conversion evidence, and neither the scale nor kilogram identity is inferred.
        return profile;
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
