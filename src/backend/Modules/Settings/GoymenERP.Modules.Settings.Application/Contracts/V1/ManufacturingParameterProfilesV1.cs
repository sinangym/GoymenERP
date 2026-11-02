namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// API-01 / MOD-01 (Üretim hazırlık planı v1; URT-D9, URT-D10, <c>docs/decisions/SINAN_KARARLARI_2026-11-01_URETIM.md</c>):
/// the Settings owner's permission names for the versioned Manufacturing parameter profiles. Registered centrally, granted to no
/// role (a human grant gate). Company-wide only, not Critical: maker ≠ approver is enforced by the lifecycle at command time.
/// </summary>
public static class ManufacturingParameterProfileActionsV1
{
    public const string Read = "settings.manufacturing-parameter-profiles.read";
    public const string Prepare = "settings.manufacturing-parameter-profiles.prepare";
    public const string Approve = "settings.manufacturing-parameter-profiles.approve";
    public const string Publish = "settings.manufacturing-parameter-profiles.publish";
}

/// <summary>How a parameter value is validated (the shape of the engine input it feeds; never a business threshold).</summary>
public enum ManufacturingParameterValueKindV1
{
    /// <summary>Above zero (a density, a speed, a film thickness, a factor).</summary>
    Positive = 1,

    /// <summary>Zero or above: zero is a stated "none" (a kerf, a trim, a tolerance), never "unknown".</summary>
    NonNegative = 2,

    /// <summary>Above zero and at most one (a solids ratio).</summary>
    RatioUpToOne = 3,

    /// <summary>A whole number above zero (a count of simultaneous profiles).</summary>
    WholeCount = 4,

    /// <summary>One of the declared choices (a kerf mode).</summary>
    Choice = 5,
}

/// <summary>One parameter key: its unit, validation kind, maximum decimal places and the processes it may be set for.</summary>
public sealed record ManufacturingParameterDefinitionV1(
    string Key,
    string Unit,
    ManufacturingParameterValueKindV1 Kind,
    int MaximumScale,
    IReadOnlyList<string> Processes,
    IReadOnlyList<string> Choices);

/// <summary>
/// The declared parameter keys. The keys are the Manufacturing engines' own input names (for example
/// <c>densityKgPerCubicMetre</c> in kg/m³ — the engines and their golden tests use 7850 kg/m³ for 7,85 kg/dm³), so a
/// consumer maps nothing. A new key is a new entry here (the storage is one typed row per key, no migration). Values are
/// data in Settings profiles, never constants in code; a key that no profile carries stays unknown for the consumer.
/// </summary>
public static class ManufacturingParameterKeysV1
{
    public const string DensityKgPerCubicMetre = "densityKgPerCubicMetre";
    public const string LineSpeedMetresPerMinute = "lineSpeedMetresPerMinute";
    public const string FilmMicrons = "filmMicrons";
    public const string SolidsRatio = "solidsRatio";

    /// <summary>The paint loss factor (PNT-006 <c>scrapFactor</c>, StokERP "fire katsayısı").</summary>
    public const string ScrapFactor = "scrapFactor";

    public const string SimultaneousProfiles = "simultaneousProfiles";
    public const string KerfMm = "kerfMm";

    /// <summary>Delegation No 86 (<c>D-M025-KERF=A</c>): <c>PerPiece</c> or <c>BetweenPieces</c>.</summary>
    public const string KerfMode = "kerfMode";

    public const string HeadTrimMm = "headTrimMm";
    public const string TailTrimMm = "tailTrimMm";

    /// <summary>URT-D10 "kenar kaybı": the edge trim of a slitting line.</summary>
    public const string EdgeTrimMm = "edgeTrimMm";

    public const string InnerDiameterMm = "innerDiameterMm";
    public const string MaximumOuterDiameterMm = "maximumOuterDiameterMm";
    public const string ToleranceMetres = "toleranceMetres";

    public const string KerfModePerPiece = "PerPiece";
    public const string KerfModeBetweenPieces = "BetweenPieces";

    /// <summary>Millimetres and kilograms follow Ö2: at most three decimals.</summary>
    private const int MeasureScale = 3;

    /// <summary>The storage scale of every other numeric value (decimal(18,6)).</summary>
    private const int ValueScale = 6;

