using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ManufacturingCostRuntimeCompositionTests
{
    [Fact]
    public async Task The_normal_host_uses_actual_cost_readers_and_one_order_store_and_generation_host_per_scope()
    {
        await using var factory = new GoymenErpApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<ManufacturingStore>();
        Assert.Same(store, services.GetRequiredService<IManufacturingNonKgInputStoreV1>());
        Assert.Same(store, services.GetRequiredService<IManufacturingCostGenerationStoreV1>());
        Assert.Same(store, services.GetRequiredService<IManufacturingOrderReopeningStoreV1>());
        var host = services.GetRequiredService<ManufacturingCostGenerationHost>();
        Assert.Same(host, services.GetRequiredService<IManufacturingClosedOrderCostRecognitionPortV1>());
        Assert.Same(host, services.GetRequiredService<IManufacturingOrderReopeningInventoryPortV1>());
        Assert.IsType<ManufacturingInventoryCostMovementReader>(services.GetRequiredService<IManufacturingCostMovementReadPortV1>());
        Assert.IsType<ManufacturingInputRatePriorityReaderV1>(services.GetRequiredService<IManufacturingInputRatePriorityReadPortV1>());
        Assert.IsType<ManufacturingSettingsScrapCostPolicyReader>(services.GetRequiredService<IManufacturingScrapCostPolicyReadPortV1>());
        Assert.IsType<ManufacturingScrapUsdRateReader>(services.GetRequiredService<IManufacturingScrapUsdRateReadPortV1>());
        Assert.IsType<ManufacturingCostCompositionSourceV1>(services.GetRequiredService<IManufacturingScrapCostDistributionSourceV1>());
        Assert.IsType<ManufacturingScrapCostDistributionPort>(services.GetRequiredService<IManufacturingScrapCostDistributionPortV1>());
        Assert.IsType<ManufacturingProductionCostPort>(services.GetRequiredService<IManufacturingProductionCostPortV2>());
        Assert.IsType<ManufacturingReopeningAccountingPeriodStatusPortV1>(services.GetRequiredService<IManufacturingReopeningAccountingPeriodStatusPortV1>());
        Assert.NotNull(services.GetRequiredService<ManufacturingInventoryCostGenerationFactoryV1>());
        Assert.NotNull(services.GetRequiredService<ManufacturingCommandService>());
    }
}
