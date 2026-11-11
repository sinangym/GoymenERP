namespace GoymenERP.BuildingBlocks.Security.Events;

/// <summary>What the caller does when the security event cannot be written (#66 6806619303 item 11, E1).</summary>
public enum SecurityEventWriteFailureMode
{
    /// <summary>The guarded operation must not happen: no permission change, no export, no read, no AI tool call.</summary>
    FailClosed = 1,

    /// <summary>
    /// The caller continues with its own decision and a loss metric is emitted. For denial/refusal types the
    /// caller's decision is the denial, so the denial stays in force.
    /// </summary>
    FailOpenWithAlarm = 2,
}

/// <summary>The single (event type, phase) → write failure mode map. Defined once; every writer uses it.</summary>
public static class SecurityEventWriteFailureModesV1
{
    public const int Version = 1;

    private static readonly IReadOnlyDictionary<(string EventType, string Phase), SecurityEventWriteFailureMode> Map =
        new Dictionary<(string, string), SecurityEventWriteFailureMode>
        {
            [(SecurityEventTypesV1.LoginSucceeded, SecurityEventPhasesV1.Single)] = SecurityEventWriteFailureMode.FailOpenWithAlarm,
            [(SecurityEventTypesV1.LoginFailed, SecurityEventPhasesV1.Single)] = SecurityEventWriteFailureMode.FailOpenWithAlarm,
            [(SecurityEventTypesV1.AuthorizationDenied, SecurityEventPhasesV1.Single)] = SecurityEventWriteFailureMode.FailOpenWithAlarm,
            [(SecurityEventTypesV1.AiAuthorizationDenied, SecurityEventPhasesV1.Single)] = SecurityEventWriteFailureMode.FailOpenWithAlarm,
            [(SecurityEventTypesV1.PermissionChanged, SecurityEventPhasesV1.Single)] = SecurityEventWriteFailureMode.FailClosed,
            [(SecurityEventTypesV1.PolicyPublished, SecurityEventPhasesV1.Single)] = SecurityEventWriteFailureMode.FailClosed,
            [(SecurityEventTypesV1.ExportPerformed, SecurityEventPhasesV1.Single)] = SecurityEventWriteFailureMode.FailClosed,
            [(SecurityEventTypesV1.RetentionPurgeRequested, SecurityEventPhasesV1.Single)] = SecurityEventWriteFailureMode.FailClosed,
            [(SecurityEventTypesV1.AiToolCalled, SecurityEventPhasesV1.Started)] = SecurityEventWriteFailureMode.FailClosed,
            [(SecurityEventTypesV1.AiToolCalled, SecurityEventPhasesV1.Completed)] = SecurityEventWriteFailureMode.FailOpenWithAlarm,
            [(SecurityEventTypesV1.AiRequestRefused, SecurityEventPhasesV1.Single)] = SecurityEventWriteFailureMode.FailOpenWithAlarm,
            [(SecurityEventTypesV1.SecurityEventsRead, SecurityEventPhasesV1.Single)] = SecurityEventWriteFailureMode.FailClosed,
        };

    /// <summary>Every valid (type, phase) pair, in a stable order.</summary>
    public static IReadOnlyList<(string EventType, string Phase)> Pairs { get; } = [.. Map.Keys];

    public static bool IsValidPhase(string? eventType, string? phase) =>
        eventType is not null && phase is not null && Map.ContainsKey((eventType, phase));

    /// <summary>
    /// Returns the mode for a valid pair. An unknown pair resolves to <see cref="SecurityEventWriteFailureMode.FailClosed"/>
    /// so that a malformed event never lets a guarded operation through.
    /// </summary>
    public static SecurityEventWriteFailureMode Resolve(string? eventType, string? phase) =>
        eventType is not null && phase is not null && Map.TryGetValue((eventType, phase), out var mode)
            ? mode
            : SecurityEventWriteFailureMode.FailClosed;
}

/// <summary>
/// Which (event type, phase) pairs may be aggregated into one counted row (#66 6806860620 item 15, E7): only
/// denial/refusal events. Fail-closed pairs are never aggregated. Window and threshold are configuration
/// (<c>SecurityEvents:Aggregation</c>); while unset, aggregation is disabled and every event is a single row.
/// </summary>
public static class SecurityEventAggregationEligibilityV1
{
    public const int Version = 1;

    private static readonly IReadOnlySet<(string EventType, string Phase)> Eligible = new HashSet<(string, string)>
    {
        (SecurityEventTypesV1.LoginFailed, SecurityEventPhasesV1.Single),
        (SecurityEventTypesV1.AuthorizationDenied, SecurityEventPhasesV1.Single),
        (SecurityEventTypesV1.AiAuthorizationDenied, SecurityEventPhasesV1.Single),
        (SecurityEventTypesV1.AiRequestRefused, SecurityEventPhasesV1.Single),
    };

    public static IReadOnlyList<(string EventType, string Phase)> Pairs { get; } = [.. Eligible];

    public static bool IsEligible(string? eventType, string? phase) =>
        eventType is not null && phase is not null && Eligible.Contains((eventType, phase))
        && SecurityEventWriteFailureModesV1.Resolve(eventType, phase) == SecurityEventWriteFailureMode.FailOpenWithAlarm;
}

/// <summary>
/// Event type → retention class. Durations and policy references are configuration
/// (<c>SecurityEvents:Retention:&lt;Class&gt;</c>), never code constants.
/// </summary>
public static class SecurityEventRetentionClassesV1
{
    public const int Version = 1;

    /// <summary>Security/authorization log retention (Sinan 2026-10-21 No 134).</summary>
    public const string No134 = "No134";

    /// <summary>AI tool call and refusal retention (Sinan 2026-10-21 No 157).</summary>
    public const string No157 = "No157";

    /// <summary>AI-originated authorization denial retention (#66 6806619303 item 11, E9).</summary>
    public const string AiAuthorizationDenied = "AiAuthorizationDenied";

    public static IReadOnlyList<string> All { get; } = [No134, No157, AiAuthorizationDenied];

    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [SecurityEventTypesV1.LoginSucceeded] = No134,
        [SecurityEventTypesV1.LoginFailed] = No134,
        [SecurityEventTypesV1.AuthorizationDenied] = No134,
        [SecurityEventTypesV1.AiAuthorizationDenied] = AiAuthorizationDenied,
        [SecurityEventTypesV1.PermissionChanged] = No134,
        [SecurityEventTypesV1.PolicyPublished] = No134,
        [SecurityEventTypesV1.ExportPerformed] = No134,
        [SecurityEventTypesV1.RetentionPurgeRequested] = No134,
        [SecurityEventTypesV1.AiToolCalled] = No157,
        [SecurityEventTypesV1.AiRequestRefused] = No157,
        [SecurityEventTypesV1.SecurityEventsRead] = No134,
    };

    public static string? ForEventType(string? eventType) =>
        eventType is not null && Map.TryGetValue(eventType, out var retentionClass) ? retentionClass : null;
}
