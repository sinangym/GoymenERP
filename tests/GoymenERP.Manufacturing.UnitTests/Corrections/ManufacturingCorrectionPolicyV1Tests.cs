using GoymenERP.Modules.Manufacturing.Domain.Corrections;

namespace GoymenERP.Manufacturing.UnitTests.Corrections;

public sealed class ManufacturingCorrectionPolicyV1Tests
{
    private static readonly Guid Company = Guid.Parse("71000000-0000-4000-8000-000000000001");
    private static readonly Guid Order = Guid.Parse("72000000-0000-4000-8000-000000000001");
    private static readonly Guid Output = Guid.Parse("73000000-0000-4000-8000-000000000001");
    private static readonly Guid AvailabilitySource = Guid.Parse("74000000-0000-4000-8000-000000000001");
    private static readonly Guid ConsumptionSource = Guid.Parse("74000000-0000-4000-8000-000000000002");
    private static readonly Guid AllocationSource = Guid.Parse("74000000-0000-4000-8000-000000000003");
    private static readonly Guid ShipmentSource = Guid.Parse("74000000-0000-4000-8000-000000000004");

    [Fact]
    public void Fully_available_output_without_downstream_use_allows_direct_linked_counter_effect()
    {
        var result = ManufacturingCorrectionPolicyV1.Evaluate(Impact());

        Assert.Equal(ManufacturingCorrectionOutcomeV1.DirectCounterEffectAllowed, result.Outcome);
        Assert.Null(result.Code);
        Assert.Equal(1000m, result.RequestedCounterEffectKg!.Value);
        Assert.Equal(1000m, result.AvailableCounterEffectKg!.Value);
        Assert.Equal(0m, result.CorrectionOrReworkKg!.Value);
    }

    [Fact]
    public void Golden_1000kg_with_600kg_consumed_allows_only_the_available_400kg()
    {
        var impact = Impact(
            availableKg: 400m,
            consumptionKg: 600m);

        var result = ManufacturingCorrectionPolicyV1.Evaluate(impact);

        Assert.Equal(ManufacturingCorrectionOutcomeV1.LinkedCorrectionRequired, result.Outcome);
        Assert.Equal(ManufacturingCorrectionCodesV1.LinkedCorrectionRequired, result.Code);
        Assert.Equal(1000m, result.RequestedCounterEffectKg!.Value);
        Assert.Equal(400m, result.AvailableCounterEffectKg!.Value);
        Assert.Equal(600m, result.CorrectionOrReworkKg!.Value);
    }

    [Fact]
    public void Any_downstream_fact_requires_a_linked_flow_even_when_requested_mass_is_available()
    {
        var consumed = ManufacturingCorrectionPolicyV1.Evaluate(Impact(
            requestedKg: 400m,
            availableKg: 400m,
            consumptionKg: 600m));
        var allocated = ManufacturingCorrectionPolicyV1.Evaluate(Impact(
            allocationKg: 1m));
        var shipped = ManufacturingCorrectionPolicyV1.Evaluate(Impact(
            shipmentKg: 1m));

        Assert.Equal(ManufacturingCorrectionOutcomeV1.LinkedCorrectionRequired, consumed.Outcome);
        Assert.Equal(400m, consumed.AvailableCounterEffectKg!.Value);
        Assert.Equal(0m, consumed.CorrectionOrReworkKg!.Value);
        Assert.Equal(ManufacturingCorrectionOutcomeV1.LinkedCorrectionRequired, allocated.Outcome);
        Assert.Equal(ManufacturingCorrectionOutcomeV1.LinkedCorrectionRequired, shipped.Outcome);
    }

    [Fact]
    public void Partial_availability_without_a_downstream_default_still_requires_linked_correction()
    {
        var fullRequest = ManufacturingCorrectionPolicyV1.Evaluate(Impact(availableKg: 400m));
        var availableOnlyRequest = ManufacturingCorrectionPolicyV1.Evaluate(Impact(
            requestedKg: 400m,
            availableKg: 400m));

        Assert.Equal(ManufacturingCorrectionOutcomeV1.LinkedCorrectionRequired, fullRequest.Outcome);
        Assert.Equal(400m, fullRequest.AvailableCounterEffectKg!.Value);
        Assert.Equal(600m, fullRequest.CorrectionOrReworkKg!.Value);
        Assert.Equal(ManufacturingCorrectionOutcomeV1.LinkedCorrectionRequired, availableOnlyRequest.Outcome);
        Assert.Equal(400m, availableOnlyRequest.AvailableCounterEffectKg!.Value);
        Assert.Equal(0m, availableOnlyRequest.CorrectionOrReworkKg!.Value);
    }

    [Fact]
    public void Missing_incomplete_or_stale_owner_proof_is_unavailable_not_zero()
    {
        var missing = ManufacturingCorrectionPolicyV1.Evaluate(
            Impact() with { AvailabilityProof = null });
        var incomplete = ManufacturingCorrectionPolicyV1.Evaluate(
            Impact() with { AllocationProof = Proof(AllocationSource, 0m) with { IsComplete = false } });
        var stale = ManufacturingCorrectionPolicyV1.Evaluate(
            Impact() with { ShipmentProof = Proof(ShipmentSource, 0m) with { SourceSnapshotVersion = null } });
        var missingDeclared = ManufacturingCorrectionPolicyV1.Evaluate(
            Impact() with { DeclaredOutputKg = null });
        var missingRequested = ManufacturingCorrectionPolicyV1.Evaluate(
            Impact() with { RequestedCounterEffectKg = null });
        var missingImpact = ManufacturingCorrectionPolicyV1.Evaluate(null);

        Assert.Equal(ManufacturingCorrectionOutcomeV1.Unavailable, missing.Outcome);
        Assert.Equal(ManufacturingCorrectionCodesV1.ImpactUnavailable, missing.Code);
        Assert.Equal(ManufacturingCorrectionOutcomeV1.Unavailable, incomplete.Outcome);
        Assert.Equal(ManufacturingCorrectionOutcomeV1.Unavailable, stale.Outcome);
        Assert.Equal(ManufacturingCorrectionOutcomeV1.Unavailable, missingDeclared.Outcome);
        Assert.Equal(ManufacturingCorrectionOutcomeV1.Unavailable, missingRequested.Outcome);
        Assert.Equal(ManufacturingCorrectionOutcomeV1.Unavailable, missingImpact.Outcome);
    }

