using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// TAKIP-U3a (#1004): an <see cref="ArgumentException"/> raised by an Inventory participant call is the allocation
/// host's contract fault (503 code), while any other fault passes through untouched.
/// </summary>
public sealed class ManufacturingPlanAllocationHostFaultMappingTests
{
    [Fact]
    public async Task An_Inventory_argument_fault_becomes_the_host_incomplete_fault()
    {
        var error = await Assert.ThrowsAsync<ManufacturingPlanAllocationHostException>(() =>
            ManufacturingPlanAllocationHost.InventoryFaultAsync<int>(() =>
                throw new ArgumentException("Inventory argument fault.")));

        Assert.Equal(ManufacturingPlanAllocationCodesV1.Incomplete, error.Code);
    }

    [Fact]
    public async Task A_derived_argument_fault_is_mapped_and_other_faults_and_results_pass_through()
    {
        var mapped = await Assert.ThrowsAsync<ManufacturingPlanAllocationHostException>(() =>
            ManufacturingPlanAllocationHost.InventoryFaultAsync<int>(() =>
                throw new ArgumentNullException("value")));
        Assert.Equal(ManufacturingPlanAllocationCodesV1.Incomplete, mapped.Code);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ManufacturingPlanAllocationHost.InventoryFaultAsync<int>(() =>
                throw new InvalidOperationException("Not an argument fault.")));

        Assert.Equal(7, await ManufacturingPlanAllocationHost.InventoryFaultAsync(() => Task.FromResult(7)));
    }
}
