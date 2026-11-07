using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>B-01: the plan-input allocation contract (URT-D3, URT-D21, URT-D22).</summary>
public sealed class InventoryManufacturingPlanAllocationContractTests
{
    private static readonly Guid Company = Guid.Parse("92000000-0000-4000-8000-000000000101");
    private static readonly DateTimeOffset At = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Demand_kind_and_command_names_are_stable_and_distinct_from_Sales()
    {
        Assert.Equal("manufacturing.plan-input", InventoryAllocationDemandKindsV1.ManufacturingPlanInput);
        Assert.NotEqual(InventoryAllocationDemandKindsV1.SalesOrderLine,
            InventoryAllocationDemandKindsV1.ManufacturingPlanInput);
        Assert.NotEqual(InventoryManufacturingPlanAllocationContractV1.AllocateCommandName,
            InventoryManufacturingPlanAllocationContractV1.ReleaseCommandName);
    }

    [Fact]
    public void Exact_planned_kilograms_are_kept_line_by_line_without_rounding()
    {
        var quantities = InventoryManufacturingPlanAllocationContractV1.Validate(
            Request(Line(Id(1), "60.125"), Line(Id(2), "0.001")), Metadata(), Company);
        Assert.Equal([60.125m, 0.001m], quantities.Select(quantity => quantity.Value).ToArray());
    }

    [Theory]
    [InlineData("60.1250")]
    [InlineData("1.2345")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1,5")]
    public void A_quantity_that_is_not_exact_positive_kilograms_is_refused(string quantity)
    {
        var error = Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingPlanAllocationContractV1.Validate(Request(Line(Id(1), quantity)), Metadata(),
                Company));
        Assert.Equal((400, InventoryManufacturingRealizationCodesV1.InvalidQuantity), (error.Status, error.Code));
    }

    [Fact]
    public void A_label_named_twice_or_an_empty_plan_is_refused()
    {
        AssertInvalid(Request(Line(Id(1), "1"), Line(Id(1), "2")));
        AssertInvalid(Request());
        AssertInvalid(Request(Line(Id(1), "1")) with { Reason = " " });
    }

    [Fact]
    public void Customer_ownership_names_exactly_one_customer_and_company_ownership_names_none()
    {
        AssertInvalid(Request(Line(Id(1), "1") with
        {
            Ownership = InventoryManufacturingPlanInputOwnershipV1.CustomerOwned,
        }));
        AssertInvalid(Request(Line(Id(1), "1") with { CustomerPartyId = Id(9) }));
        AssertInvalid(Request(Line(Id(1), "1") with
        {
            Ownership = InventoryManufacturingPlanInputOwnershipV1.CustomerOwned,
            CustomerPartyId = Guid.Empty,
        }));
        _ = InventoryManufacturingPlanAllocationContractV1.Validate(Request(Line(Id(1), "1") with
        {
            Ownership = InventoryManufacturingPlanInputOwnershipV1.CustomerOwned,
            CustomerPartyId = Id(9),
        }), Metadata(), Company);
    }

    [Fact]
    public void Another_company_is_not_found_and_a_local_time_is_invalid()
    {
        var foreign = Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingPlanAllocationContractV1.Validate(Request(Line(Id(1), "1")) with { CompanyId = Id(50) },
                Metadata(), Company));
        Assert.Equal((404, InventoryManufacturingRealizationCodesV1.InvalidReference), (foreign.Status, foreign.Code));
        AssertInvalid(Request(Line(Id(1), "1")) with { OccurredAtUtc = new DateTimeOffset(2026, 11, 7, 12, 0, 0,
            TimeSpan.FromHours(3)) });
    }

    [Fact]
    public void Release_reasons_are_stored_as_codes()
    {
        Assert.Equal("manufacturing.plan-cancelled", InventoryManufacturingPlanAllocationContractV1.ReleaseReasonCode(
            InventoryManufacturingPlanReleaseReasonV1.PlanCancelled));
        Assert.Equal("manufacturing.order-closed", InventoryManufacturingPlanAllocationContractV1.ReleaseReasonCode(
            InventoryManufacturingPlanReleaseReasonV1.OrderClosed));
        var error = Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingPlanAllocationContractV1.Validate(new InventoryManufacturingPlanReleaseRequestV1(
                Company, Id(3), "4", (InventoryManufacturingPlanReleaseReasonV1)9, At), Metadata(), Company));
        Assert.Equal((400, InventoryManufacturingRealizationCodesV1.InvalidRequest), (error.Status, error.Code));
    }

    [Fact]
    public void New_allocation_refusals_are_defined_conflicts()
    {
        foreach (var code in new[]
                 {
                     InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock,
                     InventoryManufacturingRealizationCodesV1.AllocationOwnerMismatch,
                     InventoryManufacturingRealizationCodesV1.AllocationNotUsable,
                     InventoryManufacturingRealizationCodesV1.AllocationExceeded,
                 })
        {
            Assert.True(InventoryManufacturingRealizationCodesV1.IsDefined(409, code), code);
        }
    }

    private static void AssertInvalid(InventoryManufacturingPlanAllocationRequestV1 request)
    {
        var error = Assert.Throws<InventoryManufacturingRealizationException>(() =>
            InventoryManufacturingPlanAllocationContractV1.Validate(request, Metadata(), Company));
        Assert.Equal((400, InventoryManufacturingRealizationCodesV1.InvalidRequest), (error.Status, error.Code));
    }

    private static InventoryManufacturingPlanAllocationRequestV1 Request(
        params InventoryManufacturingPlanInputLineV1[] lines) => new(
        Company, Id(3), "4", lines, "Üretim planı onayı", At);

    private static InventoryManufacturingPlanInputLineV1 Line(Guid labelId, string quantity) => new(
        labelId, "2", quantity, InventoryManufacturingPlanInputOwnershipV1.Company, null);

    private static InventoryCommandMetadata Metadata() => new(
        "manufacturing-user", "manufacturing.plans.approve", ["MANUFACTURING-USER"],
        "corr-plan-1", "cause-plan-1", "plan-key-1", "api");

    private static Guid Id(int value) => Guid.Parse($"92000000-0000-4000-8000-{value:D12}");
}