    [Fact]
    public void Company_order_version_output_or_source_mismatch_is_a_conflict()
    {
        var conflicts = new[]
        {
            ManufacturingCorrectionPolicyV1.Evaluate(Impact() with { CompanyId = Guid.Empty }),
            ManufacturingCorrectionPolicyV1.Evaluate(Impact() with
            {
                AvailabilityProof = Proof(AvailabilitySource, 1000m) with { CompanyId = Guid.NewGuid() },
            }),
            ManufacturingCorrectionPolicyV1.Evaluate(Impact() with
            {
                ConsumptionProof = Proof(ConsumptionSource, 0m) with { ProductionOrderId = Guid.NewGuid() },
            }),
            ManufacturingCorrectionPolicyV1.Evaluate(Impact() with
            {
                AllocationProof = Proof(AllocationSource, 0m) with { ProductionOrderVersion = 12 },
            }),
            ManufacturingCorrectionPolicyV1.Evaluate(Impact() with
            {
                ShipmentProof = Proof(ShipmentSource, 0m) with { OutputId = Guid.NewGuid() },
            }),
            ManufacturingCorrectionPolicyV1.Evaluate(Impact() with
            {
                AvailabilityProof = Proof(AvailabilitySource, 1000m) with { SourceSnapshotId = Guid.Empty },
            }),
            ManufacturingCorrectionPolicyV1.Evaluate(Impact() with
            {
                AvailabilityProof = Proof(AvailabilitySource, 1000m) with { SourceSnapshotVersion = 0 },
            }),
            ManufacturingCorrectionPolicyV1.Evaluate(Impact() with
            {
                AvailabilityProof = Proof(AvailabilitySource, -1m),
            }),
            ManufacturingCorrectionPolicyV1.Evaluate(Impact() with
            {
                AvailabilityProof = Proof(AvailabilitySource, 1001m),
            }),
            ManufacturingCorrectionPolicyV1.Evaluate(Impact(requestedKg: 1001m)),
        };

        Assert.All(conflicts, result =>
        {
            Assert.Equal(ManufacturingCorrectionOutcomeV1.Conflict, result.Outcome);
            Assert.Equal(ManufacturingCorrectionCodesV1.ImpactConflict, result.Code);
        });
    }

    [Fact]
    public void Precedence_is_conflict_then_unavailable_then_linked_then_direct()
    {
        var conflict = Impact(consumptionKg: 1m) with
        {
            AvailabilityProof = null,
            ShipmentProof = Proof(ShipmentSource, 0m) with { OutputId = Guid.NewGuid() },
        };
        var unavailable = Impact(consumptionKg: 1m) with { AllocationProof = null };
        var linked = Impact(consumptionKg: 1m);
        var direct = Impact();

        Assert.Equal(
            ManufacturingCorrectionOutcomeV1.Conflict,
            ManufacturingCorrectionPolicyV1.Evaluate(conflict).Outcome);
        Assert.Equal(
            ManufacturingCorrectionOutcomeV1.Unavailable,
            ManufacturingCorrectionPolicyV1.Evaluate(unavailable).Outcome);
        Assert.Equal(
            ManufacturingCorrectionOutcomeV1.LinkedCorrectionRequired,
            ManufacturingCorrectionPolicyV1.Evaluate(linked).Outcome);
        Assert.Equal(
            ManufacturingCorrectionOutcomeV1.DirectCounterEffectAllowed,
            ManufacturingCorrectionPolicyV1.Evaluate(direct).Outcome);
    }

    [Fact]
    public void Same_immutable_impact_produces_the_same_decision()
    {
        var impact = Impact(availableKg: 400m, consumptionKg: 600m);

        var first = ManufacturingCorrectionPolicyV1.Evaluate(impact);
        var second = ManufacturingCorrectionPolicyV1.Evaluate(impact);

        Assert.Equal(first, second);
        Assert.Equal(400m, impact.AvailabilityProof!.QuantityKg!.Value);
        Assert.Equal(600m, impact.ConsumptionProof!.QuantityKg!.Value);
    }

    private static ManufacturingCorrectionImpactV1 Impact(
        decimal declaredKg = 1000m,
        decimal requestedKg = 1000m,
        decimal availableKg = 1000m,
        decimal consumptionKg = 0m,
        decimal allocationKg = 0m,
        decimal shipmentKg = 0m) =>
        new(
            Company,
            Order,
            11,
            Output,
            declaredKg,
            requestedKg,
            Proof(AvailabilitySource, availableKg),
            Proof(ConsumptionSource, consumptionKg),
            Proof(AllocationSource, allocationKg),
            Proof(ShipmentSource, shipmentKg));

    private static ManufacturingCorrectionOwnerProofV1 Proof(Guid sourceSnapshotId, decimal quantityKg) =>
        new(
            Company,
            Order,
            11,
            Output,
            sourceSnapshotId,
            3,
            true,
            quantityKg);
}
