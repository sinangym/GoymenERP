namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// F-004 (BS-9/BS-13/BS-16/BS-17 of <c>docs/decisions/SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md</c>; URN-D7…D9 of
/// <c>docs/decisions/SINAN_KARARLARI_2026-11-12_URUN_EK1.md</c>): the Settings owner's name for maintaining the company's unit
/// conversion policy as a persistent, versioned Settings record. Registered, NOT granted: no role, seed or user receives it
/// here (a human grant gate). Company-wide only. Not Critical: no approved source adds grant-time dual control.
/// </summary>
public static class SettingsUnitConversionPolicyPermissionManifestV1
{
    /// <summary>Read the unit conversion policy timeline and record new versions.</summary>
    public const string Manage = "settings.unit-conversion-policies.manage";
}

/// <summary>
/// The closed list of rounding mode codes a unit conversion policy carries (each is a <see cref="MidpointRounding"/> name).
/// Which mode a company uses is a Settings value (BS-13, URN-D8); nothing here prefers one.
/// </summary>
public static class UnitConversionRoundingModesV1
{
    public const string ToEven = nameof(MidpointRounding.ToEven);
    public const string AwayFromZero = nameof(MidpointRounding.AwayFromZero);
    public const string ToZero = nameof(MidpointRounding.ToZero);
    public const string ToNegativeInfinity = nameof(MidpointRounding.ToNegativeInfinity);
    public const string ToPositiveInfinity = nameof(MidpointRounding.ToPositiveInfinity);

    public static IReadOnlyList<string> All { get; } = [ToEven, AwayFromZero, ToZero, ToNegativeInfinity, ToPositiveInfinity];
}

/// <summary>Whether a weighbridge deviation exactly equal to the tolerance passes (BS-16; the value is a Settings choice).</summary>
public static class UnitConversionToleranceBoundariesV1
{
    /// <summary>deviation &lt;= tolerance passes.</summary>
    public const string Inclusive = "Inclusive";

    /// <summary>deviation &lt; tolerance passes; a deviation equal to the tolerance fails.</summary>
    public const string Exclusive = "Exclusive";

    public static IReadOnlyList<string> All { get; } = [Inclusive, Exclusive];
}

/// <summary>Which kg the weighbridge deviation percent is measured against (BS-17; the value is a Settings choice).</summary>
public static class UnitConversionToleranceBasesV1
{
    /// <summary>|weighed - theoretical| / theoretical (the calculated kg).</summary>
    public const string TheoreticalKg = "TheoreticalKg";

    /// <summary>|weighed - theoretical| / weighed.</summary>
    public const string WeighedKg = "WeighedKg";

    public static IReadOnlyList<string> All { get; } = [TheoreticalKg, WeighedKg];
}

/// <summary>
/// The provider's typed tolerance boundary. The numbers equal the consumer calculator's own enum members of the same meaning,
/// so a consumer maps by meaning without a table of magic numbers.
/// </summary>
public enum SettingsWeighbridgeToleranceBoundaryV1
{
    Inclusive = 1,
    Exclusive = 2,
}

/// <summary>The provider's typed tolerance base (same numbering rule as <see cref="SettingsWeighbridgeToleranceBoundaryV1"/>).</summary>
public enum SettingsWeighbridgeToleranceBaseV1
{
    TheoreticalKg = 1,
    WeighedKg = 2,
}

/// <summary>Technical bounds of the inputs (storage and transport shape, not policy).</summary>
public static class UnitConversionPolicyInputLimitsV1
{
    /// <summary>SAT-D68 precedent: the mandatory reason of every change.</summary>
    public const int ReasonMaximumLength = 4000;

    /// <summary>
    /// The largest number of decimals a coefficient or document total may be rounded to. A technical guard equal to the
    /// conversion calculator's own guard (decimal keeps kg values far inside its significant digits), not a business value.
    /// </summary>
    public const int MaximumRoundingDecimals = 12;

