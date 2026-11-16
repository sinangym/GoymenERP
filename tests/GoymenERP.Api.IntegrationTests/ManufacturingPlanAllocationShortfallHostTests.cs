using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Quantities;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ManufacturingPlanAllocationShortfallHostTests
{
    [Fact]
    public void Owner_SQL_decimal_scale_is_normalized_before_the_authorized_public_snapshot_is_created()
    {
        var company = Guid.NewGuid();
        var plan = Guid.NewGuid();
        var owner = new InventoryManufacturingPlanAllocationShortfallV1(company, plan, Guid.NewGuid(), Guid.NewGuid(),
            "7", Kilograms.Positive(600.000m), Kilograms.Positive(1000.000m), Kilograms.Positive(600.000m), 1);
        var refusal = InventoryManufacturingRealizationException.RefusedPlanAllocation(409,
            InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock, owner);

        var actual = ManufacturingPlanAllocationHost.MapPlanAllocationShortfall(company, plan, refusal);

        Assert.NotNull(actual);
        Assert.Equal(owner.LabelId, actual.LabelId);
        Assert.Equal(owner.ProductId, actual.ProductId);
        Assert.Equal("600", actual.RequestedKg);
        Assert.Equal("400", actual.AvailableKg);
        Assert.Equal("200", actual.MissingKg);
        Assert.Equal("7", actual.LabelVersion);
    }

    [Fact]
    public void Foreign_or_unattributed_owner_refusals_do_not_release_plan_or_stock_detail()
    {
        var company = Guid.NewGuid();
        var plan = Guid.NewGuid();
        var owner = new InventoryManufacturingPlanAllocationShortfallV1(company, plan, Guid.NewGuid(), Guid.NewGuid(),
            "7", Kilograms.Positive(10.125m), Kilograms.Positive(20m), Kilograms.Positive(15m), 1);
        var refusal = InventoryManufacturingRealizationException.RefusedPlanAllocation(409,
            InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock, owner);

        Assert.Null(ManufacturingPlanAllocationHost.MapPlanAllocationShortfall(Guid.NewGuid(), plan, refusal));
        Assert.Null(ManufacturingPlanAllocationHost.MapPlanAllocationShortfall(company, Guid.NewGuid(), refusal));
        Assert.Null(ManufacturingPlanAllocationHost.MapPlanAllocationShortfall(company, plan,
            InventoryManufacturingRealizationException.Refused(409,
                InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock)));
    }
}