    public static IReadOnlyList<ManufacturingParameterDefinitionV1> All { get; } =
    [
        Numeric(DensityKgPerCubicMetre, ManufacturingParameterUnitsV1.KilogramsPerCubicMetre,
            ManufacturingParameterValueKindV1.Positive, ValueScale,
            SettingsManufacturingProcessesV1.Slitting, SettingsManufacturingProcessesV1.ColdRolling,
            SettingsManufacturingProcessesV1.CutToLength),
        Numeric(LineSpeedMetresPerMinute, ManufacturingParameterUnitsV1.MetresPerMinute,
            ManufacturingParameterValueKindV1.Positive, ValueScale,
            SettingsManufacturingProcessesV1.Slitting, SettingsManufacturingProcessesV1.TubeProfile,
            SettingsManufacturingProcessesV1.Paint),
        Numeric(FilmMicrons, ManufacturingParameterUnitsV1.Micrometres, ManufacturingParameterValueKindV1.Positive, ValueScale,
            SettingsManufacturingProcessesV1.Paint),
        Numeric(SolidsRatio, ManufacturingParameterUnitsV1.Ratio, ManufacturingParameterValueKindV1.RatioUpToOne, ValueScale,
            SettingsManufacturingProcessesV1.Paint),
        Numeric(ScrapFactor, ManufacturingParameterUnitsV1.Factor, ManufacturingParameterValueKindV1.Positive, ValueScale,
            SettingsManufacturingProcessesV1.Paint),
        Numeric(SimultaneousProfiles, ManufacturingParameterUnitsV1.Count, ManufacturingParameterValueKindV1.WholeCount, 0,
            SettingsManufacturingProcessesV1.Paint),
        Numeric(KerfMm, ManufacturingParameterUnitsV1.Millimetres, ManufacturingParameterValueKindV1.NonNegative, MeasureScale,
            SettingsManufacturingProcessesV1.CutToLength),
        new(KerfMode, ManufacturingParameterUnitsV1.Choice, ManufacturingParameterValueKindV1.Choice, 0,
            [SettingsManufacturingProcessesV1.CutToLength], [KerfModePerPiece, KerfModeBetweenPieces]),
        Numeric(HeadTrimMm, ManufacturingParameterUnitsV1.Millimetres, ManufacturingParameterValueKindV1.NonNegative,
            MeasureScale, SettingsManufacturingProcessesV1.CutToLength),
        Numeric(TailTrimMm, ManufacturingParameterUnitsV1.Millimetres, ManufacturingParameterValueKindV1.NonNegative,
            MeasureScale, SettingsManufacturingProcessesV1.CutToLength),
        Numeric(EdgeTrimMm, ManufacturingParameterUnitsV1.Millimetres, ManufacturingParameterValueKindV1.NonNegative,
            MeasureScale, SettingsManufacturingProcessesV1.Slitting),
        Numeric(InnerDiameterMm, ManufacturingParameterUnitsV1.Millimetres, ManufacturingParameterValueKindV1.Positive,
            MeasureScale, SettingsManufacturingProcessesV1.Slitting),
        Numeric(MaximumOuterDiameterMm, ManufacturingParameterUnitsV1.Millimetres, ManufacturingParameterValueKindV1.Positive,
            MeasureScale, SettingsManufacturingProcessesV1.Slitting),
        Numeric(ToleranceMetres, ManufacturingParameterUnitsV1.Metres, ManufacturingParameterValueKindV1.NonNegative,
            ValueScale, SettingsManufacturingProcessesV1.Slitting),
    ];

    public static ManufacturingParameterDefinitionV1? Definition(string? key) =>
        All.SingleOrDefault(definition => string.Equals(definition.Key, key, StringComparison.Ordinal));

    private static ManufacturingParameterDefinitionV1 Numeric(string key, string unit, ManufacturingParameterValueKindV1 kind,
        int scale, params string[] processes) => new(key, unit, kind, scale, processes, []);
}

/// <summary>The declared units (wire values).</summary>
public static class ManufacturingParameterUnitsV1
{
    public const string KilogramsPerCubicMetre = "kg/m3";
    public const string MetresPerMinute = "m/min";
    public const string Micrometres = "um";
    public const string Ratio = "ratio";
    public const string Factor = "factor";
    public const string Count = "count";
    public const string Millimetres = "mm";
    public const string Metres = "m";
    public const string Choice = "choice";
}

/// <summary>Where a profile comes from.</summary>
public static class ManufacturingProfileSourcesV1
{
    /// <summary>The reviewed version-0 catalog shipped as source (URT-D6/D7/D9/D10: Sinan's explicit starting values).</summary>
    public const string Catalog = "Catalog";

    /// <summary>A profile version prepared, approved and published in Settings.</summary>
    public const string Settings = "Settings";
}

