namespace GoymenERP.Modules.Manufacturing.Domain.Corrections;

public enum ManufacturingCorrectionProofOwnerV1
{
    Availability = 1,
    Consumption = 2,
    Allocation = 3,
    Shipment = 4,
}

/// <summary>
/// One owner-produced quantity proof for the exact production output under review. The source
/// identity and version identify the immutable owner snapshot. Owner and current-fence fields bind
/// separate owner reads to the same evaluation; this value grants no authority and records no
/// counter movement, correction or rework.
/// </summary>
public sealed record ManufacturingCorrectionOwnerProofV1(
    ManufacturingCorrectionProofOwnerV1? Owner,
    Guid CompanyId,
    Guid ProductionOrderId,
    long ProductionOrderVersion,
    Guid OutputId,
    Guid? CurrentFenceId,
    long? CurrentFenceVersion,
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
    Guid? CurrentFenceId,
    long? CurrentFenceVersion,
    decimal? DeclaredOutputKg,
    decimal? RequestedCounterEffectKg,
    ManufacturingCorrectionOwnerProofV1? AvailabilityProof,
    ManufacturingCorrectionOwnerProofV1? ConsumptionProof,
    ManufacturingCorrectionOwnerProofV1? AllocationProof,
    ManufacturingCorrectionOwnerProofV1? ShipmentProof);
