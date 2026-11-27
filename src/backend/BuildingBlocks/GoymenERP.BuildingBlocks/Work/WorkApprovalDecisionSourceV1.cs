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
    [property: JsonIgnore] WorkInventoryTransferLossProofV1? InventoryTransferLossProof = null,
    [property: JsonIgnore] WorkCounterpartyTaxIdentityChangePreviewV1? CounterpartyTaxIdentityChange = null,
    [property: JsonIgnore] WorkCounterpartyLegalNameChangePreviewV1? CounterpartyLegalNameChange = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkLogisticsPlanExcessOptionsV1? LogisticsPlanExcess = null,
    [property: JsonIgnore] WorkLogisticsPlanExcessProofV1? LogisticsPlanExcessProof = null,
    [property: JsonIgnore] WorkInventoryTransferOverageProofV1? InventoryTransferOverageProof = null);

/// <summary>Approval-purpose identities from the current plan revision, never an implicit user selection.</summary>
public sealed record WorkManufacturingPlanReapprovalOptionsV1(IReadOnlyList<Guid> ParentRollIds);

/// <summary>The current native request, not a transfer version or a decision timestamp.</summary>
public sealed record WorkInventoryTransferLossOptionsV1(int RequestNumber, DateTimeOffset RequestedAtUtc);
/// <summary>Owner-only routing/history proof. Never serialized into the purpose preview.</summary>
public sealed record WorkInventoryTransferLossProofV1(Guid TransferId, Guid LineId,
    IReadOnlyList<WorkInventoryTransferLossRequestProofV1> Requests);
public sealed record WorkInventoryTransferLossRequestProofV1(int RequestNumber, DateTimeOffset RequestedAtUtc,
    string PreparedBy, string Status);

/// <summary>Exact immutable proposal for the authorized human decision GET only. Never Inbox/AI metadata.</summary>
public sealed record WorkCounterpartyTaxIdentityChangePreviewV1(Guid PartyId, Guid ChangeRequestId,
    string CurrentTaxIdentityNumber, string ProposedTaxIdentityNumber, string ProposedKind, string Reason);
/// <summary>Native full names (up to 250), separate from the generic bounded Work caption/reference.</summary>
public sealed record WorkCounterpartyLegalNameChangePreviewV1(Guid PartyId, Guid ChangeRequestId,
    string CurrentLegalName, string ProposedLegalName, string Reason);

/// <summary>Current native instruction version for the human's frozen approval request; not a POST default.</summary>
public sealed record WorkLogisticsPlanExcessOptionsV1(string InstructionVersion);
/// <summary>Exact immutable proposal for the authorized human decision GET only. Never Inbox/AI metadata.</summary>
public sealed record WorkLogisticsPlanExcessPreviewV1(string InstructionVersion, Guid InstructionId, Guid LineId,
    string Quantity, string Reason, string ProposedBy, DateTimeOffset ProposedAtUtc);
/// <summary>Owner-only immutable proposal binding for validating the original native receipt.</summary>
public sealed record WorkLogisticsPlanExcessProofV1(Guid CompanyId, Guid ExceptionId, Guid InstructionId, Guid LineId,
    string Quantity, string Reason, string ProposedBy, DateTimeOffset ProposedAtUtc);

/// <summary>Native numbered excess request; exposed only by the authorized human purpose GET.</summary>
public sealed record WorkInventoryTransferOveragePreviewV1(int RequestNumber, DateTimeOffset RequestedAtUtc,
    Guid TransferId, Guid LineId, string Kilograms, int? PieceCount, string Reason, string RequestedBy, string ReceivedBy);
/// <summary>Retained owner history for exact native replay, never Inbox/AI metadata.</summary>
public sealed record WorkInventoryTransferOverageProofV1(Guid TransferId, Guid LineId, string ReceivedBy,
    IReadOnlyList<WorkInventoryTransferOverageRequestProofV1> Requests);
public sealed record WorkInventoryTransferOverageRequestProofV1(int RequestNumber, DateTimeOffset RequestedAtUtc,
    string PreparedBy, string Status, string Kilograms, int? PieceCount, string Reason);