/// <summary>Lifecycle states on the wire (<see cref="ManufacturingToleranceProfileStatusV1"/> names).</summary>
public static class ManufacturingProfileStatusesV1
{
    public const string Draft = nameof(ManufacturingToleranceProfileStatusV1.Draft);
    public const string Approved = nameof(ManufacturingToleranceProfileStatusV1.Approved);
    public const string Published = nameof(ManufacturingToleranceProfileStatusV1.Published);

    /// <summary>
    /// A reviewed tolerance starting value (URT-D6/D7), version 0. Since URT-D23 the scoped tolerance read serves it to the
    /// order close when no published version of the scope covers the date.
    /// </summary>
    public const string StartingValue = "StartingValue";
}

/// <summary>
/// The parameter profile in force for one company × process (× optional work center) on one date. The company always comes
/// from the server context. A consumer persists the exact identity, version text, window and values it used.
/// </summary>
public sealed record ManufacturingParameterProfileSnapshotV1(
    Guid CompanyId,
    string Process,
    Guid? WorkCenterId,
    Guid ProfileId,
    string ProfileVersion,
    string Source,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    IReadOnlyList<ManufacturingParameterValueV1> Values);

/// <summary>One value: a number (lossless decimal) or, for a <see cref="ManufacturingParameterValueKindV1.Choice"/> key, a choice.</summary>
public sealed record ManufacturingParameterValueV1(string Key, string Unit, decimal? Number, string? Choice);

/// <summary>
/// Read port for Manufacturing parameter profiles. Outcomes, in order: cancellation; no actor / company or not a member →
/// <c>settings_access_denied</c>; a non-canonical process or an empty work center id →
/// <see cref="ManufacturingParameterProfileCodesV1.RequestInvalid"/>; an unreadable store, or a company with neither a
/// reviewed catalog nor a published profile → <c>settings_provider_unavailable</c>; nothing published covers the date →
/// <see cref="ManufacturingParameterProfileCodesV1.ProfileNotFound"/>; stored data that breaks the key registry →
/// <c>settings_profile_invalid</c>. Selection: a published profile of the given work center whose window covers the date wins;
/// otherwise the process profile; within one scope the latest <c>EffectiveFrom</c>, then the highest version (the catalog is
/// version 0, so a same-day Settings version wins). Never a fallback value.
/// </summary>
public interface ISettingsManufacturingParameterProviderV1
{
    Task<ManufacturingParameterProfileSnapshotV1> GetManufacturingParametersAsync(
        string process, Guid? workCenterId, DateOnly effectiveOn, CancellationToken cancellationToken = default);
}

public static class ManufacturingParameterProfileCodesV1
{
    public const string ProfileNotFound = "settings_manufacturing_parameter_profile_not_found";
    public const string RequestInvalid = "settings_manufacturing_parameter_request_invalid";
}

/// <summary>Published problem codes of the Manufacturing parameter and tolerance profile routes.</summary>
public static class ManufacturingProfileProblemCodesV1
{
    public const string RequestInvalid = "settings_manufacturing_profile_request_invalid";
    public const string CommandMetadataInvalid = "settings_manufacturing_profile_command_metadata_invalid";
    public const string ReasonRequired = "settings_manufacturing_profile_reason_required";
    public const string ReasonTooLong = "settings_manufacturing_profile_reason_too_long";
    public const string IdempotencyConflict = "settings_manufacturing_profile_idempotency_conflict";
    public const string VersionConflict = "settings_manufacturing_profile_version_conflict";
    public const string TransitionInvalid = "settings_manufacturing_profile_transition_invalid";
    public const string MakerSeparationRequired = "settings_manufacturing_profile_maker_separation_required";
    public const string EffectiveFromPast = "settings_manufacturing_profile_effective_from_past";
    public const string WindowOverlap = "settings_manufacturing_profile_window_overlap";
    public const string ProviderUnavailable = "settings_provider_unavailable";
    public const string AccessDenied = "settings_access_denied";
}

/// <summary>Technical bounds (storage and transport shape, not policy).</summary>
public static class ManufacturingProfileInputLimitsV1
{
    /// <summary>The lifecycle's reason bound (<c>ManufacturingToleranceProfileLifecyclePolicyV1</c>).</summary>
    public const int ReasonMaximumLength = 500;

    /// <summary>Integer digits of a stored value (decimal(18,6)).</summary>
    public const int ValueMaximumIntegerDigits = 12;

    /// <summary>Decimal places of a tolerance percent (decimal(9,6)).</summary>
    public const int PercentMaximumScale = 6;
}

/// <summary>One value of a prepare request: the key and its value as invariant decimal text (or the choice).</summary>
public sealed record ManufacturingParameterValueInputV1(string? Key, string? Value);

