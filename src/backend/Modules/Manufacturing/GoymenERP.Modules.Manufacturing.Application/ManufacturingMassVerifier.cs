using System.Globalization;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Manufacturing.Application;

/// <summary>
/// A mass verified against the company's kilogram profile, with the profile snapshot the
/// Settings port asks consumers to keep with the fact they record. <see cref="ProfileScale"/> is
/// the owner's raw scale; <see cref="Scale"/> is the scale actually applied, min(profile, Ö2's 3),
/// which is what a consumer may offer for input or display (MFG-3, ÜRÜN #292 P3-1; PUR-3's shape).
/// <see cref="ProfileVersion"/> is the invariant-culture text of the owner's version, as in
/// Purchasing and Logistics (MFG-3, SİPARİŞ #292 P3-b).
/// </summary>
public sealed record VerifiedMass(Kilograms Mass, Guid UnitId, Guid ProfileId, string ProfileVersion,
    int ProfileScale, int Scale);

/// <summary>
/// Either a verified mass or a stable refusal code. A refusal is never a zero mass or a default
/// profile; the caller does not record the fact.
/// </summary>
public sealed record MassVerification(VerifiedMass? Mass, string? Code)
{
    public bool IsVerified => Mass is not null;

    internal static MassVerification Verified(VerifiedMass mass) => new(mass, null);

    internal static MassVerification Refused(string code) => new(null, code);
}

/// <summary>The refusal codes this verifier adds on top of the Settings owner's own codes.</summary>
public static class ManufacturingMassCodes
{
    public const string CompanyMissing = "manufacturing_mass_company_missing";
    public const string MassNegative = "manufacturing_mass_negative";
    public const string ProfileCompanyMismatch = "manufacturing_kilogram_profile_company_mismatch";
    public const string ProfileInvalid = "manufacturing_kilogram_profile_invalid";
    public const string ProfileNotInEffect = "manufacturing_kilogram_profile_not_in_effect";
    public const string ScaleExceeded = "manufacturing_mass_scale_exceeded";
}

/// <summary>
/// Verifies a measured mass against the company's approved kilogram unit profile before a
/// realization fact is recorded with it (Ö2; the MFG-1 domain holds only the arithmetic rule).
/// The Settings owner's port (Contracts.V1) is the only source: nothing here guesses a unit, a
/// scale or a profile, and no concrete Settings provider is referenced.
/// <list type="bullet">
/// <item>Fail-closed: every <see cref="SettingsPrerequisiteException"/> becomes a refusal with the
/// owner's code kept verbatim (e.g. <c>settings_access_denied</c>, <c>settings_provider_unavailable</c>,
/// <see cref="SettingsPrerequisiteCodesV1.UnitProfileNotFound"/>, <c>settings_profile_invalid</c>),
/// so "denied", "unavailable" and "no profile" stay distinct.</item>
/// <item>Manufacturing masses are kilograms only, so the unit asked about is always
/// <see cref="SettingsUnitIdentitiesV1.Kilogram"/>. The profile must belong to the SERVER-resolved
/// company the caller passes, name that unit, be a kilogram profile, be internally valid and be
/// active on the effective date.</item>
/// <item>Precision is the profile's scale capped by Ö2's kilogram rule (at most 3): a mass with more
/// decimals is refused, never rounded or cut. Trailing zeros are not precision.</item>
/// <item>A zero mass is a valid reading (a WIP measurement can be zero); whether a fact needs a
/// positive mass stays the order's rule. A negative mass is refused here.</item>
/// </list>
/// The result is reference metadata for one fact; it authorizes nothing and is not a production policy.
/// </summary>
public sealed class ManufacturingMassVerifier(ISettingsPrerequisiteProvider settings)
{
    public async Task<MassVerification> VerifyAsync(Guid companyId, decimal mass, DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return MassVerification.Refused(ManufacturingMassCodes.CompanyMissing);
        }

        if (mass < 0m)
        {
            return MassVerification.Refused(ManufacturingMassCodes.MassNegative);
        }

        var kilogram = SettingsUnitIdentitiesV1.Kilogram;
        UnitProfileV1 profile;
        try
        {
            profile = await settings.GetUnitProfileAsync(kilogram, effectiveOn, cancellationToken);
        }
        catch (SettingsPrerequisiteException exception)
        {
            return MassVerification.Refused(exception.Code);
        }

        if (profile is null)
        {
            return MassVerification.Refused(ManufacturingMassCodes.ProfileInvalid);
        }

        if (profile.CompanyId != companyId)
        {
            return MassVerification.Refused(ManufacturingMassCodes.ProfileCompanyMismatch);
        }

        // The kilogram identity answered with anything but a well-formed kilogram profile is the
        // owner's data being inconsistent, not a different unit to fall back to.
        if (profile.UnitId != kilogram || !profile.IsKilogram || profile.Profile is null
            || profile.Profile.ProfileId == Guid.Empty || profile.Profile.Version <= 0 || profile.Scale < 0)
        {
            return MassVerification.Refused(ManufacturingMassCodes.ProfileInvalid);
        }

        if (!profile.IsActive || effectiveOn < profile.EffectiveFrom
            || (profile.EffectiveThrough is { } through && effectiveOn > through))
        {
            return MassVerification.Refused(ManufacturingMassCodes.ProfileNotInEffect);
        }

        var appliedScale = Math.Min(profile.Scale, Kilograms.Scale);
        if (SignificantDecimals(mass) > appliedScale)
        {
            return MassVerification.Refused(ManufacturingMassCodes.ScaleExceeded);
        }

        return MassVerification.Verified(new VerifiedMass(new Kilograms(mass), kilogram, profile.Profile.ProfileId,
            profile.Profile.Version.ToString(CultureInfo.InvariantCulture), profile.Scale, appliedScale));
    }

    /// <summary>The number of decimals that carry a non-zero digit: 12.5000 → 1, 12.5001 → 4, 1000 → 0.</summary>
    private static int SignificantDecimals(decimal value)
    {
        var decimals = 0;
        while (decimal.Round(value, decimals) != value)
        {
            decimals++;
        }

        return decimals;
    }
}
