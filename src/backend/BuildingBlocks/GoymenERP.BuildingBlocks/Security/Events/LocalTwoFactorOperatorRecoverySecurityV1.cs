using System.Data;
using GoymenERP.BuildingBlocks.Observability;

namespace GoymenERP.BuildingBlocks.Security.Events;

/// <summary>Closed platform signal. Target/operator provenance belongs to typed Audit; this envelope has no login identifier or identifier hash.</summary>
public sealed record LocalTwoFactorOperatorRecoverySecurityV1(Guid EventId, bool Recovered, DateTimeOffset OccurredAtUtc,
    string CorrelationId, string? CausationId)
{
    public const string Actor = "identity.recover-two-factor";
    public SecurityEventV1 ToEvent() => new()
    {
        EventId = EventId, OccurredAtUtc = OccurredAtUtc, ScopeKind = SecurityEventScopeKindsV1.Platform,
        ActorType = SecurityEventActorTypesV1.System, ActorId = Actor,
        EventType = Recovered ? SecurityEventTypesV1.OperatorTwoFactorRecovered : SecurityEventTypesV1.OperatorTwoFactorRecoveryRefused,
        Outcome = Recovered ? SecurityEventOutcomesV1.Allowed : SecurityEventOutcomesV1.Denied,
        ReasonCode = Recovered ? null : SecurityEventReasonCodesV1.PolicyRefused, Severity = SecurityEventSeveritiesV1.High,
        SourceChannel = SourceChannels.System, CorrelationId = CorrelationId, CausationId = CausationId,
    };
}

/// <summary>SecurityEvents owns the append, borrowing the exact open Serializable credential transaction without committing it.</summary>
public interface ILocalTwoFactorOperatorRecoverySecurityWriterV1
{
    Task AppendAsync(IDbTransaction transaction, LocalTwoFactorOperatorRecoverySecurityV1 record, CancellationToken cancellationToken = default);
}

public static partial class SecurityEventV1Validator
{
    private static void ValidateOperatorTwoFactorRecovery(SecurityEventV1 e, List<string> errors)
    {
        if (!SecurityEventTypesV1.OperatorTwoFactorRecoveryTypes.Contains(e.EventType, StringComparer.Ordinal)) return;
        if (e.ScopeKind != SecurityEventScopeKindsV1.Platform || e.CompanyId is not null || e.RequestedCompanyId is not null
            || e.ActorType != SecurityEventActorTypesV1.System || e.ActorId != LocalTwoFactorOperatorRecoverySecurityV1.Actor
            || e.SourceChannel != SourceChannels.System || e.Severity != SecurityEventSeveritiesV1.High || e.PermissionCode is not null
            || e.PolicyId is not null || e.PolicyVersion is not null || e.RateLimitSummary is not null)
            errors.Add("operator_two_factor_recovery_context_invalid");
        if (e.EventType == SecurityEventTypesV1.OperatorTwoFactorRecoveryRefused && e.ReasonCode != SecurityEventReasonCodesV1.PolicyRefused)
            errors.Add("operator_two_factor_recovery_reason_invalid");
    }
}
