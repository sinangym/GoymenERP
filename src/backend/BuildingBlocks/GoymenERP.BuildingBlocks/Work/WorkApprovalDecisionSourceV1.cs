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
    WorkManufacturingPlanReapprovalOptionsV1? ManufacturingPlanReapproval = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WorkInventoryTransferLossOptionsV1? InventoryTransferLoss = null,
    [property: JsonIgnore] WorkInventoryTransferLossProofV1? InventoryTransferLossProof = null);

/// <summary>Approval-purpose identities from the current plan revision, never an implicit user selection.</summary>
public sealed record WorkManufacturingPlanReapprovalOptionsV1(IReadOnlyList<Guid> ParentRollIds);

/// <summary>The current native request, not a transfer version or a decision timestamp.</summary>
public sealed record WorkInventoryTransferLossOptionsV1(int RequestNumber, DateTimeOffset RequestedAtUtc);
/// <summary>Owner-only routing/history proof. Never serialized into the purpose preview.</summary>
public sealed record WorkInventoryTransferLossProofV1(Guid TransferId, Guid LineId,
    IReadOnlyList<WorkInventoryTransferLossRequestProofV1> Requests);
public sealed record WorkInventoryTransferLossRequestProofV1(int RequestNumber, DateTimeOffset RequestedAtUtc,
    string PreparedBy, string Status);
