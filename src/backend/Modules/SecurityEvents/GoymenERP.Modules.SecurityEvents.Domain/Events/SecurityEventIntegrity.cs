using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoymenERP.Modules.SecurityEvents.Domain.Events;

public sealed record SecurityEventIntegrityEnvelope(
    Guid Id,
    int SchemaVersion,
    DateTimeOffset OccurredAtUtc,
    string ScopeKind,
    Guid? CompanyId,
    Guid? RequestedCompanyId,
    string ActorType,
    string? ActorId,
    string EventType,
    string Phase,
    string Outcome,
    string? ReasonCode,
    string Severity,
    string? PermissionCode,
    string? PolicyId,
    int? PolicyVersion,
    string CorrelationId,
    string? CausationId,
    string SourceChannel,
    string? AiToolId,
    string? AiToolVersion,
    string? AiRiskClass,
    string? AiDataClass,
    DateTimeOffset RetainUntilUtc,
    string RetentionPolicyRef,
    bool LegalHold,
    int OccurrenceCount,
    DateTimeOffset? AggregationWindowStartUtc,
    DateTimeOffset? AggregationWindowEndUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? RateLimitPermitLimit = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? RateLimitWindowSeconds = null);

/// <summary>Row integrity hash, the same construction as <c>AuditEventIntegrity</c>.</summary>
/// <remarks>
/// The rate-limit columns (added with <c>auth.login.rate_limited</c>) are left out of the canonical JSON while null,
/// so every row written before them keeps the hash it was stored with.
/// </remarks>
public static class SecurityEventIntegrity
{
    public static string Compute(SecurityEventIntegrityEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(envelope)));
    }
}
