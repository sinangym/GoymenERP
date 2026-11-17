using System.Data;

namespace GoymenERP.Modules.Audit.Application.Contracts;

public enum LocalCredentialSelfServiceOperationV1 { EnableTwoFactor = 1, DisableTwoFactor = 2, RegenerateRecoveryCodes = 3 }

/// <summary>Authenticated self-service only. No password, protected token, secret, code or hash belongs in this payload.</summary>
public sealed record LocalCredentialSelfServiceAuditV1(Guid CompanyId, Guid ActorUserId, Guid TargetUserId,
    LocalCredentialSelfServiceOperationV1 Operation, long CredentialVersionBefore, long CredentialVersionAfter,
    string Method, DateTimeOffset OccurredAtUtc, string Reason, string CorrelationId, string? CausationId,
    string IdempotencyKey);

/// <summary>Audit owns its table. Identity validates fresh actor==target/member/session and lends its SERIALIZABLE transaction.</summary>
public interface ILocalCredentialSelfServiceAuditWriterV1
{
    Task AppendAsync(IDbTransaction transaction, LocalCredentialSelfServiceAuditV1 record, CancellationToken cancellationToken = default);
}
