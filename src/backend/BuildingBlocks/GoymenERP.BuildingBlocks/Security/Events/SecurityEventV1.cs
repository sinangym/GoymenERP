using System.Text.RegularExpressions;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Observability;

namespace GoymenERP.BuildingBlocks.Security.Events;

/// <summary>
/// Security event envelope, schema v1 (ADR-SEC-001 §3). It deliberately has no IP address, user agent, username,
/// e-mail or identifier hash (#66 6806619303 item 11, E5/E8), no free-text reason and no request/prompt payload.
/// Retention and integrity fields are added by the writer, never by the caller.
/// </summary>
public sealed record SecurityEventV1
{
    public const int CurrentSchemaVersion = 1;
    public const int ActorIdMaxLength = 128;
    public const int PermissionCodeMaxLength = 160;
    public const int PolicyIdMaxLength = 128;
    public const int CorrelationIdMaxLength = 128;
    public const int AiToolIdMaxLength = 96;
    public const int AiToolVersionMaxLength = 32;

    public required Guid EventId { get; init; }
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required DateTimeOffset OccurredAtUtc { get; init; }

    public required string ScopeKind { get; init; }
    public Guid? CompanyId { get; init; }
    public Guid? RequestedCompanyId { get; init; }

    public required string ActorType { get; init; }
    public string? ActorId { get; init; }

    public required string EventType { get; init; }
    public string Phase { get; init; } = SecurityEventPhasesV1.Single;
    public required string Outcome { get; init; }
    public string? ReasonCode { get; init; }
    public required string Severity { get; init; }
    public string? PermissionCode { get; init; }
    public string? PolicyId { get; init; }
    public int? PolicyVersion { get; init; }

    public required string CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public required string SourceChannel { get; init; }

    public string? AiToolId { get; init; }
    public string? AiToolVersion { get; init; }
    public AiToolRiskClassV1? AiRiskClass { get; init; }
    public AiDataClassV1? AiDataClass { get; init; }
}

/// <summary>Validates a <see cref="SecurityEventV1"/>; an invalid envelope is never persisted.</summary>
public static partial class SecurityEventV1Validator
{
    private static readonly IReadOnlyList<string> SourceChannelValues =
    [
        SourceChannels.Web, SourceChannels.Mobile, SourceChannels.Import, SourceChannels.Integration,
        SourceChannels.AiAssisted, SourceChannels.System,
    ];

    public static IReadOnlyList<string> SourceChannelsV1 => SourceChannelValues;

    /// <summary>Returns stable English violation codes; an empty list means the envelope is valid.</summary>
    public static IReadOnlyList<string> Validate(SecurityEventV1? securityEvent)
    {
        if (securityEvent is null)
        {
            return ["event_missing"];
        }

        var errors = new List<string>();
        var e = securityEvent;

        if (e.EventId == Guid.Empty) errors.Add("event_id_empty");
        if (e.SchemaVersion != SecurityEventV1.CurrentSchemaVersion) errors.Add("schema_version_unsupported");
        if (e.OccurredAtUtc == default || e.OccurredAtUtc.Offset != TimeSpan.Zero) errors.Add("occurred_at_not_utc");

        ValidateScope(e, errors);
        ValidateActor(e, errors);
        ValidateEvent(e, errors);
        ValidateReferences(e, errors);
        ValidateAiFields(e, errors);

        return errors;
    }

    public static bool IsValid(SecurityEventV1? securityEvent) => Validate(securityEvent).Count == 0;

    private static void ValidateScope(SecurityEventV1 e, List<string> errors)
    {
        if (!SecurityEventScopeKindsV1.IsDefined(e.ScopeKind))
        {
            errors.Add("scope_kind_unknown");
        }
        else if (e.ScopeKind == SecurityEventScopeKindsV1.Company && (e.CompanyId is null || e.CompanyId == Guid.Empty))
        {
            errors.Add("company_scope_requires_company_id");
        }
        else if (e.ScopeKind == SecurityEventScopeKindsV1.Platform && e.CompanyId is not null)
        {
            errors.Add("platform_scope_forbids_company_id");
        }

        if (e.RequestedCompanyId == Guid.Empty) errors.Add("requested_company_id_empty");
    }

