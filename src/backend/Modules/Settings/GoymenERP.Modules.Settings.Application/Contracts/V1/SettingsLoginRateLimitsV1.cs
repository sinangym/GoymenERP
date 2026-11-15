namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// KIM-D34 (<c>docs/decisions/SINAN_KARARLARI_2026-11-15_DALGA2_EK9.md</c>): the Settings owner's name for maintaining the
/// group-wide login attempt limit (the local-account login rate limit of KIM-D29) as a persistent, versioned Settings record.
/// Registered, NOT granted: no role, seed or user receives it here (a human grant gate). Company-wide only (measured in the
/// server-resolved company the person works in). Not Critical: no approved source adds grant-time dual control.
/// </summary>
public static class SettingsLoginRateLimitPermissionManifestV1
{
    /// <summary>Read the login attempt limit timeline and record new versions.</summary>
    public const string Manage = "settings.login-rate-limit.manage";
}

/// <summary>
/// The closed list of what one login attempt window counts per (KIM-D29 / KIM-D34). The names equal the Identity
/// configuration's partition names, so the host maps a stored value without a numbering table.
/// </summary>
public static class LoginRateLimitPartitionsV1
{
    /// <summary>One window per client IP address.</summary>
    public const string IpAddress = "IpAddress";

    /// <summary>One window per client IP address and normalized user name (the KIM-D34 starting value).</summary>
    public const string IpAddressAndUserName = "IpAddressAndUserName";

    public static IReadOnlyList<string> All { get; } = [IpAddress, IpAddressAndUserName];
}

/// <summary>Technical bounds of the inputs (storage and transport shape, not policy).</summary>
public static class LoginRateLimitInputLimitsV1
{
    /// <summary>SAT-D68 precedent: the mandatory reason of every change.</summary>
    public const int ReasonMaximumLength = 4000;

    /// <summary>The smallest number of attempts a window allows (a limiter needs at least one permit).</summary>
    public const int MinimumPermitLimit = 1;

    /// <summary>
    /// The largest number of attempts a window may allow: a technical guard that keeps the value an ordinary 32-bit permit count
    /// of the in-process limiter, not a business value.
    /// </summary>
    public const int MaximumPermitLimit = 1_000_000;

    /// <summary>The shortest window in seconds (the limiter needs a positive window).</summary>
    public const int MinimumWindowSeconds = 1;

    /// <summary>
    /// The longest window in seconds (one day): a technical guard of the in-process fixed window, which keeps its counters in
    /// memory, not a business value.
    /// </summary>
    public const int MaximumWindowSeconds = 86_400;
}

/// <summary>Published problem codes of the login attempt limit routes.</summary>
public static class LoginRateLimitProblemCodesV1
{
    public const string RequestInvalid = "settings_login_rate_limit_request_invalid";
    public const string CommandMetadataInvalid = "settings_login_rate_limit_command_metadata_invalid";
    public const string ReasonRequired = "settings_login_rate_limit_reason_required";
    public const string ReasonTooLong = "settings_login_rate_limit_reason_too_long";
    public const string IdempotencyConflict = "settings_login_rate_limit_idempotency_conflict";
    public const string VersionConflict = "settings_login_rate_limit_version_conflict";
    public const string ProviderUnavailable = "settings_provider_unavailable";
    public const string ProfileInvalid = "settings_profile_invalid";
    public const string AccessDenied = "settings_access_denied";
}

