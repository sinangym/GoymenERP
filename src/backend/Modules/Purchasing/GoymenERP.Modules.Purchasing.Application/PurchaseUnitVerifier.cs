using System.Globalization;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>
/// A line unit verified against the Settings profile, with the exact quantity in that unit.
/// <see cref="ProfileScale"/> is the owner's raw scale; <see cref="Scale"/> is the scale actually
/// applied to the quantity, min(profile, Ö2's 3), so a consumer offering an input limit uses the
/// one the domain will accept (ÜRÜN #288 P3-2).
/// </summary>
public sealed record VerifiedPurchaseUnit(Guid UnitId, string UnitCode, Guid ProfileId, string ProfileVersion,
    int ProfileScale, int Scale, Quantity Quantity);

/// <summary>
/// Either a verified unit or a stable refusal code. A refusal is never an empty selection or a
/// default unit; the caller does not add the line.
/// </summary>
public sealed record PurchaseUnitVerification(VerifiedPurchaseUnit? Unit, string? Code)
{
    public bool IsVerified => Unit is not null;

    internal static PurchaseUnitVerification Verified(VerifiedPurchaseUnit unit) => new(unit, null);

    internal static PurchaseUnitVerification Refused(string code) => new(null, code);
}

/// <summary>
/// The refusal codes this verifier adds on top of the Settings owner's own codes. The owner's
/// "no profile" code is <see cref="SettingsPrerequisiteCodesV1.UnitProfileNotFound"/> (Contracts.V1,
/// #287); it is passed through, never copied here.
/// </summary>
public static class PurchaseUnitCodes
{
    public const string ReferenceMissing = "purchasing_unit_reference_missing";
    public const string UnitCompanyMismatch = "purchasing_unit_company_mismatch";
    public const string UnitProfileInvalid = "purchasing_unit_profile_invalid";
    public const string UnitUnsupported = "purchasing_unit_unsupported";
    public const string QuantityNotPositive = "purchasing_quantity_not_positive";
    public const string QuantityScaleExceeded = "purchasing_quantity_scale_exceeded";
}

/// <summary>
/// Verifies a purchase line's unit and quantity against the Settings unit profile before the
/// line is added to a request or order (ÜRÜN #278 P3-2; Ö2). The Settings owner's port is the
/// only source: nothing here guesses a unit, a scale or a dimension.
/// <list type="bullet">
/// <item>Fail-closed: every <see cref="SettingsPrerequisiteException"/> becomes a refusal with the
/// owner's code kept verbatim (<c>settings_access_denied</c>, <c>settings_provider_unavailable</c>,
/// <see cref="SettingsPrerequisiteCodesV1.UnitProfileNotFound"/>, <c>settings_profile_invalid</c>), so a
/// consumer can still tell "denied" from "unavailable" from "no profile".</item>
/// <item>The profile must belong to the SERVER-resolved company the caller passes, name the
/// requested unit, be internally valid, and be active on the effective date.</item>
/// <item>Kilogram profiles and, from AK-3 (SAT-D434/SAT-D435, KLN-D119), the Settings standard piece
/// identity in the count dimension are approved. Metre (SAT-D443) and every other dimension are refused
/// as unsupported rather than mapped to a guessed unit.</item>
/// <item>Precision comes from the profile's scale, capped by Ö2's kilogram rule (at most 3) or the
/// piece rule (0): a quantity with more decimals is refused, never rounded or cut.</item>
/// </list>
/// The result is reference metadata for one line; it authorizes nothing and is not a purchase policy.
/// </summary>
public sealed class PurchaseUnitVerifier(ISettingsPrerequisiteProvider settings)
{
    public async Task<PurchaseUnitVerification> VerifyAsync(Guid companyId, Guid unitId, decimal quantity,
        DateOnly effectiveOn, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty || unitId == Guid.Empty)
        {
            return PurchaseUnitVerification.Refused(PurchaseUnitCodes.ReferenceMissing);
        }

        if (quantity <= 0m)
        {
            return PurchaseUnitVerification.Refused(PurchaseUnitCodes.QuantityNotPositive);
        }

        UnitProfileV1 profile;
        try
        {
            profile = await settings.GetUnitProfileAsync(unitId, effectiveOn, cancellationToken);
        }
        catch (SettingsPrerequisiteException exception)
        {
            return PurchaseUnitVerification.Refused(exception.Code);
        }

        if (profile is null)
        {
            return PurchaseUnitVerification.Refused(PurchaseUnitCodes.UnitProfileInvalid);
        }

        if (profile.CompanyId != companyId)
        {
            return PurchaseUnitVerification.Refused(PurchaseUnitCodes.UnitCompanyMismatch);
        }

        if (profile.UnitId != unitId || profile.Profile is null || profile.Profile.ProfileId == Guid.Empty
            || profile.Profile.Version <= 0 || profile.Scale < 0 || string.IsNullOrWhiteSpace(profile.UnitCode))
        {
            return PurchaseUnitVerification.Refused(PurchaseUnitCodes.UnitProfileInvalid);
        }

        if (!profile.IsActive || effectiveOn < profile.EffectiveFrom
            || (profile.EffectiveThrough is { } through && effectiveOn > through))
        {
            return PurchaseUnitVerification.Refused(PurchaseUnitCodes.UnitUnsupported);
        }

        var unit = PieceUnitOf(profile);
        if (!profile.IsKilogram && unit is null)
        {
            // AK-3 (SAT-D434/SAT-D435, KLN-D119): besides kilograms only the Settings standard piece identity with the count
            // dimension is approved; metre and every other dimension stay refused (SAT-D443), never mapped to a guessed unit.
            return PurchaseUnitVerification.Refused(PurchaseUnitCodes.UnitUnsupported);
        }

        var quantityUnit = unit ?? QuantityUnit.Kilogram;
        var appliedScale = Math.Min(profile.Scale, Quantity.AllowedScale(quantityUnit));
        if (Quantity.SignificantDecimals(quantity) > appliedScale)
        {
            return PurchaseUnitVerification.Refused(PurchaseUnitCodes.QuantityScaleExceeded);
        }

        return PurchaseUnitVerification.Verified(new VerifiedPurchaseUnit(unitId, profile.UnitCode,
            profile.Profile.ProfileId, profile.Profile.Version.ToString(CultureInfo.InvariantCulture), profile.Scale,
            appliedScale, Quantity.Positive(quantity, quantityUnit)));
    }

    /// <summary>The piece unit for the Settings standard piece identity in the count dimension; null for anything else.</summary>
    private static QuantityUnit? PieceUnitOf(UnitProfileV1 profile) =>
        !profile.IsKilogram && profile.UnitId == SettingsUnitIdentitiesV1.Piece
        && string.Equals(profile.DimensionCode, PieceDimensionCode, StringComparison.Ordinal)
            ? QuantityUnit.Piece
            : null;

    /// <summary>The Settings dimension code of the standard piece identity (ApprovedUnitProfileCatalog, AK-1).</summary>
    public const string PieceDimensionCode = "count";
}