    private static void ValidateActor(SecurityEventV1 e, List<string> errors)
    {
        if (!SecurityEventActorTypesV1.IsDefined(e.ActorType))
        {
            errors.Add("actor_type_unknown");
            return;
        }

        if (e.ActorType == SecurityEventActorTypesV1.Anonymous)
        {
            if (e.ActorId is not null) errors.Add("anonymous_actor_forbids_actor_id");
            return;
        }

        if (!IsBoundedToken(e.ActorId, SecurityEventV1.ActorIdMaxLength))
        {
            errors.Add("actor_id_invalid");
        }
        else if (e.ActorId!.Contains('@', StringComparison.Ordinal))
        {
            // Defence in depth for E8: an internal actor id is never an e-mail address.
            errors.Add("actor_id_looks_like_email");
        }
        else if (e.ActorType == SecurityEventActorTypesV1.User && !IsCanonicalInternalUserId(e.ActorId))
        {
            // E8: a user actor is the internal user id (Guid "D", as issued by the claims transformation), never a
            // username, login name or display name typed by someone.
            errors.Add("user_actor_id_not_internal_id");
        }

        if (e.EventType == SecurityEventTypesV1.LoginFailed && e.ActorType != SecurityEventActorTypesV1.Anonymous)
        {
            // E8: a failed login has no authenticated actor; the attempted username must never reach ActorId.
            errors.Add("login_failed_requires_anonymous_actor");
        }
    }

    private static bool IsCanonicalInternalUserId(string actorId) =>
        Guid.TryParseExact(actorId, "D", out var userId)
        && userId != Guid.Empty
        && string.Equals(userId.ToString("D"), actorId, StringComparison.Ordinal);

    private static void ValidateEvent(SecurityEventV1 e, List<string> errors)
    {
        if (!SecurityEventTypesV1.IsDefined(e.EventType)) errors.Add("event_type_unknown");
        if (!SecurityEventPhasesV1.IsDefined(e.Phase)) errors.Add("phase_unknown");
        if (!SecurityEventOutcomesV1.IsDefined(e.Outcome)) errors.Add("outcome_unknown");
        if (!SecurityEventSeveritiesV1.IsDefined(e.Severity)) errors.Add("severity_unknown");

        if (SecurityEventTypesV1.IsDefined(e.EventType) && SecurityEventPhasesV1.IsDefined(e.Phase)
            && !SecurityEventWriteFailureModesV1.IsValidPhase(e.EventType, e.Phase))
        {
            errors.Add("phase_not_allowed_for_event_type");
        }

        if (SecurityEventTypesV1.IsDefined(e.EventType) && SecurityEventOutcomesV1.IsDefined(e.Outcome)
            && !IsOutcomeAllowed(e.EventType, e.Phase, e.Outcome))
        {
            errors.Add("outcome_not_allowed_for_event_type");
        }

        if (e.ReasonCode is not null && !SecurityEventReasonCodesV1.IsDefined(e.ReasonCode))
        {
            errors.Add("reason_code_unknown");
        }

        if (e.Outcome is SecurityEventOutcomesV1.Denied or SecurityEventOutcomesV1.Failed && e.ReasonCode is null)
        {
            errors.Add("reason_code_required");
        }

        if (e.Outcome == SecurityEventOutcomesV1.Allowed && e.ReasonCode is not null)
        {
            errors.Add("reason_code_forbidden_for_allowed");
        }
    }

