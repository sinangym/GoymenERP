namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// Server-derived evidence for one Settings tolerance-profile command. None of these values is
/// accepted from a profile value payload by the later HTTP adapter.
/// </summary>
public sealed record ManufacturingToleranceProfilePersistenceContextV1(
    Guid CompanyId,
    string ActorId,
    long ExpectedStateVersion,
    string IdempotencyKey,
    string Reason,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string? CausationId,
    string SourceChannel,
    string? ClientIpAddress,
    string? UserAgent);

public sealed record PrepareManufacturingToleranceProfileCommandV1(
    Guid ProfileId,
    string Process,
    long ProfileVersion,
    decimal MaximumFirePercent,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    ManufacturingToleranceProfilePersistenceContextV1 Context);

public sealed record PrepareNextManufacturingToleranceProfileVersionCommandV1(
    Guid PublishedProfileId,
    Guid ProfileId,
    decimal MaximumFirePercent,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    ManufacturingToleranceProfilePersistenceContextV1 Context);

public sealed record TransitionManufacturingToleranceProfileCommandV1(
    Guid ProfileId,
    ManufacturingToleranceProfilePersistenceContextV1 Context);

/// <summary>
/// Persistence result for one candidate profile version. This is an application contract, not an
/// HTTP request/response contract and it does not activate the runtime tolerance provider.
/// </summary>
public sealed record ManufacturingToleranceProfileCandidateV1(
    ManufacturingToleranceProfileLifecycleV1 Lifecycle,
    decimal MaximumFirePercent,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough);

public sealed record ManufacturingToleranceProfileCommandExecutionV1(
    ManufacturingToleranceProfileCandidateV1 Profile,
    bool IsReplay,
    string OriginalCorrelationId);

public interface ISettingsManufacturingToleranceProfileCommandServiceV1
{
    Task<ManufacturingToleranceProfileCommandExecutionV1> PrepareAsync(
        PrepareManufacturingToleranceProfileCommandV1 command,
        CancellationToken cancellationToken = default);

    Task<ManufacturingToleranceProfileCommandExecutionV1> PrepareNextVersionAsync(
        PrepareNextManufacturingToleranceProfileVersionCommandV1 command,
        CancellationToken cancellationToken = default);

    Task<ManufacturingToleranceProfileCommandExecutionV1> ApproveAsync(
        TransitionManufacturingToleranceProfileCommandV1 command,
        CancellationToken cancellationToken = default);

    Task<ManufacturingToleranceProfileCommandExecutionV1> PublishAsync(
        TransitionManufacturingToleranceProfileCommandV1 command,
        CancellationToken cancellationToken = default);
}

public static class ManufacturingToleranceProfilePersistenceCodesV1
{
    public const string Invalid = "settings_manufacturing_tolerance_profile_command_invalid";
    public const string Unauthenticated = "settings_manufacturing_tolerance_profile_unauthenticated";
    public const string Forbidden = "settings_manufacturing_tolerance_profile_forbidden";
    public const string NotFound = "settings_manufacturing_tolerance_profile_not_found";
    public const string VersionConflict = "settings_manufacturing_tolerance_profile_version_conflict";
    public const string IdempotencyConflict = "settings_manufacturing_tolerance_profile_idempotency_conflict";
    public const string PublicationOverlap = "settings_manufacturing_tolerance_profile_publication_overlap";
    public const string StoredResponseInvalid = "settings_manufacturing_tolerance_profile_stored_response_invalid";
}

public sealed class ManufacturingToleranceProfilePersistenceException(
    string code,
    long? expectedStateVersion = null,
    long? currentStateVersion = null,
    Exception? innerException = null) : InvalidOperationException(code, innerException)
{
    public string Code { get; } = code;
    public long? ExpectedStateVersion { get; } = expectedStateVersion;
    public long? CurrentStateVersion { get; } = currentStateVersion;
}