    /// <summary>Integer digits of a density in g/cm³ (storage shape decimal(12,6)).</summary>
    public const int DensityMaximumIntegerDigits = 6;

    /// <summary>Fraction digits of a density in g/cm³; a longer value is refused, never rounded.</summary>
    public const int DensityMaximumScale = 6;

    /// <summary>Integer digits of the tolerance percent (0…100; storage shape decimal(7,4)).</summary>
    public const int PercentMaximumIntegerDigits = 3;

    /// <summary>Fraction digits of the tolerance percent; a longer value is refused, never rounded.</summary>
    public const int PercentMaximumScale = 4;

    /// <summary>A percent's lower bound.</summary>
    public const decimal MinimumPercent = 0m;

    /// <summary>A percent's upper bound (a percent of the base kg).</summary>
    public const decimal MaximumPercent = 100m;
}

/// <summary>Published problem codes of the unit conversion policy routes.</summary>
public static class UnitConversionPolicyProblemCodesV1
{
    public const string RequestInvalid = "settings_unit_conversion_policy_request_invalid";
    public const string CommandMetadataInvalid = "settings_unit_conversion_policy_command_metadata_invalid";
    public const string ReasonRequired = "settings_unit_conversion_policy_reason_required";
    public const string ReasonTooLong = "settings_unit_conversion_policy_reason_too_long";
    public const string IdempotencyConflict = "settings_unit_conversion_policy_idempotency_conflict";
    public const string VersionConflict = "settings_unit_conversion_policy_version_conflict";
    public const string EffectiveFromPast = "settings_unit_conversion_policy_effective_from_past";
    public const string ProviderUnavailable = "settings_provider_unavailable";
    public const string AccessDenied = "settings_access_denied";
}

/// <summary>
/// A new unit conversion policy version of the server-resolved company. Every field is mandatory; nothing has a code default
/// (BS-9/BS-13/BS-16/BS-17: every value is a Settings value):
/// <list type="bullet">
/// <item><c>DensityGramsPerCubicCentimetre</c>: the steel density in g/cm³ as lossless positive decimal text (BS-9's example
/// "7.85"), at most <see cref="UnitConversionPolicyInputLimitsV1.DensityMaximumScale"/> decimals;</item>
/// <item><c>CoefficientDecimals</c> / <c>CoefficientRounding</c>: the decimals and rounding mode a coefficient (kg per piece,
/// kg per metre) is rounded with once where it is produced (BS-13, URN-D8; kept apart from the document total's mode);</item>
/// <item><c>DocumentTotalDecimals</c> / <c>DocumentTotalRounding</c>: the document kg total's decimals and rounding mode
/// (BS-13);</item>
/// <item><c>WholeUnitRounding</c>: the kg-to-whole-unit-count rounding mode (BS-13);</item>
/// <item><c>WeighbridgeTolerancePercent</c> (0…100, at most four decimals), <c>ToleranceBoundary</c> (BS-16) and
/// <c>ToleranceBase</c> (BS-17) of the weighbridge check (URN-D7: on the absolute deviation).</item>
/// </list>
/// The version starts on <c>EffectiveFrom</c> (omitted = today, Europe/Istanbul; earlier is refused). A version on the same
/// date as an existing one replaces it from that date. <c>ExpectedVersion</c> is the latest stored version ("0" when none).
/// </summary>
public sealed record RecordUnitConversionPolicyRequestV1(
    string? ExpectedVersion,
    string? DensityGramsPerCubicCentimetre,
    int? CoefficientDecimals,
    string? CoefficientRounding,
    int? DocumentTotalDecimals,
    string? DocumentTotalRounding,
    string? WholeUnitRounding,
    string? WeighbridgeTolerancePercent,
    string? ToleranceBoundary,
    string? ToleranceBase,
    string? Reason,
    DateOnly? EffectiveFrom = null);