    private static bool IsOutcomeAllowed(string eventType, string phase, string outcome)
    {
        if (eventType == SecurityEventTypesV1.LoginSucceeded) return outcome == SecurityEventOutcomesV1.Allowed;
        if (SecurityEventTypesV1.DenialTypes.Contains(eventType, StringComparer.Ordinal)) return outcome == SecurityEventOutcomesV1.Denied;
        if (eventType == SecurityEventTypesV1.AiToolCalled)
        {
            return phase switch
            {
                SecurityEventPhasesV1.Started => outcome == SecurityEventOutcomesV1.Allowed,
                SecurityEventPhasesV1.Completed => outcome is SecurityEventOutcomesV1.Allowed or SecurityEventOutcomesV1.Failed,
                _ => false,
            };
        }

        return outcome is SecurityEventOutcomesV1.Allowed or SecurityEventOutcomesV1.Failed or SecurityEventOutcomesV1.Unknown;
    }

    private static void ValidateReferences(SecurityEventV1 e, List<string> errors)
    {
        if (e.PermissionCode is not null
            && (e.PermissionCode.Length > SecurityEventV1.PermissionCodeMaxLength || !PermissionCodePattern().IsMatch(e.PermissionCode)))
        {
            errors.Add("permission_code_invalid");
        }

        if ((e.PolicyId is null) != (e.PolicyVersion is null)) errors.Add("policy_reference_incomplete");
        if (e.PolicyId is not null && !IsBoundedToken(e.PolicyId, SecurityEventV1.PolicyIdMaxLength)) errors.Add("policy_id_invalid");
        if (e.PolicyVersion is <= 0) errors.Add("policy_version_invalid");

        if (!IsBoundedToken(e.CorrelationId, SecurityEventV1.CorrelationIdMaxLength)) errors.Add("correlation_id_invalid");
        if (e.CausationId is not null && !IsBoundedToken(e.CausationId, SecurityEventV1.CorrelationIdMaxLength)) errors.Add("causation_id_invalid");
        if (e.SourceChannel is null || !SourceChannelValues.Contains(e.SourceChannel, StringComparer.Ordinal)) errors.Add("source_channel_unknown");
    }

    private static void ValidateAiFields(SecurityEventV1 e, List<string> errors)
    {
        var hasAny = e.AiToolId is not null || e.AiToolVersion is not null || e.AiRiskClass is not null || e.AiDataClass is not null;
        var hasAll = e.AiToolId is not null && e.AiToolVersion is not null && e.AiRiskClass is not null && e.AiDataClass is not null;

        if (!SecurityEventTypesV1.AiTypes.Contains(e.EventType, StringComparer.Ordinal))
        {
            if (hasAny) errors.Add("ai_fields_forbidden_for_event_type");
            return;
        }

        if (e.EventType == SecurityEventTypesV1.AiToolCalled && !hasAll)
        {
            errors.Add("ai_fields_required_for_tool_call");
            return;
        }

        if (hasAny && !hasAll)
        {
            errors.Add("ai_fields_incomplete");
            return;
        }

        if (!hasAll) return;

        if (e.AiToolId!.Length > SecurityEventV1.AiToolIdMaxLength || !AiToolDescriptorV1.IsWellFormedToolId(e.AiToolId))
        {
            errors.Add("ai_tool_id_invalid");
        }

        if (!IsBoundedToken(e.AiToolVersion, SecurityEventV1.AiToolVersionMaxLength)) errors.Add("ai_tool_version_invalid");
        if (!Enum.IsDefined(e.AiRiskClass!.Value)) errors.Add("ai_risk_class_unknown");
        if (!Enum.IsDefined(e.AiDataClass!.Value)) errors.Add("ai_data_class_unknown");
    }

    /// <summary>A bounded opaque token: printable ASCII, no whitespace, no free text.</summary>
    private static bool IsBoundedToken(string? value, int maxLength) =>
        value is { Length: > 0 } && value.Length <= maxLength && TokenPattern().IsMatch(value);

    [GeneratedRegex("\\A[A-Za-z0-9._:@/+=-]+\\z", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    [GeneratedRegex("\\A[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)+\\z", RegexOptions.CultureInvariant)]
    private static partial Regex PermissionCodePattern();
}
