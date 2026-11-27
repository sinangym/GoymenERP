using System.Text.Json.Serialization;

namespace GoymenERP.BuildingBlocks.Work;

/// <summary>WP8 purpose read over the owner's own record. No price, cost or permission override.
/// Unlike the pending queue, the immutable maker metadata remains readable after a decision so an
/// authorized retry can reach the owner's idempotency receipt. Version belongs to the owner:
/// mutable records use their real version; an append-only submission uses its immutable identity.</summary>
public interface IWorkApprovalDecisionSourceV1 : IWorkInboxSourceV1
{
    Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId,
        CancellationToken cancellationToken);
}

public sealed record WorkApprovalDecisionSnapshotV1(Guid CompanyId, WorkInboxItemV1 Item,
    string SourceVersion, bool Pending,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkManufacturingPlanReapprovalOptionsV1? ManufacturingPlanReapproval = null);

/// <summary>Approval-purpose identities from the current plan revision, never an implicit user selection.</summary>
public sealed record WorkManufacturingPlanReapprovalOptionsV1(IReadOnlyList<Guid> ParentRollIds);