public sealed record UnitConversionPolicyVersionViewV1(
    Guid ProfileId,
    string Version,
    string DensityGramsPerCubicCentimetre,
    int CoefficientDecimals,
    string CoefficientRounding,
    int DocumentTotalDecimals,
    string DocumentTotalRounding,
    string WholeUnitRounding,
    string WeighbridgeTolerancePercent,
    string ToleranceBoundary,
    string ToleranceBase,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    string State,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    string Reason);

/// <summary>
/// The company's unit conversion policy timeline. <c>Version</c> is the latest stored version ("0" when none) — the next
/// record's expected version. An empty list means the company has no policy yet: every consumer read is refused
/// (<see cref="SettingsPrerequisiteCodesV1.UnitConversionPolicyNotFound"/>) until an authorized person records one.
/// </summary>
public sealed record UnitConversionPolicyViewV1(
    Guid CompanyId,
    DateOnly Today,
    string Version,
    IReadOnlyList<UnitConversionPolicyVersionViewV1> Versions);

/// <summary>A command's terminal answer (stored for replay). <c>Code</c> is null for a success.</summary>
public sealed record UnitConversionPolicyCommandAnswerV1(int StatusCode, string? Code, string Title, string Detail);

public sealed record UnitConversionPolicyCommandResultV1(
    UnitConversionPolicyCommandAnswerV1 Answer,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>
/// The unit conversion policy administration (F-004). Every read and command is authorized first on
/// <see cref="SettingsUnitConversionPolicyPermissionManifestV1.Manage"/> company-wide in the server-resolved company and
/// re-measured before every save. <see cref="UnauthorizedAccessException"/> means 403. One person decides (SAT-D68
/// precedent): a mandatory reason, Idempotency-Key, expected version and one audit event per real change (none for a no-op).
/// </summary>
public interface ISettingsUnitConversionPolicyAdministrationV1
{
    Task<UnitConversionPolicyViewV1> GetAsync(CancellationToken cancellationToken = default);

    Task<UnitConversionPolicyCommandResultV1> RecordAsync(RecordUnitConversionPolicyRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

/// <summary>
/// The company's unit conversion policy in force on a date (the provider's answer). <c>Profile</c> names the exact stored
/// version a consumer computed with; <c>EffectiveThrough</c> is null for the open last version.
/// </summary>
public sealed record SettingsUnitConversionPolicyV1(
    Guid CompanyId,
    ProfileVersionV1 Profile,
    decimal DensityGramsPerCubicCentimetre,
    int CoefficientDecimals,
    MidpointRounding CoefficientRounding,
    int DocumentTotalDecimals,
    MidpointRounding DocumentTotalRounding,
    MidpointRounding WholeUnitRounding,
    decimal WeighbridgeTolerancePercent,
    SettingsWeighbridgeToleranceBoundaryV1 ToleranceBoundary,
    SettingsWeighbridgeToleranceBaseV1 ToleranceBase,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough);

/// <summary>
/// The read port a consumer (Product's unit conversion, stock/logistics weighbridge checks) asks for the server-resolved
/// company's unit conversion policy on a business date. Errors are <see cref="SettingsPrerequisiteException"/> codes, in this
/// order: cancelled → <see cref="OperationCanceledException"/>; no authenticated user or no access to the company →
/// <c>settings_access_denied</c>; an unreadable store → <c>settings_provider_unavailable</c>; no version in force on the date
/// (none recorded, or the date is before the first version's start) →
/// <see cref="SettingsPrerequisiteCodesV1.UnitConversionPolicyNotFound"/>; malformed stored data →
/// <c>settings_profile_invalid</c>. There is no fallback value: a consumer must refuse to convert rather than guess.
/// </summary>
public interface ISettingsUnitConversionPolicyProviderV1
{
    Task<SettingsUnitConversionPolicyV1> GetUnitConversionPolicyAsync(DateOnly effectiveOn,
        CancellationToken cancellationToken = default);
}
