using System.Data;

namespace GoymenERP.Modules.Audit.Application.Contracts;

public enum FounderRecoverySystemAuditOperationV1 { Reactivate = 1, ResetPassword = 2 }
public sealed record FounderRecoveryOperatorDeclarationV1(string OperatingSystemUser, string MachineName);
public sealed record FounderRecoveryCompanyAuditV1(Guid CompanyId, Guid FounderUserId,
    FounderRecoverySystemAuditOperationV1 Operation, FounderRecoveryOperatorDeclarationV1 Operator,
    IReadOnlyList<Guid> PreconditionCompanyIds, IReadOnlyList<Guid> RestoredAssignmentIds,
    long UserVersionBefore, long UserVersionAfter, DateTimeOffset OccurredAtUtc,
    string CorrelationId, string? CausationId, string IdempotencyKey);
/// <summary>CLI-only typed founder recovery audit; writes each company through a fresh Audit owner joined to the command transaction.</summary>
public interface IFounderRecoverySystemAuditV1
{
    Task AppendAsync(IDbTransaction transaction, IReadOnlyList<FounderRecoveryCompanyAuditV1> records, CancellationToken cancellationToken = default);
}
