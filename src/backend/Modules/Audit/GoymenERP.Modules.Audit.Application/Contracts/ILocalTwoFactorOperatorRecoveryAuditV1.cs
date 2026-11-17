using System.Data;

namespace GoymenERP.Modules.Audit.Application.Contracts;

public enum LocalTwoFactorOperatorRecoveryAuditOutcomeV1 { Recovered = 1, AlreadyDisabled = 2, ConcurrentChange = 3 }
public sealed record LocalTwoFactorRecoveryOperatorAuditV1(string OperatingSystemUser, string MachineName)
{ public override string ToString() => nameof(LocalTwoFactorRecoveryOperatorAuditV1); }
public sealed record LocalTwoFactorRecoveryVersionAuditV1(long UserVersion, long CredentialVersion, long SecretVersion, bool TwoFactorEnabled);
public sealed record LocalTwoFactorOperatorRecoveryAuditV1(Guid AttemptId, Guid TargetUserId, LocalTwoFactorRecoveryOperatorAuditV1 Operator,
    LocalTwoFactorRecoveryVersionAuditV1 Confirmed, LocalTwoFactorRecoveryVersionAuditV1 Before,
    LocalTwoFactorRecoveryVersionAuditV1 After, LocalTwoFactorOperatorRecoveryAuditOutcomeV1 Outcome, string Reason,
    DateTimeOffset OccurredAtUtc, string CorrelationId, string? CausationId, string IdempotencyKeyHash)
{ public override string ToString() => nameof(LocalTwoFactorOperatorRecoveryAuditV1); }

/// <summary>
/// Closed server-operator recovery of a global local credential, including a target with zero company memberships.
/// No company is invented; no normal audit draft, role, permission or company read authority is granted.
/// Audit alone appends its platform subject on the caller's open Serializable transaction and never commits it.
/// </summary>
public interface ILocalTwoFactorOperatorRecoveryAuditV1
{
    Task AppendAsync(IDbTransaction transaction, LocalTwoFactorOperatorRecoveryAuditV1 record, CancellationToken cancellationToken = default);
}
