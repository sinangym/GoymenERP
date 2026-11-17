using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ManufacturingCostGenerationHostContractTests
{
    [Theory]
    [InlineData(409, "inventory_manufacturing_cost_transfer_reversal_required", ManufacturingApiStatus.Conflict, "manufacturing_cost_transfer_reversal_required")]
    [InlineData(409, "inventory_manufacturing_cost_version_conflict", ManufacturingApiStatus.Conflict, "manufacturing_concurrent_change")]
    [InlineData(503, "inventory_manufacturing_cost_transfer_source_unreadable", ManufacturingApiStatus.Unavailable, "cost_source_unavailable")]
    public void A_real_transfer_undo_requirement_keeps_its_actionable_code_without_turning_unreadable_evidence_into_permission(
        int status, string ownerCode, ManufacturingApiStatus expectedStatus, string expectedCode)
    {
        var mapped = ManufacturingCostGenerationHost.Map(new(status, ownerCode));
        Assert.Equal(expectedStatus, mapped.Status); Assert.Equal(expectedCode, mapped.Code);
        Assert.False(ManufacturingCostGenerationHost.MayDeferInitialRecognition(new(status, ownerCode)));
    }

    [Fact]
    public void Nonfinite_owner_quantities_keep_exact_mass_proof_and_reject_rounded_or_contradictory_scalars()
    {
        var company = Guid.NewGuid(); var order = Guid.NewGuid(); var generation = Guid.NewGuid();
        var output = new InventoryManufacturingCostGenerationOutputResultV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            3m, null, null, null, null, null, null, [], null, null, new("4", "3"), new("5", "3"));
        var result = new InventoryManufacturingCostGenerationResultV1(company, order, generation, "1", "4",
            InventoryManufacturingCostGenerationStateV1.Pending, new string('a', 64), [output], null, false, "synthetic");
        ManufacturingCostGenerationHost.RequireResult(result, company, order, generation, 4, ManufacturingCostGenerationStateV1.Pending,
            result.InputFingerprint, [output]);
        foreach (var invalid in new[] {
            output with { RemainingQuantityKg = 1.3333m },
            output with { RemainingQuantityKgRatio = null },
            output with { ConsumedQuantityKgRatio = new("4", "3") },
            output with { RemainingQuantityKgRatio = new("8", "6") },
        }) Assert.Throws<ManufacturingInventoryRealizationPortException>(() => ManufacturingCostGenerationHost.RequireResult(
            result with { Outputs = [invalid] }, company, order, generation, 4, ManufacturingCostGenerationStateV1.Pending, result.InputFingerprint, [output]));
    }
    [Theory]
    [InlineData(503, "inventory_manufacturing_cost_consumption_source_unreadable", true)]
    [InlineData(409, "inventory_manufacturing_cost_consumption_source_unreadable", false)]
    [InlineData(503, "inventory_manufacturing_cost_generation_unavailable", false)]
    [InlineData(503, "inventory_manufacturing_cost_audit_failed", false)]
    public void Only_the_owners_exact_prewrite_consumption_refusal_can_defer_the_initial_money_recognition(int status, string code, bool expected)
    {
        Assert.Equal(expected, ManufacturingCostGenerationHost.MayDeferInitialRecognition(
            new InventoryManufacturingCostGenerationException(status, code)));
    }

    [Fact]
    public void Pending_cost_never_accepts_fictional_zero_money_and_owner_results_must_preserve_the_exact_physical_set()
    {
        var company = Guid.NewGuid(); var order = Guid.NewGuid(); var generation = Guid.NewGuid();
        var output = new InventoryManufacturingCostGenerationOutputResultV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            100m, 70m, 30m, null, null, null, null, []);
        var result = new InventoryManufacturingCostGenerationResultV1(company, order, generation, "1", "4",
            InventoryManufacturingCostGenerationStateV1.Pending, new string('a', 64), [output], null, false, "synthetic-correlation");
        ManufacturingCostGenerationHost.RequireResult(result, company, order, generation, 4, ManufacturingCostGenerationStateV1.Pending,
            result.InputFingerprint, [output]);
        foreach (var invalid in new[]
        {
            result with { CompanyId = Guid.NewGuid() },
            result with { GenerationVersion = "" },
            result with { Outputs = [output with { ReceiptMovementId = Guid.NewGuid() }] },
            result with { Outputs = [output with { RemainingQuantityKg = 69m }] },
            result with { Outputs = [output with { RemainingTlValue = 0m }] },
            result with { Outputs = [output with { OriginalTlAmount = 0m }] },
            result with { CostDifferenceEventId = Guid.NewGuid() },
            result with { Outputs = [output, output] },
        })
            Assert.Throws<ManufacturingInventoryRealizationPortException>(() => ManufacturingCostGenerationHost.RequireResult(invalid,
                company, order, generation, 4, ManufacturingCostGenerationStateV1.Pending, result.InputFingerprint, [output]));
    }
}
