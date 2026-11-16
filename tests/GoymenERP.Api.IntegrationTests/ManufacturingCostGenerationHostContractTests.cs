using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ManufacturingCostGenerationHostContractTests
{
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
            result with { Outputs = [output, output] },
        })
            Assert.Throws<ManufacturingInventoryRealizationPortException>(() => ManufacturingCostGenerationHost.RequireResult(invalid,
                company, order, generation, 4, ManufacturingCostGenerationStateV1.Pending, result.InputFingerprint, [output]));
    }
}
