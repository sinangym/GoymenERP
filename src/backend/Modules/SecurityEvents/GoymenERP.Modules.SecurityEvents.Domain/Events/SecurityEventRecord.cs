using GoymenERP.BuildingBlocks.Security.Events;

namespace GoymenERP.Modules.SecurityEvents.Domain.Events;

/// <summary>
/// One persisted security event row (<c>security.SecurityEvents</c>). Insert-only: there is no mutator, the
/// DbContext refuses normal SaveChanges and the database trigger refuses UPDATE/DELETE.
/// </summary>
public sealed class SecurityEventRecord
{
    public const int RetentionPolicyRefMaxLength = 64;
    public const int CodeMaxLength = 32;
    public const int EventTypeMaxLength = 64;
    public const int ReasonCodeMaxLength = 64;
    public const int IntegrityHashLength = 64;

    private SecurityEventRecord()
    {
    }

    private SecurityEventRecord(SecurityEventV1 securityEvent, DateTimeOffset retainUntilUtc, string retentionPolicyRef)
    {
        Id = securityEvent.EventId;
        SchemaVersion = securityEvent.SchemaVersion;
        OccurredAtUtc = securityEvent.OccurredAtUtc;
        ScopeKind = securityEvent.ScopeKind;
        CompanyId = securityEvent.CompanyId;
        RequestedCompanyId = securityEvent.RequestedCompanyId;
        ActorType = securityEvent.ActorType;
        ActorId = securityEvent.ActorId;
        EventType = securityEvent.EventType;
        Phase = securityEvent.Phase;
        Outcome = securityEvent.Outcome;
        ReasonCode = securityEvent.ReasonCode;
        Severity = securityEvent.Severity;
        PermissionCode = securityEvent.PermissionCode;
        PolicyId = securityEvent.PolicyId;
        PolicyVersion = securityEvent.PolicyVersion;
        CorrelationId = securityEvent.CorrelationId;
        CausationId = securityEvent.CausationId;
        SourceChannel = securityEvent.SourceChannel;
        AiToolId = securityEvent.AiToolId;
        AiToolVersion = securityEvent.AiToolVersion;
        AiRiskClass = securityEvent.AiRiskClass?.ToString();
        AiDataClass = securityEvent.AiDataClass?.ToString();
        RetainUntilUtc = retainUntilUtc;
        RetentionPolicyRef = retentionPolicyRef;
        LegalHold = false;
        OccurrenceCount = 1;
        AggregationWindowStartUtc = null;
        AggregationWindowEndUtc = null;
        IntegrityHash = SecurityEventIntegrity.Compute(ToIntegrityEnvelope());
    }

    public Guid Id { get; private set; }
    public int SchemaVersion { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string ScopeKind { get; private set; } = string.Empty;
    public Guid? CompanyId { get; private set; }
    public Guid? RequestedCompanyId { get; private set; }
    public string ActorType { get; private set; } = string.Empty;
    public string? ActorId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Phase { get; private set; } = string.Empty;
    public string Outcome { get; private set; } = string.Empty;
    public string? ReasonCode { get; private set; }
    public string Severity { get; private set; } = string.Empty;
    public string? PermissionCode { get; private set; }
    public string? PolicyId { get; private set; }
    public int? PolicyVersion { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string? CausationId { get; private set; }
    public string SourceChannel { get; private set; } = string.Empty;
    public string? AiToolId { get; private set; }
    public string? AiToolVersion { get; private set; }
    public string? AiRiskClass { get; private set; }
    public string? AiDataClass { get; private set; }
    public DateTimeOffset RetainUntilUtc { get; private set; }
    public string RetentionPolicyRef { get; private set; } = string.Empty;

    /// <summary>Always false until OPEN-018 (legal hold) is decided; enforced by a database check constraint.</summary>
    public bool LegalHold { get; private set; }

    /// <summary>
    /// Number of identical denial/refusal occurrences this row stands for (E7, #66 6806860620 item 15). Always 1
    /// in AI-2a: the aggregator is follow-up AI-2c and stays off until the window/threshold are configured.
    /// </summary>
    public int OccurrenceCount { get; private set; } = 1;

    public DateTimeOffset? AggregationWindowStartUtc { get; private set; }

    public DateTimeOffset? AggregationWindowEndUtc { get; private set; }

    public string IntegrityHash { get; private set; } = string.Empty;

    /// <summary>Creates a row from a valid envelope and a configuration-resolved retention.</summary>
    public static SecurityEventRecord Create(SecurityEventV1 securityEvent, DateTimeOffset retainUntilUtc, string retentionPolicyRef)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);
        var errors = SecurityEventV1Validator.Validate(securityEvent);
        if (errors.Count > 0)
        {
            throw new ArgumentException($"Invalid security event: {string.Join(",", errors)}.", nameof(securityEvent));
        }

        if (string.IsNullOrWhiteSpace(retentionPolicyRef) || retentionPolicyRef.Length > RetentionPolicyRefMaxLength)
        {
            throw new ArgumentException("A bounded retention policy reference is required.", nameof(retentionPolicyRef));
        }

        if (retainUntilUtc.Offset != TimeSpan.Zero || retainUntilUtc <= securityEvent.OccurredAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(retainUntilUtc), "Retention must end in UTC after the event.");
        }

        return new SecurityEventRecord(securityEvent, retainUntilUtc, retentionPolicyRef);
    }

    /// <summary>The canonical envelope the integrity hash covers: every persisted column except the hash.</summary>
    public SecurityEventIntegrityEnvelope ToIntegrityEnvelope() => new(
        Id, SchemaVersion, OccurredAtUtc, ScopeKind, CompanyId, RequestedCompanyId, ActorType, ActorId, EventType,
        Phase, Outcome, ReasonCode, Severity, PermissionCode, PolicyId, PolicyVersion, CorrelationId, CausationId,
        SourceChannel, AiToolId, AiToolVersion, AiRiskClass, AiDataClass, RetainUntilUtc, RetentionPolicyRef, LegalHold,
        OccurrenceCount, AggregationWindowStartUtc, AggregationWindowEndUtc);
}
