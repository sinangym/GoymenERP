using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Persistence;

namespace GoymenERP.Modules.Work.Infrastructure;

// Internal owner-admission seam only. An authorized source supplies the observation and audit metadata;
// this type does not grant authority, produce ordinals or activate a public Work command.
public sealed class WorkDesiredStateAdmissionV1(
    WorkDesiredStateStoreV2 store,
    WorkDbContext dbContext,
    ICompanyContext companyContext)
{
    public async Task<WorkIssueEvaluationV1> AdmitAsync(
        WorkDesiredStateObservationV2 observation,
        WorkDesiredStateWriteMetadataV2 metadata,
        WorkEvaluationIdV1 evaluationId,
        WorkDeliveryIdV1 deliveryId,
        CancellationToken cancellationToken = default)
    {
        var persisted = await store.ReconcileAsync(observation, metadata, cancellationToken);
        return WorkIssueLifecycleV1.ProjectPersisted(
            persisted, observation, evaluationId, deliveryId);
    }

    public Task<WorkDesiredStateSnapshotV2?> ReadSnapshotAsync(
        string sourceKey,
        string scopeKey,
        CancellationToken cancellationToken = default) =>
        WorkDesiredStateStoreV2.ReadSnapshotAsync(
            dbContext, companyContext, sourceKey, scopeKey, cancellationToken);
}
