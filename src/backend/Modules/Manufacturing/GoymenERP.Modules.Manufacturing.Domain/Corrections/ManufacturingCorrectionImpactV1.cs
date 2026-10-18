namespace GoymenERP.Modules.Manufacturing.Domain.Corrections;

/// <summary>
/// One owner-produced quantity proof for the exact production output under review. The source
/// identity and version identify the immutable owner snapshot; this value grants no authority and
/// records no counter movement, correction or rework.
/// </summary>
public sealed record ManufacturingCorrectionOwnerProofV1(
    Guid CompanyId,
    Guid ProductionOrderId,
    long ProductionOrderVersion,
    Guid OutputId,
    Guid SourceSnapshotId,
    long? SourceSnapshotVersion,
    bool IsComplete,
    decimal? QuantityKg);

/// <summary>
/// The complete read-only impact envelope for one requested counter-effect. Quantities from
/// different downstream owners are deliberately kept separate: consumption, allocation and
/// shipment may describe related facts and are never summed as if they were disjoint.
/// </summary>
public sealed record ManufacturingCorrectionImpactV1(
    Guid CompanyId,
    Guid ProductionOrderId,
    long ProductionOrderVersion,
    Guid OutputId,
    decimal? DeclaredOutputKg,
    decimal? RequestedCounterEffectKg,
    ManufacturingCorrectionOwnerProofV1? AvailabilityProof,
    ManufacturingCorrectionOwnerProofV1? ConsumptionProof,
    ManufacturingCorrectionOwnerProofV1? AllocationProof,
    ManufacturingCorrectionOwnerProofV1? ShipmentProof);