/// <summary>
/// Prepare a new draft parameter profile version for one process (and optionally one work center: a line's own profile,
/// URT-D9). <c>ExpectedVersion</c> is the highest profile version of that scope as decimal text ("0" while it has none);
/// the draft becomes that + 1. <c>EffectiveFrom</c> (omitted = today, Europe/Istanbul) may not be before today;
/// <c>EffectiveThrough</c> is optional (open-ended) and not before <c>EffectiveFrom</c>. Each value's key must be declared
/// for the process (<see cref="ManufacturingParameterKeysV1"/>) and pass its kind and scale; a key appears at most once.
/// </summary>
public sealed record PrepareManufacturingParameterProfileRequestV1(
    string? ExpectedVersion,
    string? Process,
    Guid? WorkCenterId,
    IReadOnlyList<ManufacturingParameterValueInputV1>? Values,
    string? Reason,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveThrough = null);

/// <summary>Approve or publish one profile version. <c>ExpectedVersion</c> is its state version as decimal text.</summary>
public sealed record ManufacturingProfileLifecycleRequestV1(string? ExpectedVersion, string? Reason);

/// <summary>A command's success body: the profile, its business version, its state version (the next fence) and status.</summary>
public sealed record ManufacturingProfileCommandReceiptV1(
    Guid ProfileId,
    string ProfileVersion,
    string StateVersion,
    string Status);

/// <summary>A command's terminal answer (stored for replay). <c>Code</c> is null for a success and the code-less 404.</summary>
public sealed record ManufacturingProfileCommandAnswerV1(
    int StatusCode,
    string? Code,
    string Title,
    string Detail,
    ManufacturingProfileCommandReceiptV1? Receipt);

public sealed record ManufacturingProfileCommandResultV1(
    ManufacturingProfileCommandAnswerV1 Answer,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>A declared key as the screen lists it.</summary>
public sealed record ManufacturingParameterDefinitionViewV1(
    string Key,
    string Unit,
    string Kind,
    int MaximumScale,
    IReadOnlyList<string> Processes,
    IReadOnlyList<string> Choices);

public sealed record ManufacturingParameterValueViewV1(string Key, string Unit, string Value);

/// <summary>
/// One parameter profile version. <c>Source</c> <c>Catalog</c>: the reviewed version 0 (published, no actor, never changes);
/// <c>Settings</c>: a lifecycle version (Draft → Approved → Published; preparer ≠ approver, preparer ≠ publisher).
/// </summary>
public sealed record ManufacturingParameterProfileViewV1(
    Guid ProfileId,
    string Source,
    string Process,
    Guid? WorkCenterId,
    string ProfileVersion,
    string StateVersion,
    string Status,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    IReadOnlyList<ManufacturingParameterValueViewV1> Values,
    string? PreparedBy,
    DateTimeOffset? PreparedAtUtc,
    string PreparationReason,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAtUtc,
    string? ApprovalReason,
    string? PublishedBy,
    DateTimeOffset? PublishedAtUtc,
    string? PublicationReason);

/// <summary>The company's parameter profiles (catalog and Settings versions, every status) and the declared keys.</summary>
public sealed record ManufacturingParameterProfileListViewV1(
    Guid CompanyId,
    DateOnly BusinessDate,
    IReadOnlyList<ManufacturingParameterDefinitionViewV1> Keys,
    IReadOnlyList<ManufacturingParameterProfileViewV1> Profiles);

/// <summary>
/// The Manufacturing parameter profile administration. Every read and command is authorized on its own name
/// (<see cref="ManufacturingParameterProfileActionsV1"/>) company-wide in the server-resolved company and re-measured before
/// every save; <see cref="UnauthorizedAccessException"/> means 403. Commands: Idempotency-Key, expected version, a mandatory
/// reason, correlation/causation and one audit event per change in the same transaction.
/// </summary>
public interface ISettingsManufacturingParameterProfileAdministrationV1
{
    Task<ManufacturingParameterProfileListViewV1> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One profile; null when this company has no such profile (another company's id included).</summary>
    Task<ManufacturingParameterProfileViewV1?> GetAsync(Guid profileId, CancellationToken cancellationToken = default);

    Task<ManufacturingProfileCommandResultV1> PrepareAsync(PrepareManufacturingParameterProfileRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<ManufacturingProfileCommandResultV1> ApproveAsync(Guid profileId, ManufacturingProfileLifecycleRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<ManufacturingProfileCommandResultV1> PublishAsync(Guid profileId, ManufacturingProfileLifecycleRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}
