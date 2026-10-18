namespace GoymenERP.Modules.Manufacturing.Domain.Corrections;

public enum ManufacturingCorrectionOutcomeV1
{
    DirectCounterEffectAllowed = 1,
    LinkedCorrectionRequired = 2,
    Unavailable = 3,
    Conflict = 4,
}

public static class ManufacturingCorrectionCodesV1
{
    public const string ImpactUnavailable = "manufacturing_correction_impact_unavailable";
    public const string ImpactConflict = "manufacturing_correction_impact_conflict";
    public const string LinkedCorrectionRequired = "manufacturing_linked_correction_required";
}

/// <summary>
/// A read-only decision. <see cref="ManufacturingCorrectionOutcomeV1.DirectCounterEffectAllowed"/>
/// still requires an actual counter-effect to be linked to the original output; it only says that
/// the supplied proofs show no downstream fact requiring a separate correction or rework flow.
/// No result here writes Inventory, Manufacturing or Accounting state.
/// </summary>
public sealed record ManufacturingCorrectionDecisionV1(
    ManufacturingCorrectionOutcomeV1 Outcome,
    string? Code,
    Guid CompanyId,
    Guid ProductionOrderId,
    long ProductionOrderVersion,
    Guid OutputId,
    decimal? RequestedCounterEffectKg,
    decimal? AvailableCounterEffectKg,
    decimal? CorrectionOrReworkKg);

/// <summary>
/// Pure M07.1A policy over immutable owner proofs. It never creates a movement, chooses a
/// permission, edits the original fact or treats missing evidence as zero.
/// </summary>
public static class ManufacturingCorrectionPolicyV1
{
    public static ManufacturingCorrectionDecisionV1 Evaluate(ManufacturingCorrectionImpactV1? impact)
    {
        if (impact is null)
        {
            return Unavailable(null);
        }

        var proofs = new[]
        {
            impact.AvailabilityProof,
            impact.ConsumptionProof,
            impact.AllocationProof,
            impact.ShipmentProof,
        };

        // Conflict has first priority. A malformed supplied fact or an identity/version mismatch
        // cannot be hidden behind another owner's missing snapshot.
        if (InvalidImpact(impact)
            || proofs.Any(proof => InvalidProof(proof, impact)))
        {
            return Conflict(impact);
        }

        // Missing or incomplete owner proof is unknown, never zero downstream use.
        if (impact.DeclaredOutputKg is null
            || impact.RequestedCounterEffectKg is null
            || proofs.Any(UnavailableProof))
        {
            return Unavailable(impact);
        }

        var available = impact.AvailabilityProof!.QuantityKg!.Value;
        var requested = impact.RequestedCounterEffectKg.Value;
        var availableForRequest = Math.Min(available, requested);
        var correctionOrRework = requested - availableForRequest;
        var hasDownstreamUse = impact.ConsumptionProof!.QuantityKg!.Value > 0m
            || impact.AllocationProof!.QuantityKg!.Value > 0m
            || impact.ShipmentProof!.QuantityKg!.Value > 0m;

        if (hasDownstreamUse || available < impact.DeclaredOutputKg.Value)
        {
            return new(
                ManufacturingCorrectionOutcomeV1.LinkedCorrectionRequired,
                ManufacturingCorrectionCodesV1.LinkedCorrectionRequired,
                impact.CompanyId,
                impact.ProductionOrderId,
                impact.ProductionOrderVersion,
                impact.OutputId,
                requested,
                availableForRequest,
                correctionOrRework);
        }

        return new(
            ManufacturingCorrectionOutcomeV1.DirectCounterEffectAllowed,
            null,
            impact.CompanyId,
            impact.ProductionOrderId,
            impact.ProductionOrderVersion,
            impact.OutputId,
            requested,
            requested,
            0m);
    }

    private static bool InvalidImpact(ManufacturingCorrectionImpactV1 impact) =>
        impact.CompanyId == Guid.Empty
        || impact.ProductionOrderId == Guid.Empty
        || impact.ProductionOrderVersion <= 0
        || impact.OutputId == Guid.Empty
        || impact.DeclaredOutputKg is <= 0m
        || impact.RequestedCounterEffectKg is <= 0m
        || impact.DeclaredOutputKg is { } declared
            && impact.RequestedCounterEffectKg is { } requested
            && requested > declared;

    private static bool InvalidProof(
        ManufacturingCorrectionOwnerProofV1? proof,
        ManufacturingCorrectionImpactV1 impact)
    {
        if (proof is null)
        {
            return false;
        }

        return proof.CompanyId == Guid.Empty
            || proof.ProductionOrderId == Guid.Empty
            || proof.ProductionOrderVersion <= 0
            || proof.OutputId == Guid.Empty
            || proof.SourceSnapshotId == Guid.Empty
            || proof.SourceSnapshotVersion is <= 0
            || proof.QuantityKg is < 0m
            || proof.CompanyId != impact.CompanyId
            || proof.ProductionOrderId != impact.ProductionOrderId
            || proof.ProductionOrderVersion != impact.ProductionOrderVersion
            || proof.OutputId != impact.OutputId
            || proof.QuantityKg is { } quantity
                && impact.DeclaredOutputKg is { } declared
                && quantity > declared;
    }

    private static bool UnavailableProof(ManufacturingCorrectionOwnerProofV1? proof) =>
        proof is null
        || !proof.IsComplete
        || proof.SourceSnapshotVersion is null
        || proof.QuantityKg is null;

    private static ManufacturingCorrectionDecisionV1 Unavailable(ManufacturingCorrectionImpactV1? impact) =>
        Failed(ManufacturingCorrectionOutcomeV1.Unavailable, ManufacturingCorrectionCodesV1.ImpactUnavailable, impact);

    private static ManufacturingCorrectionDecisionV1 Conflict(ManufacturingCorrectionImpactV1 impact) =>
        Failed(ManufacturingCorrectionOutcomeV1.Conflict, ManufacturingCorrectionCodesV1.ImpactConflict, impact);

    private static ManufacturingCorrectionDecisionV1 Failed(
        ManufacturingCorrectionOutcomeV1 outcome,
        string code,
        ManufacturingCorrectionImpactV1? impact) =>
        new(
            outcome,
            code,
            impact?.CompanyId ?? Guid.Empty,
            impact?.ProductionOrderId ?? Guid.Empty,
            impact?.ProductionOrderVersion ?? 0,
            impact?.OutputId ?? Guid.Empty,
            impact?.RequestedCounterEffectKg,
            null,
            null);
}
