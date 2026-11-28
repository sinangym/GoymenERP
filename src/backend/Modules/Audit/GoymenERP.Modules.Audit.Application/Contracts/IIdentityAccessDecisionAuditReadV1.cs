using System.Data.Common;

namespace GoymenERP.Modules.Audit.Application.Contracts;

/// <summary>Private exact-artifact port for the Identity access decision owner; no general audit query surface.</summary>
public interface IIdentityAccessDecisionAuditReadV1
{
    Task<IdentityAccessDecisionAuditArtifactV1?> ReadAsync(IdentityAccessDecisionAuditSelectorV1 selector,
        Func<CancellationToken, Task> consumerFence, DbTransaction? transaction = null,
        CancellationToken cancellationToken = default);
}

public sealed record IdentityAccessDecisionAuditSelectorV1(Guid CompanyId, Guid RequestId, string CurrentActorId,
    string OriginalActorId, string OriginalCorrelationId, string IdempotencyKeyHash);
public sealed record IdentityAccessDecisionAuditArtifactV1(Guid EventId, string AfterJson,
    long? VersionBefore, long? VersionAfter, string IntegrityHash);
