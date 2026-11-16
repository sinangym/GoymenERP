namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// F-029 S1/S2 (URUN_F029 spec §5.4(4); URN-D2…D5 of <c>docs/decisions/SINAN_KARARLARI_2026-11-12_URUN.md</c>): the Settings
/// owner's name for maintaining the company's unit profiles (which unit a company may use, from when, at what quantity scale)
/// as a persistent, versioned Settings record. Registered, NOT granted: no role, seed or user receives it here (URN-D3: who
/// the "authorized manager" is stays a human grant gate). Company-wide only. Not Critical: URN-D3 asks for no second approver.
/// </summary>
public static class SettingsUnitProfilePermissionManifestV1
{
    /// <summary>Read the unit profile timeline, transfer the approved baseline once and record new versions.</summary>
    public const string Manage = "settings.unit-profiles.manage";
}

/// <summary>Technical bounds of the inputs (storage and transport shape, not policy).</summary>
public static class UnitProfileInputLimitsV1
{
    /// <summary>SAT-D68 precedent: the mandatory reason of every change.</summary>
    public const int ReasonMaximumLength = 4000;

    /// <summary>The largest quantity scale any unit may carry: the decimal type's own maximum scale (a technical guard).</summary>
    public const int MaximumScale = 28;

    /// <summary>
    /// Ö2 (Sinan, 2026-09-18, <c>docs/decisions/SINAN_KARARLARI_2026-09-18_O1_O8_VE_SON_ASAMA.md</c>): "kg ... en fazla 3
    /// ondalık". A kilogram profile may not carry more decimals than this; which scale a company uses within it is the
    /// profile's value.
    /// </summary>
    public const int KilogramMaximumScale = 3;

    /// <summary>The decision reference a baseline row carries (the catalog entry's source text).</summary>
    public const int DecisionReferenceMaximumLength = SalesPolicyInputLimitsV1.DecisionReferenceMaximumLength;
}

/// <summary>Published problem codes of the unit profile routes.</summary>
public static class UnitProfileProblemCodesV1
{
    public const string RequestInvalid = "settings_unit_profile_request_invalid";
    public const string CommandMetadataInvalid = "settings_unit_profile_command_metadata_invalid";
    public const string ReasonRequired = "settings_unit_profile_reason_required";
    public const string ReasonTooLong = "settings_unit_profile_reason_too_long";
    public const string IdempotencyConflict = "settings_unit_profile_idempotency_conflict";
    public const string VersionConflict = "settings_unit_profile_version_conflict";
    public const string EffectiveFromPast = "settings_unit_profile_effective_from_past";
    public const string BaselineRequired = "settings_unit_profile_baseline_required";
    public const string AlreadyTransferred = "settings_unit_profile_already_transferred";
    public const string UnitUnknown = "settings_unit_profile_unit_unknown";
    public const string ProviderUnavailable = "settings_provider_unavailable";
    public const string AccessDenied = "settings_access_denied";
}

/// <summary>
/// The one-time transfer of the server-resolved company's approved unit profiles (<c>ApprovedUnitProfileCatalog</c>) to the
/// Settings table, values unchanged (URN-D5). The reason is mandatory.
/// </summary>
public sealed record TransferUnitProfileBaselineRequestV1(string? Reason);

/// <summary>
/// A new unit profile version of the server-resolved company for one unit. Every field is mandatory:
/// <list type="bullet">
/// <item><c>UnitId</c>: a Settings unit identity (<see cref="ISettingsUnitIdentityCatalogV1"/>); its code, dimension and
/// kilogram flag are copied from the identity, never typed;</item>
/// <item><c>Scale</c>: the quantity decimals (0…<see cref="UnitProfileInputLimitsV1.MaximumScale"/>; a kilogram profile at
/// most <see cref="UnitProfileInputLimitsV1.KilogramMaximumScale"/>, Ö2);</item>
/// <item><c>IsActive</c>: whether the company may use the unit from the version's start.</item>
/// </list>
/// The version starts on <c>EffectiveFrom</c> (omitted = today, Europe/Istanbul; earlier is refused, URN-D4). A version on the
/// same date as an existing one replaces it from that date. <c>ExpectedVersion</c> is that unit's latest stored version ("0"
/// when the unit has none). Recording is refused until the company's baseline is transferred (URN-D5).
/// </summary>
public sealed record RecordUnitProfileRequestV1(
    string? ExpectedVersion,
    Guid? UnitId,
    int? Scale,
    bool? IsActive,
    string? Reason,
    DateOnly? EffectiveFrom = null);

/// <summary>
/// One unit profile version. Before the baseline transfer the rows are the approved catalog's entries
/// (<c>SourceKind</c> = <see cref="SalesPolicySourceKindsV1.CompiledBaselineV0"/>, no recorder); afterwards the stored rows.
/// <c>State</c> is one of <see cref="SalesPolicyVersionStatesV1"/>.
/// </summary>
public sealed record UnitProfileVersionViewV1(
    Guid ProfileId,
    string Version,
    int Scale,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    string State,
    string SourceKind,
    string? DecisionReference,
    string? RecordedBy,
    DateTimeOffset? RecordedAtUtc,
    string? Reason);

/// <summary>
/// One unit of the company's profiles. <c>Version</c> is the unit's latest stored version ("0" when none) — the next record's
/// expected version.
/// </summary>
public sealed record UnitProfileUnitViewV1(
    Guid UnitId,
    string UnitCode,
    string DimensionCode,
    bool IsKilogram,
    string Version,
    IReadOnlyList<UnitProfileVersionViewV1> Versions);

/// <summary>A unit identity a new profile may be recorded for (the identity catalog's list).</summary>
public sealed record UnitProfileUnitOptionV1(Guid UnitId, string UnitCode, string DimensionCode, bool IsKilogram, int MaximumScale);

/// <summary>
/// The company's unit profiles. <c>IsBaselineTransferred</c> false means the approved catalog still answers every consumer
/// read and no version can be recorded yet; true means the Settings table is the only source (URN-D5).
/// </summary>
public sealed record UnitProfileViewV1(
    Guid CompanyId,
    DateOnly Today,
    bool IsBaselineTransferred,
    IReadOnlyList<UnitProfileUnitViewV1> Units,
    IReadOnlyList<UnitProfileUnitOptionV1> UnitOptions);

/// <summary>A command's terminal answer (stored for replay). <c>Code</c> is null for a success.</summary>
public sealed record UnitProfileCommandAnswerV1(int StatusCode, string? Code, string Title, string Detail);

public sealed record UnitProfileCommandResultV1(UnitProfileCommandAnswerV1 Answer, bool IsReplay, string OriginalCorrelationId);

/// <summary>
/// The unit profile administration (F-029 S1/S2). Every read and command is authorized first on
/// <see cref="SettingsUnitProfilePermissionManifestV1.Manage"/> company-wide in the server-resolved company and re-measured
/// before every save. <see cref="UnauthorizedAccessException"/> means 403. One person decides (URN-D3): a mandatory reason,
/// Idempotency-Key, expected version and one audit event per real change (none for a no-op).
/// </summary>
public interface ISettingsUnitProfileAdministrationV1
{
    Task<UnitProfileViewV1> GetAsync(CancellationToken cancellationToken = default);

    Task<UnitProfileCommandResultV1> TransferBaselineAsync(TransferUnitProfileBaselineRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<UnitProfileCommandResultV1> RecordAsync(RecordUnitProfileRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default);
}
