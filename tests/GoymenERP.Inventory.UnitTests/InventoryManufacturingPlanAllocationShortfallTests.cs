using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Quantities;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryManufacturingPlanAllocationShortfallTests
{
    private static readonly Guid Company = Guid.Parse("93000000-0000-4000-8000-000000001101");
    private static readonly Guid Plan = Guid.Parse("93000000-0000-4000-8000-000000001102");
    private static readonly Guid Label = Guid.Parse("93000000-0000-4000-8000-000000001103");
    private static readonly Guid Product = Guid.Parse("93000000-0000-4000-8000-000000001104");

    [Fact]
    public void The_refusal_preserves_the_owner_measurement_as_canonical_kg_separate_from_physical_issue_shortfalls()
    {
        var detail = Shortfall(3m, 10m, 7.125m);
        var refused = InventoryManufacturingRealizationException.RefusedPlanAllocation(409,
            InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock, detail);
        Assert.Equal((Company, Plan, Label, Product, "7", 1),
            (detail.CompanyId, detail.PlanId, detail.LabelId, detail.ProductId, detail.LabelVersion, detail.FreePositionCount));
        Assert.Equal(("3", "10", "7.125", "2.875", "0.125"),
            (detail.RequestedKg, detail.FreePhysicalKg, detail.ReservedKg, detail.AvailableKg, detail.MissingKg));
        Assert.Same(detail, refused.PlanAllocationShortfall);
        Assert.Null(refused.StockShortfall);
    }

    [Fact]
    public void Missing_or_other_refusal_facts_are_not_filled_with_a_plan_allocation_measurement()
    {
        Assert.Null(InventoryManufacturingRealizationException.Refused(409,
            InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock).PlanAllocationShortfall);
        Assert.Null(InventoryManufacturingRealizationException.Refused(409,
            InventoryManufacturingRealizationCodesV1.LabelVersionConflict).PlanAllocationShortfall);
        var detail = Shortfall(1m, 0m, 0m);
        Assert.Throws<InvalidOperationException>(() => InventoryManufacturingRealizationException.RefusedPlanAllocation(503,
            InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock, detail));
        Assert.Throws<InvalidOperationException>(() => InventoryManufacturingRealizationException.RefusedPlanAllocation(409,
            InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient, detail));
        Assert.Throws<ArgumentNullException>(() => InventoryManufacturingRealizationException.RefusedPlanAllocation(409,
            InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock, null!));
    }

    [Fact]
    public void Existing_unreserved_rule_clamps_corrupt_overallocation_to_zero_instead_of_publishing_negative_available_kg()
    {
        var detail = Shortfall(1m, 3m, 5m);
        Assert.Equal(("0", "1"), (detail.AvailableKg, detail.MissingKg));
        Assert.Throws<ArgumentOutOfRangeException>(() => Shortfall(2m, 10m, 8m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Shortfall(0m, 10m, 11m));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("plan")]
    [InlineData("label")]
    [InlineData("product")]
    [InlineData("version")]
    public void Anonymous_or_unobserved_shortfalls_are_refused(string fault)
    {
        Assert.Throws<ArgumentException>(() => new InventoryManufacturingPlanAllocationShortfallV1(
            fault == "company" ? Guid.Empty : Company, fault == "plan" ? Guid.Empty : Plan,
            fault == "label" ? Guid.Empty : Label, fault == "product" ? Guid.Empty : Product,
            fault == "version" ? "07" : "7", Kilograms.Of(1m), Kilograms.Zero, Kilograms.Zero, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void An_unverified_or_ambiguous_free_position_is_not_reported_as_a_measured_single_position(int positions) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Shortfall(1m, 0m, 0m, positions));

    private static InventoryManufacturingPlanAllocationShortfallV1 Shortfall(decimal requested, decimal physical, decimal reserved,
        int positions = 1) => new(Company, Plan, Label, Product, "7", Kilograms.Of(requested), Kilograms.Of(physical),
            Kilograms.Of(reserved), positions);
}
