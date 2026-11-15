namespace GoymenERP.BuildingBlocks.Security.Events;

/// <summary>
/// F023-2 / KIM-D10: the one shared helper that records an authorization refusal (401, 403, an invalid or foreign
/// company context, a lost service fence) as a single <c>authz.denied</c> security event. Refusals are recorded only
/// in the security event channel, never in the audit table.
/// </summary>
/// <remarks>
/// The recorder is request scoped and records at most once per request: the first refusal wins, later calls in the
/// same request return without writing. The actor, scope, company and correlation come from the server-resolved
/// request context, never from the caller. A write failure never changes the refusal; the writer raises the loss
/// metric (ADR-SEC-001 E1, fail-open with alarm).
/// </remarks>
public interface IAuthorizationDenialRecorderV1
{
    /// <summary>True once a refusal has been recorded or attempted in this request.</summary>
    bool HasAttempted { get; }

    /// <summary>Records the refusal once per request. Never throws for a write failure.</summary>
    Task<SecurityEventWriteResultV1> RecordAsync(AuthorizationDenialV1 denial, CancellationToken cancellationToken = default);
}

/// <summary>
/// The restricted refusal draft a caller hands to <see cref="IAuthorizationDenialRecorderV1"/>. It carries no free
/// text, no request body and no client address; adding a field is a contract change.
/// </summary>
public sealed record AuthorizationDenialV1
{
    /// <summary>One of <see cref="AuthorizationDenialReasonCodesV1.All"/>.</summary>
    public required string ReasonCode { get; init; }

    /// <summary>The registered permission the refused operation required, when known. An unknown name is dropped.</summary>
    public string? PermissionCode { get; init; }

    /// <summary>
    /// The company the request asked for. Kept only for <see cref="AuthorizationDenialReasonCodesV1.CompanyContextMismatch"/>
    /// and only for an authenticated actor (ADR-SEC-001 §3); an anonymous request never stores it.
    /// </summary>
    public Guid? RequestedCompanyId { get; init; }
}

/// <summary>The closed reason list for <c>authz.denied</c> (F023 spec, "Güvenlik kanalı"). No new event type is added.</summary>
public static class AuthorizationDenialReasonCodesV1
{
    public const int Version = 1;

    public const string PermissionMissing = SecurityEventReasonCodesV1.PermissionMissing;
    public const string CompanyContextMismatch = SecurityEventReasonCodesV1.CompanyContextMismatch;
    public const string SessionInvalid = SecurityEventReasonCodesV1.SessionInvalid;

    public static IReadOnlyList<string> All { get; } = [PermissionMissing, CompanyContextMismatch, SessionInvalid];

    public static bool IsDefined(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);
}
