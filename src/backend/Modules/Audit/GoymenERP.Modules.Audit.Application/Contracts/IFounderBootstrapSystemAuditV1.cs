using System.Data;

namespace GoymenERP.Modules.Audit.Application.Contracts;

/// <summary>One company row of the founder bootstrap (the Admin assignment written for that company).</summary>
public sealed record FounderBootstrapCompanyAuditV1(Guid CompanyId, Guid FounderUserId, Guid AdminAssignmentId,
    FounderRecoveryOperatorDeclarationV1 Operator, IReadOnlyList<Guid> CompanyIds, DateTimeOffset OccurredAtUtc,
    string CorrelationId, string? CausationId, string IdempotencyKey);

/// <summary>
/// CLI-only typed founder bootstrap audit (spec ID-2, K3-B finding 3): writes one row per bootstrapped company through a
/// fresh Audit owner joined to the bootstrap command's Serializable transaction, so a refused audit rolls the founder
/// back.
/// </summary>
public interface IFounderBootstrapSystemAuditV1
{
    Task AppendAsync(IDbTransaction transaction, IReadOnlyList<FounderBootstrapCompanyAuditV1> records, CancellationToken cancellationToken = default);
}
