using System.Security.Cryptography;
using System.Text.Json;

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
    DateTimeOffset? AggregationWindowEndUtc);

/// <summary>Row integrity hash, the same construction as <c>AuditEventIntegrity</c>.</summary>
public static class SecurityEventIntegrity
{
    public static string Compute(SecurityEventIntegrityEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(envelope)));
    }
}
