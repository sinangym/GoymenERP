using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.Modules.Manufacturing.Application;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// TAKIP-U3a (#1004): the Manufacturing command service refuses plan approval, approved-plan cancellation and order
/// closure (503) without the allocation port, so the normal host must compose that port with its Inventory adapter.
/// </summary>
public sealed class ManufacturingPlanAllocationHostCompositionTests
{
    [Fact]
    public async Task The_normal_host_composes_the_plan_allocation_port_with_the_inventory_adapter()
    {
        await using var factory = new GoymenErpApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();

        Assert.IsType<ManufacturingPlanAllocationHost>(
            scope.ServiceProvider.GetRequiredService<IManufacturingPlanAllocationPortV1>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ManufacturingCommandService>());
    }
}
