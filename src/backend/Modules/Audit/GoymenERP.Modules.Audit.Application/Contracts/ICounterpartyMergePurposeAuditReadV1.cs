using System.Data.Common;

namespace GoymenERP.Modules.Audit.Application.Contracts;

/// <summary>Exact private KIM-D59 artifact read; no general Audit filter or read permission is exposed.</summary>
public interface ICounterpartyMergePurposeAuditReadV1
{
    Task<CounterpartyMergePurposeAuditArtifactV1?> ReadAsync(CounterpartyMergePurposeAuditSelectorV1 selector,
        Func<CancellationToken, Task> consumerFence, DbTransaction transaction, CancellationToken cancellationToken = default);
}
public sealed record CounterpartyMergePurposeAuditSelectorV1(Guid CompanyId, Guid MergedPartyId,
    string CurrentActorId, string OriginalActorId, string OriginalCorrelationId, string IdempotencyKeyHash);
public sealed record CounterpartyMergePurposeAuditArtifactV1(Guid EventId, string AfterJson, long? VersionBefore,
    long? VersionAfter, string IntegrityHash);
