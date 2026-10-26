namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// Stable action identities for the Settings-owned Manufacturing tolerance profile lifecycle.
/// They are declarations only: this contract registers no permission and grants no actor access.
/// </summary>
public static class ManufacturingToleranceProfileActionsV1
{
    public const string Prepare = "settings.manufacturing-tolerance-profiles.prepare";
    public const string Approve = "settings.manufacturing-tolerance-profiles.approve";
    public const string Publish = "settings.manufacturing-tolerance-profiles.publish";
}

public enum ManufacturingToleranceProfileStatusV1
{
    Draft = 1,
    Approved = 2,
    Published = 3,
}

/// <summary>
/// Server-derived command metadata. Company and actor never come from a profile payload.
/// Idempotency persistence and authorization belong to the later command adapter.
/// </summary>
public sealed record ManufacturingToleranceProfileCommandContextV1(
    Guid CompanyId,
    string ActorId,
    long ExpectedStateVersion,
    string IdempotencyKey,
    string Reason,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Opens the first draft of an explicitly identified, explicitly numbered profile version.
/// The numeric tolerance and effective window remain outside this contract-only slice.
/// </summary>
public sealed record PrepareManufacturingToleranceProfileV1(
    Guid ProfileId,
    string Process,
    long ProfileVersion,
    ManufacturingToleranceProfileCommandContextV1 Context);

/// <summary>
/// Opens a new draft version after an immutable publication. Process and the next profile version
/// are derived from that publication; callers supply only the new identity and command metadata.
/// </summary>
public sealed record PrepareNextManufacturingToleranceProfileVersionV1(
    Guid ProfileId,
    ManufacturingToleranceProfileCommandContextV1 Context);

/// <summary>
/// Immutable lifecycle metadata for one profile version. <see cref="StateVersion"/> is the
/// optimistic command version and is separate from the business <see cref="ProfileVersion"/>.
/// </summary>
public sealed record ManufacturingToleranceProfileLifecycleV1(
    Guid CompanyId,
    Guid ProfileId,
    string Process,
    long ProfileVersion,
    long StateVersion,
    ManufacturingToleranceProfileStatusV1 Status,
    string PreparedBy,
    DateTimeOffset PreparedAtUtc,
    string PreparationReason,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAtUtc,
    string? ApprovalReason,
    string? PublishedBy,
    DateTimeOffset? PublishedAtUtc,
    string? PublicationReason);

public static class ManufacturingToleranceProfileLifecycleCodesV1
{
    public const string Invalid = "settings_manufacturing_tolerance_lifecycle_invalid";
    public const string VersionConflict = "settings_manufacturing_tolerance_lifecycle_version_conflict";
    public const string InvalidTransition = "settings_manufacturing_tolerance_lifecycle_transition_invalid";
    public const string MakerSeparationRequired =
        "settings_manufacturing_tolerance_lifecycle_maker_separation_required";
}

public sealed class ManufacturingToleranceProfileLifecycleException(
    string code,
    long? expectedStateVersion = null,
    long? currentStateVersion = null) : InvalidOperationException(code)
{
    public string Code { get; } = code;
    public long? ExpectedStateVersion { get; } = expectedStateVersion;
    public long? CurrentStateVersion { get; } = currentStateVersion;
}
