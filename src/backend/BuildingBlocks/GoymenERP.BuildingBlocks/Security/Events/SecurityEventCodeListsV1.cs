namespace GoymenERP.BuildingBlocks.Security.Events;

/// <summary>
/// Closed, versioned security event type list (ADR-SEC-001 §4, schema v1). Adding a value is a contract change:
/// the failure-mode and retention-class maps and the database check constraint change in the same PR.
/// </summary>
public static class SecurityEventTypesV1
{
    public const int Version = 1;

    public const string LoginSucceeded = "auth.login.succeeded";
    public const string LoginFailed = "auth.login.failed";
    public const string AuthorizationDenied = "authz.denied";
    public const string AiAuthorizationDenied = "ai.authz.denied";
    public const string PermissionChanged = "identity.permission.changed";
    public const string PolicyPublished = "settings.policy.published";
    public const string ExportPerformed = "export.performed";
    public const string RetentionPurgeRequested = "retention.purge.requested";
    public const string AiToolCalled = "ai.tool.called";
    public const string AiRequestRefused = "ai.request.refused";
    public const string SecurityEventsRead = "security.events.read";

    /// <summary>
    /// One-minute summary of login attempts refused by the login rate limiter (KIM-D29). Platform scope, anonymous
    /// actor, no company; it carries only the <see cref="SecurityEventRateLimitSummaryV1"/> counters and the policy
    /// reference, never a username, username hash or IP address.
    /// </summary>
    public const string LoginRateLimited = "auth.login.rate_limited";

    /// <summary>
    /// The terminal recovery command reactivated the inactive bootstrap founder (ID-2r <c>recover-founder reactivate</c>,
    /// KIM-D22). Platform scope, no company, <c>System</c> actor, always <c>Allowed</c>; written fail-closed before the
    /// command's commit, so a recovery the channel cannot record does not happen. Never carries a password, hash or stamp.
    /// </summary>
    public const string FounderRecovered = "identity.founder.recovered";

    /// <summary>
    /// The terminal recovery command issued a one-time temporary password to the active bootstrap founder (ID-2r
    /// <c>recover-founder reset-password</c>, KIM-D23/D43). Same envelope rules as <see cref="FounderRecovered"/>.
    /// </summary>
    public const string FounderPasswordReset = "identity.founder.password_reset";

    /// <summary>
    /// One refused <c>recover-founder</c> run (either sub-command or none). Platform scope, no company, <c>System</c>
    /// actor, always <c>Denied</c> with <c>policy_refused</c>; the refusal stays in force when the channel cannot record
    /// it (alarm). Not a denial aggregate: every run is its own row.
    /// </summary>
    public const string FounderRecoveryRefused = "identity.founder.recovery_refused";

    public const string OperatorTwoFactorRecovered = "identity.two_factor.recovered";
    public const string OperatorTwoFactorRecoveryRefused = "identity.two_factor.recovery_refused";
    public static IReadOnlyList<string> OperatorTwoFactorRecoveryTypes { get; } =
        [OperatorTwoFactorRecovered, OperatorTwoFactorRecoveryRefused];

    /// <summary>The ID-2r founder recovery types: Platform scope, no company, System actor, no permission code.</summary>
    public static IReadOnlyList<string> FounderRecoveryTypes { get; } =
        [FounderRecovered, FounderPasswordReset, FounderRecoveryRefused];

    public static IReadOnlyList<string> All { get; } =
    [
        LoginSucceeded, LoginFailed, AuthorizationDenied, AiAuthorizationDenied, PermissionChanged,
        PolicyPublished, ExportPerformed, RetentionPurgeRequested, AiToolCalled, AiRequestRefused,
        SecurityEventsRead, LoginRateLimited, FounderRecovered, FounderPasswordReset, FounderRecoveryRefused,
        OperatorTwoFactorRecovered, OperatorTwoFactorRecoveryRefused,
    ];

    public static bool IsDefined(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);

    /// <summary>
    /// Event types whose decision is a single denial or refusal; the denial stays in force on write failure. They
    /// are exactly the aggregation-eligible types. <see cref="LoginRateLimited"/> is also always <c>Denied</c>, but it
    /// is already a counted summary, so it is not listed here and is never aggregated again.
    /// </summary>
    public static IReadOnlyList<string> DenialTypes { get; } =
        [LoginFailed, AuthorizationDenied, AiAuthorizationDenied, AiRequestRefused];

    /// <summary>Event types that may carry AI tool identity fields.</summary>
    public static IReadOnlyList<string> AiTypes { get; } = [AiToolCalled, AiAuthorizationDenied, AiRequestRefused];
}

/// <summary>Event phase. Only <see cref="SecurityEventTypesV1.AiToolCalled"/> uses Started/Completed.</summary>
public static class SecurityEventPhasesV1
{
    public const int Version = 1;
    public const string Single = "Single";
    public const string Started = "Started";
    public const string Completed = "Completed";

    public static IReadOnlyList<string> All { get; } = [Single, Started, Completed];

    public static bool IsDefined(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);
}

public static class SecurityEventOutcomesV1
{
    public const int Version = 1;
    public const string Allowed = "Allowed";
    public const string Denied = "Denied";
    public const string Failed = "Failed";
    public const string Unknown = "Unknown";

    public static IReadOnlyList<string> All { get; } = [Allowed, Denied, Failed, Unknown];

    public static bool IsDefined(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);
}

public static class SecurityEventSeveritiesV1
{
    public const int Version = 1;
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string High = "High";

    public static IReadOnlyList<string> All { get; } = [Info, Warning, High];

    public static bool IsDefined(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);
}

/// <summary>Company rows carry a server-resolved CompanyId; platform rows never carry one.</summary>
public static class SecurityEventScopeKindsV1
{
    public const int Version = 1;
    public const string Company = "Company";
    public const string Platform = "Platform";

    public static IReadOnlyList<string> All { get; } = [Company, Platform];

    public static bool IsDefined(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);
}

/// <summary>Actor kinds. Anonymous carries no actor id; no username or e-mail is ever stored (E8).</summary>
public static class SecurityEventActorTypesV1
{
    public const int Version = 1;
    public const string User = "User";
    public const string System = "System";
    public const string Ai = "Ai";
    public const string Anonymous = "Anonymous";

    public static IReadOnlyList<string> All { get; } = [User, System, Ai, Anonymous];

    public static bool IsDefined(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);
}

/// <summary>Closed, versioned reason codes. There is no free-text reason.</summary>
public static class SecurityEventReasonCodesV1
{
    public const int Version = 1;
    public const string PermissionMissing = "permission_missing";
    public const string CompanyContextMismatch = "company_context_mismatch";
    public const string CredentialsInvalid = "credentials_invalid";
    public const string AccountInactive = "account_inactive";
    public const string SessionInvalid = "session_invalid";
    public const string PolicyRefused = "policy_refused";
    public const string AiToolNotAllowed = "ai_tool_not_allowed";
    public const string AiDataClassNotAllowed = "ai_data_class_not_allowed";
    public const string AiApprovalMissing = "ai_approval_missing";
    public const string AiProviderFailed = "ai_provider_failed";
    public const string OperationFailed = "operation_failed";

    public static IReadOnlyList<string> All { get; } =
    [
        PermissionMissing, CompanyContextMismatch, CredentialsInvalid, AccountInactive, SessionInvalid,
        PolicyRefused, AiToolNotAllowed, AiDataClassNotAllowed, AiApprovalMissing, AiProviderFailed,
        OperationFailed,
    ];

    public static bool IsDefined(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);
}
