using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed partial class ManufacturingCommandServiceTests
{
    [Fact]
    public async Task Input_availability_keeps_physical_allocated_and_free_kg_distinct_and_computes_missing_kg_on_the_server()
    {
        var fixture = new Fixture();
        var plan = AvailabilityPlan(fixture);
        var source = new InputAvailabilitySource();
        var result = await AvailabilityService(fixture, source).GetPlanInputAvailabilityAsync(plan.Id, CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        var line = Assert.Single(result.Resource!.Inputs);
        Assert.Equal(("600", "1000", "100", "550", "350", "250", "8"),
            (line.RequestedKg, line.PhysicalKg, line.BlockedKg, line.AllocatedKg, line.FreeKg, line.MissingKg, line.LabelVersion));
        Assert.Equal(plan.Version, result.Resource.PlanVersion);
        Assert.Empty(fixture.Store.Commands);
        Assert.All(source.Asked, request => Assert.Equal((Company, Roll), request));
    }

    [Theory]
    [InlineData("different_version")]
    [InlineData("different_company")]
    [InlineData("unreadable")]
    [InlineData("invalid_quantity")]
    public async Task An_inconsistent_owner_snapshot_discloses_no_partial_input_quantities(string failure)
    {
        var fixture = new Fixture(); var plan = AvailabilityPlan(fixture);
        var source = new InputAvailabilitySource { Failure = failure };
        var result = await AvailabilityService(fixture, source).GetPlanInputAvailabilityAsync(plan.Id, CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Unavailable, result.Status);
        Assert.Null(result.Resource);
        Assert.Empty(fixture.Store.Commands);
    }

    [Fact]
    public async Task Revoking_plan_read_after_the_first_owner_read_discloses_nothing_and_skips_the_second_owner()
    {
        var fixture = new Fixture(); var plan = AvailabilityPlan(fixture);
        var source = new InputAvailabilitySource { AfterFreeRead = () => fixture.Authority.Scope = PermissionScope.Denied };
        var result = await AvailabilityService(fixture, source).GetPlanInputAvailabilityAsync(plan.Id, CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
        Assert.Single(source.Asked);
    }

    [Fact]
    public async Task An_unknown_plan_never_asks_Inventory_for_arbitrary_labels()
    {
        var fixture = new Fixture(); var source = new InputAvailabilitySource();
        var result = await AvailabilityService(fixture, source).GetPlanInputAvailabilityAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.NotFound, result.Status);
        Assert.Empty(source.Asked);
    }

    private static ProductionPlan AvailabilityPlan(Fixture fixture)
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(Roll, new Kilograms(600m))]);
        fixture.Store.Plans[plan.Id] = plan;
        return plan;
    }

    private static ManufacturingCommandService AvailabilityService(Fixture fixture, InputAvailabilitySource source) =>
        new(fixture.Store, fixture.Store, fixture.Store, new ManufacturingMassVerifier(fixture.Settings), fixture.Authority,
            fixture.CompanyContext, fixture.User, fixture.Correlation, new FixedTime(), freeQuantity: source,
            inputDisposition: source);

    private sealed class InputAvailabilitySource : IInventoryFreeQuantityReadPortV1, IInventoryLabelDispositionReadPortV1
    {
        public string? Failure { get; init; }
        public Action? AfterFreeRead { get; init; }
        public List<(Guid CompanyId, Guid LabelId)> Asked { get; } = [];
        Task<InventoryFreeQuantityReadV1> IInventoryFreeQuantityReadPortV1.GetAsync(Guid companyId, Guid labelId,
            CancellationToken cancellationToken)
        {
            Asked.Add((companyId, labelId)); AfterFreeRead?.Invoke();
            return Task.FromResult(new InventoryFreeQuantityReadV1(InventoryLabelDispositionStatusV1.Value,
                new(Company, Roll, Product, "8", Failure == "invalid_quantity" ? "-1" : "350", 1)));
        }
        Task<InventoryLabelDispositionReadV1> IInventoryLabelDispositionReadPortV1.GetAsync(Guid companyId, Guid labelId,
            CancellationToken cancellationToken)
        {
            Asked.Add((companyId, labelId));
            if (Failure == "unreadable") return Task.FromResult(InventoryLabelDispositionReadV1.Unreadable);
            return Task.FromResult(new InventoryLabelDispositionReadV1(InventoryLabelDispositionStatusV1.Value,
                new(Failure == "different_company" ? OtherCompany : Company, Roll, Product,
                    Failure == "different_version" ? "9" : "8", InventoryLabelStockStateV1.InStock, null,
                    "0", "0", "0", "0", "0", "0", "0", "0", "0", "1000", "0", "1000", "100", "550")));
        }
    }
}
