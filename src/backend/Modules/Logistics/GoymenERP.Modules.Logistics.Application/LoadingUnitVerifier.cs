using System.Globalization;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Logistics.Application;

/// <summary>A line unit verified against the Settings profile, with the exact quantity in that unit.</summary>
/// <summary>
/// A line unit verified against the Settings profile, with the exact quantity in that unit.
/// <see cref="ProfileScale"/> is the owner's raw scale; <see cref="Scale"/> is the scale actually
/// applied to the quantity, min(profile, Ö2's 3), the one a consumer may offer for input
/// (LOG-3; the same shape as Purchasing's PUR-3 and Manufacturing's MFG-3 snapshots).
/// </summary>
public sealed record VerifiedLoadingUnit(Guid UnitId, string UnitCode, Guid ProfileId, string ProfileVersion,
    int ProfileScale, int Scale, Quantity Quantity);

/// <summary>Either a verified unit or a stable refusal code. A refusal is never a default unit; the caller does not add the line.</summary>
public sealed record LoadingUnitVerification(VerifiedLoadingUnit? Unit, string? Code)
{
    public bool IsVerified => Unit is not null;

    internal static LoadingUnitVerification Verified(VerifiedLoadingUnit unit) => new(unit, null);

    internal static LoadingUnitVerification Refused(string code) => new(null, code);
}

/// <summary>The refusal codes this verifier adds on top of the Settings owner's own codes.</summary>
public static class LoadingUnitCodes
{
    public const string ReferenceMissing = "logistics_unit_reference_missing";
    public const string UnitCompanyMismatch = "logistics_unit_company_mismatch";
    public const string UnitProfileInvalid = "logistics_unit_profile_invalid";
    public const string UnitUnsupported = "logistics_unit_unsupported";
    public const string QuantityNotPositive = "logistics_quantity_not_positive";
    public const string QuantityScaleExceeded = "logistics_quantity_scale_exceeded";
}

/// <summary>
/// Verifies a loading line's unit and quantity against the Settings unit profile before the line
/// is added (same pattern as Purchasing's verifier; Ö2). The Settings owner's port is the only
/// source: nothing here guesses a unit, a scale or a dimension.
/// <list type="bullet">
/// <item>Fail-closed: every <see cref="SettingsPrerequisiteException"/> becomes a refusal with the
/// owner's code kept verbatim (<c>settings_access_denied</c>, <c>settings_provider_unavailable</c>,
/// <see cref="SettingsPrerequisiteCodesV1.UnitProfileNotFound"/>, <c>settings_profile_invalid</c>).</item>
/// <item>The profile must belong to the SERVER-resolved company the caller passes, name the
/// requested unit, be internally valid, and be active on the effective date.</item>
/// <item>Only kilogram profiles are approved today (Settings README, Ö1); another dimension is
/// refused as unsupported rather than mapped to a guessed count unit.</item>
/// <item>Precision comes from the profile's scale, capped by Ö2's kilogram rule (at most 3):
/// more decimals are refused, never rounded or cut.</item>
/// </list>
/// The result is reference metadata for one line; it authorizes nothing.
/// </summary>
public sealed class LoadingUnitVerifier(ISettingsPrerequisiteProvider settings)
{
    public async Task<LoadingUnitVerification> VerifyAsync(Guid companyId, Guid unitId, decimal quantity,
        DateOnly effectiveOn, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty || unitId == Guid.Empty)
        {
            return LoadingUnitVerification.Refused(LoadingUnitCodes.ReferenceMissing);
        }

        if (quantity <= 0m)
        {
            return LoadingUnitVerification.Refused(LoadingUnitCodes.QuantityNotPositive);
        }

        UnitProfileV1 profile;
        try
        {
            profile = await settings.GetUnitProfileAsync(unitId, effectiveOn, cancellationToken);
        }
        catch (SettingsPrerequisiteException exception)
        {
            return LoadingUnitVerification.Refused(exception.Code);
        }

        if (profile is null)
        {
            return LoadingUnitVerification.Refused(LoadingUnitCodes.UnitProfileInvalid);
        }

        if (profile.CompanyId != companyId)
        {
            return LoadingUnitVerification.Refused(LoadingUnitCodes.UnitCompanyMismatch);
        }

        if (profile.UnitId != unitId || profile.Profile is null || profile.Profile.ProfileId == Guid.Empty
            || profile.Profile.Version <= 0 || profile.Scale < 0 || string.IsNullOrWhiteSpace(profile.UnitCode))
        {
            return LoadingUnitVerification.Refused(LoadingUnitCodes.UnitProfileInvalid);
        }

        if (!profile.IsActive || effectiveOn < profile.EffectiveFrom
            || (profile.EffectiveThrough is { } through && effectiveOn > through))
        {
            return LoadingUnitVerification.Refused(LoadingUnitCodes.UnitUnsupported);
        }

        if (!profile.IsKilogram)
        {
            // No count dimension is approved in Settings yet; refusing never invents a unit.
            return LoadingUnitVerification.Refused(LoadingUnitCodes.UnitUnsupported);
        }

        var appliedScale = Math.Min(profile.Scale, Quantity.KilogramScale);
        if (Quantity.SignificantDecimals(quantity) > appliedScale)
        {
            return LoadingUnitVerification.Refused(LoadingUnitCodes.QuantityScaleExceeded);
        }

        return LoadingUnitVerification.Verified(new VerifiedLoadingUnit(unitId, profile.UnitCode,
            profile.Profile.ProfileId, profile.Profile.Version.ToString(CultureInfo.InvariantCulture), profile.Scale,
            appliedScale, Quantity.Positive(quantity, QuantityUnit.Kilogram)));
    }
}