/// <summary>
/// A new group-wide login attempt limit version. Every field is mandatory; nothing has a code default (KIM-D34: the value is a
/// Settings value; the decision names 10 attempts per minute per IP + normalized user name as the starting value, which the
/// host's configuration carries until the first version is recorded):
/// <list type="bullet">
/// <item><c>PermitLimit</c>: attempts allowed in one window
/// (<see cref="LoginRateLimitInputLimitsV1.MinimumPermitLimit"/>…<see cref="LoginRateLimitInputLimitsV1.MaximumPermitLimit"/>);</item>
/// <item><c>WindowSeconds</c>: the fixed window length in seconds
/// (<see cref="LoginRateLimitInputLimitsV1.MinimumWindowSeconds"/>…<see cref="LoginRateLimitInputLimitsV1.MaximumWindowSeconds"/>);</item>
/// <item><c>PartitionBy</c>: one of <see cref="LoginRateLimitPartitionsV1.All"/>;</item>
/// <item><c>Reason</c>: the mandatory reason (≤ <see cref="LoginRateLimitInputLimitsV1.ReasonMaximumLength"/>).</item>
/// </list>
/// A version takes effect when it is saved (the login path reads it on its next refresh). <c>ExpectedVersion</c> is the latest
/// stored version ("0" when none).
/// </summary>
public sealed record RecordLoginRateLimitRequestV1(
    string? ExpectedVersion,
    int? PermitLimit,
    int? WindowSeconds,
    string? PartitionBy,
    string? Reason);

/// <summary>One stored version; <c>IsCurrent</c> marks the latest one, which the login path uses.</summary>
public sealed record LoginRateLimitVersionViewV1(
    Guid ProfileId,
    string Version,
    int PermitLimit,
    int WindowSeconds,
    string PartitionBy,
    bool IsCurrent,
    string RecordedBy,
    Guid RecordedInCompanyId,
    DateTimeOffset RecordedAtUtc,
    string Reason);

/// <summary>
/// The group-wide login attempt limit timeline (newest first). <c>Version</c> is the latest stored version ("0" when none) —
/// the next record's expected version. An empty list means no version is recorded yet: the login path then uses the host's
/// configured starting value (<c>Identity:LocalAccount:RateLimit:Login</c>, KIM-D29).
/// </summary>
public sealed record LoginRateLimitViewV1(
    string Version,
    IReadOnlyList<LoginRateLimitVersionViewV1> Versions);

/// <summary>A command's terminal answer (stored for replay). <c>Code</c> is null for a success.</summary>
public sealed record LoginRateLimitCommandAnswerV1(int StatusCode, string? Code, string Title, string Detail);

public sealed record LoginRateLimitCommandResultV1(
    LoginRateLimitCommandAnswerV1 Answer,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>
/// The login attempt limit administration (KIM-D34). Every read and command is authorized first on
/// <see cref="SettingsLoginRateLimitPermissionManifestV1.Manage"/> company-wide in the server-resolved company and re-measured
/// before every save. <see cref="UnauthorizedAccessException"/> means 403. One person decides (SAT-D68 precedent): a mandatory
/// reason, Idempotency-Key, expected version and one audit event per real change (none for a no-op), recorded under the
/// company the person worked in.
/// </summary>
public interface ISettingsLoginRateLimitAdministrationV1
{
    Task<LoginRateLimitViewV1> GetAsync(CancellationToken cancellationToken = default);

    Task<LoginRateLimitCommandResultV1> RecordAsync(RecordLoginRateLimitRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

/// <summary>The group-wide login attempt limit in force (the provider's answer): the latest stored version.</summary>
public sealed record SettingsLoginRateLimitV1(
    ProfileVersionV1 Profile,
    int PermitLimit,
    TimeSpan Window,
    string PartitionBy);

/// <summary>
/// The read port the host's login rate limiter asks for the group-wide login attempt limit (KIM-D34). It runs on the anonymous
/// login path, so it asks for no user and no company: the record is group-wide and carries no company data. Answers: the
/// latest stored version, or <c>null</c> when none is recorded yet (the host then keeps its configured starting value).
/// Errors: cancelled → <see cref="OperationCanceledException"/>; an unreadable store →
/// <see cref="SettingsPrerequisiteException"/> <c>settings_provider_unavailable</c>; malformed stored data →
/// <c>settings_profile_invalid</c>. The host keeps the limit it last applied on an error; it never lifts the limit.
/// </summary>
public interface ISettingsLoginRateLimitProviderV1
{
    Task<SettingsLoginRateLimitV1?> GetLoginRateLimitAsync(CancellationToken cancellationToken = default);
}
