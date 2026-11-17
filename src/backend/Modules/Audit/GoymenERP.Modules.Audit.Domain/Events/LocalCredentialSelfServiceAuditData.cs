using System.Text.Json;

namespace GoymenERP.Modules.Audit.Domain.Events;

public enum LocalCredentialSelfServiceOperation { EnableTwoFactor = 1, DisableTwoFactor = 2, RegenerateRecoveryCodes = 3 }
public sealed record LocalCredentialSelfServiceAuditData(Guid EventId, Guid CompanyId, Guid ActorUserId, Guid TargetUserId,
    LocalCredentialSelfServiceOperation Operation, long CredentialVersionBefore, long CredentialVersionAfter,
    string Method, DateTimeOffset OccurredAtUtc, string Reason, string CorrelationId, string? CausationId, string IdempotencyKeyHash);

public sealed partial class AuditEvent
{
    public const string LocalCredentialSelfServiceKind = "IdentityLocalCredentialSelfServiceV1";
    public const string TwoFactorEnabledEvent = "identity.local-credentials.two-factor-enabled.v1";
    public const string TwoFactorDisabledEvent = "identity.local-credentials.two-factor-disabled.v1";
    public const string RecoveryCodesRegeneratedEvent = "identity.local-credentials.recovery-codes-regenerated.v1";

    /// <summary>Closed authenticated self-service facts. Arbitrary null-permission drafts remain forbidden.</summary>
    public static AuditEvent CreateLocalCredentialSelfService(LocalCredentialSelfServiceAuditData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!Enum.IsDefined(data.Operation) || data.ActorUserId == Guid.Empty || data.ActorUserId != data.TargetUserId
            || data.CredentialVersionBefore < 0 || data.CredentialVersionBefore == long.MaxValue
            || data.CredentialVersionAfter != data.CredentialVersionBefore + 1 || data.Method != "TOTP"
            || data.OccurredAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("A versioned, UTC, authenticated TOTP self-service operation is required.");
        var eventType = data.Operation switch
        {
            LocalCredentialSelfServiceOperation.EnableTwoFactor => TwoFactorEnabledEvent,
            LocalCredentialSelfServiceOperation.DisableTwoFactor => TwoFactorDisabledEvent,
            LocalCredentialSelfServiceOperation.RegenerateRecoveryCodes => RecoveryCodesRegeneratedEvent,
            _ => throw new ArgumentOutOfRangeException(nameof(data.Operation)),
        };
        var envelope = new AuditEventEnvelope(data.EventId, data.CompanyId, data.OccurredAtUtc, eventType,
            "Identity.LocalCredential", data.TargetUserId.ToString("D"), data.Operation.ToString(), data.CredentialVersionBefore,
            data.CredentialVersionAfter, "User", data.ActorUserId.ToString("D"), null, "[]",
            JsonSerializer.Serialize(new { credentialVersion = data.CredentialVersionBefore }),
            JsonSerializer.Serialize(new { credentialVersion = data.CredentialVersionAfter }), data.Reason,
            JsonSerializer.Serialize(new { kind = LocalCredentialSelfServiceKind, method = data.Method }),
            "None", null, "Web", null, null, data.CorrelationId, data.CausationId, data.IdempotencyKeyHash, "Succeeded", 1);
        return new(new AuditEventData(envelope, AuditEventIntegrity.Compute(envelope)), credentialSelfService: true);
    }
}
