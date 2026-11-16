namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// URT-D89 (<c>docs/decisions/SINAN_KARARLARI_2026-11-15_DALGA2_EK12.md</c>; URT-D87 the unit, URT-D91/URT-D92 the buffer's
/// starting value and its single per-work-center source): the Settings owner's name for maintaining a work center's input
/// capacity (kg/hour) and its optional due-date safety buffer as a persistent, versioned Settings record. The decided persona
/// is the production manager ("Üretim Müdürü"); that is a persona note only. Registered, NOT granted: no role, seed or user
/// receives it here (a human grant gate). Company-wide only (measured in the server-resolved company the person works in).
/// Not Critical: no approved source adds grant-time dual control.
/// </summary>
public static class SettingsWorkCenterCapacityPermissionManifestV1
{
    /// <summary>Read every work center capacity timeline of the company and record new versions.</summary>
    public const string Manage = "settings.work-center-capacities.manage";
}

/// <summary>The published names of the capacity value (URT-D87: input kg per hour, divided into the recipe input kg).</summary>
public static class WorkCenterCapacityKeysV1
{
    /// <summary>The capacity's parameter name, as the Manufacturing due-date suggestion names it.</summary>
    public const string InputCapacityKgPerHour = "inputCapacityKgPerHour";

    /// <summary>The capacity's unit: kilograms of recipe input per hour.</summary>
    public const string InputCapacityUnit = "kg/h";
}

/// <summary>
/// The closed list of safety buffer units (URT-D89: "saat/gün"). The value is stored with the unit it was entered in; how a
/// "day" is counted (working days of the work center calendar or calendar days) is the consuming due-date calculation's
/// rule, not converted here.
/// </summary>
public static class WorkCenterSafetyBufferUnitsV1
{
    public const string Hours = "Hours";
    public const string Days = "Days";

    public static IReadOnlyList<string> All { get; } = [Hours, Days];
}

/// <summary>Technical bounds of the inputs (storage and transport shape, not policy).</summary>
public static class WorkCenterCapacityInputLimitsV1
{
    /// <summary>SAT-D68 precedent: the mandatory reason of every change.</summary>
    public const int ReasonMaximumLength = 4000;

    /// <summary>The capacity column holds at most this many integer digits (decimal(18,6)).</summary>
    public const int CapacityMaximumIntegerDigits = 12;

    /// <summary>The capacity column keeps at most this many decimals (decimal(18,6)).</summary>
    public const int CapacityMaximumScale = 6;

    /// <summary>The safety buffer column holds at most this many integer digits (decimal(9,3)).</summary>
    public const int SafetyBufferMaximumIntegerDigits = 6;

    /// <summary>The safety buffer column keeps at most this many decimals (decimal(9,3)).</summary>
    public const int SafetyBufferMaximumScale = 3;
}

/// <summary>Published problem codes of the work center capacity routes.</summary>
public static class WorkCenterCapacityProblemCodesV1
{
    public const string RequestInvalid = "settings_work_center_capacity_request_invalid";
    public const string CommandMetadataInvalid = "settings_work_center_capacity_command_metadata_invalid";
    public const string ReasonRequired = "settings_work_center_capacity_reason_required";
    public const string ReasonTooLong = "settings_work_center_capacity_reason_too_long";
    public const string WorkCenterNotFound = "settings_work_center_capacity_work_center_not_found";
    public const string IdempotencyConflict = "settings_work_center_capacity_idempotency_conflict";
    public const string VersionConflict = "settings_work_center_capacity_version_conflict";
    public const string ProviderUnavailable = "settings_provider_unavailable";
    public const string ProfileInvalid = "settings_profile_invalid";
    public const string AccessDenied = "settings_access_denied";
}

/// <summary>
/// A new capacity version of one work center (the work center is the route's id). Nothing has a code default:
/// <list type="bullet">
/// <item><c>InputCapacityKgPerHour</c>: mandatory, lossless decimal text greater than zero (at most
/// <see cref="WorkCenterCapacityInputLimitsV1.CapacityMaximumIntegerDigits"/> integer digits and
/// <see cref="WorkCenterCapacityInputLimitsV1.CapacityMaximumScale"/> decimals);</item>
/// <item><c>SafetyBufferValue</c> + <c>SafetyBufferUnit</c>: optional together — both omitted means no buffer (URT-D89: counted
/// as zero); when given, the value is lossless decimal text greater than zero (at most
/// <see cref="WorkCenterCapacityInputLimitsV1.SafetyBufferMaximumIntegerDigits"/> integer digits and
/// <see cref="WorkCenterCapacityInputLimitsV1.SafetyBufferMaximumScale"/> decimals) and the unit one of
/// <see cref="WorkCenterSafetyBufferUnitsV1.All"/>;</item>
/// <item><c>Reason</c>: the mandatory reason (≤ <see cref="WorkCenterCapacityInputLimitsV1.ReasonMaximumLength"/>).</item>
/// </list>
/// A version takes effect when it is saved. <c>ExpectedVersion</c> is the work center's latest stored version ("0" when none).
/// </summary>
public sealed record RecordWorkCenterCapacityRequestV1(
    string? ExpectedVersion,
    string? InputCapacityKgPerHour,
    string? SafetyBufferValue,
    string? SafetyBufferUnit,
    string? Reason);

/// <summary>
/// One stored version of a work center; <c>IsCurrent</c> marks the latest one, which the due-date suggestion uses. Decimals
/// travel as canonical invariant text (no exponent, no trailing zeros); the buffer fields are null when no buffer is recorded.
/// </summary>
public sealed record WorkCenterCapacityVersionViewV1(
    Guid ProfileId,
    string Version,
    string InputCapacityKgPerHour,
    string? SafetyBufferValue,
    string? SafetyBufferUnit,
    bool IsCurrent,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    string Reason);

/// <summary>
/// The capacity timeline of one work center (newest first). <c>Version</c> is the latest stored version — the next record's
/// expected version.
/// </summary>
public sealed record WorkCenterCapacityTimelineViewV1(
    Guid WorkCenterId,
    string Version,
    IReadOnlyList<WorkCenterCapacityVersionViewV1> Versions);

/// <summary>
/// Every work center of the company that has a recorded capacity, ordered by work center id. A work center that is absent
/// has no capacity: its due-date suggestion is "hesaplanamadı" (URT-D89, fail-closed) and its next record's expected version
/// is "0". Names and the list of work centers come from the Organization owner, not from here.
/// </summary>
public sealed record WorkCenterCapacityViewV1(
    string CapacityUnit,
    IReadOnlyList<WorkCenterCapacityTimelineViewV1> WorkCenters);

/// <summary>A command's terminal answer (stored for replay). <c>Code</c> is null for a success.</summary>
public sealed record WorkCenterCapacityCommandAnswerV1(int StatusCode, string? Code, string Title, string Detail);

public sealed record WorkCenterCapacityCommandResultV1(
    WorkCenterCapacityCommandAnswerV1 Answer,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>
/// The work center capacity administration (URT-D89). Every read and command is authorized first on
/// <see cref="SettingsWorkCenterCapacityPermissionManifestV1.Manage"/> company-wide in the server-resolved company and
/// re-measured before every save. <see cref="UnauthorizedAccessException"/> means 403. One person decides (SAT-D68
/// precedent): a mandatory reason, Idempotency-Key, expected version and one audit event per real change (none for a no-op).
/// The work center must exist in the company (asked of the Organization owner through
/// <see cref="ISettingsWorkCenterReferenceV1"/>); an unknown or foreign one is
/// <see cref="WorkCenterCapacityProblemCodesV1.WorkCenterNotFound"/>.
/// </summary>
public interface ISettingsWorkCenterCapacityAdministrationV1
{
    Task<WorkCenterCapacityViewV1> GetAsync(CancellationToken cancellationToken = default);

    Task<WorkCenterCapacityCommandResultV1> RecordAsync(Guid workCenterId, RecordWorkCenterCapacityRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

/// <summary>
/// The capacity of one work center in force (the provider's answer): the latest stored version.
/// <c>InputCapacityKgPerHour</c> is always greater than zero. With no recorded buffer <c>SafetyBufferValue</c> is 0 and
/// <c>SafetyBufferUnit</c> is null (URT-D89/URT-D91: no buffer counts as zero); otherwise the value is greater than zero and
/// the unit one of <see cref="WorkCenterSafetyBufferUnitsV1.All"/>.
/// </summary>
public sealed record SettingsWorkCenterCapacityV1(
    Guid CompanyId,
    Guid WorkCenterId,
    ProfileVersionV1 Profile,
    decimal InputCapacityKgPerHour,
    decimal SafetyBufferValue,
    string? SafetyBufferUnit);

/// <summary>
/// The read port the Manufacturing due-date suggestion asks for a work center's capacity (URT-D87/URT-D89). The caller passes
/// the company of the plan; it must equal the server-resolved company, otherwise the answer is <c>settings_access_denied</c>.
/// It applies no Settings permission: the calling module authorizes its own read. Answers: the latest stored version, or
/// <c>null</c> when no capacity is recorded for the work center — the consumer then answers "hesaplanamadı" (fail-closed),
/// never a guessed capacity. Errors are <see cref="SettingsPrerequisiteException"/> codes, in this order: cancelled →
/// <see cref="OperationCanceledException"/>; no authenticated user, no access to the company or another company →
/// <c>settings_access_denied</c>; an unreadable store → <c>settings_provider_unavailable</c>; malformed stored data →
/// <c>settings_profile_invalid</c>.
/// </summary>
public interface ISettingsWorkCenterCapacityProviderV1
{
    Task<SettingsWorkCenterCapacityV1?> GetWorkCenterCapacityAsync(Guid companyId, Guid workCenterId,
        CancellationToken cancellationToken = default);
}

/// <summary>The Organization owner's answer about a work center id (through the host adapter).</summary>
public enum SettingsWorkCenterReferenceOutcomeV1
{
    /// <summary>The work center exists in the company.</summary>
    Found = 1,

    /// <summary>No such work center in the company (unknown or of another company).</summary>
    NotFound = 2,

    /// <summary>The owner could not answer; the command fails closed (<c>settings_provider_unavailable</c>).</summary>
    Unavailable = 3,
}

/// <summary>
/// The Settings-owned port through which the capacity administration asks whether a work center exists in the
/// server-resolved company. Settings reads no Organization table: the API host composes an adapter over the Organization
/// owner's planning reference read. Until the host composes one, every record fails closed
/// (<c>settings_provider_unavailable</c>).
/// </summary>
public interface ISettingsWorkCenterReferenceV1
{
    Task<SettingsWorkCenterReferenceOutcomeV1> FindAsync(Guid companyId, Guid workCenterId,
        CancellationToken cancellationToken = default);
}
