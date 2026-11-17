using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.ManufacturingRuntime;

/// <summary>URT-S8/SC32/SC36: actual owner readers and a same-transaction Inventory cost participant.
/// No price, currency rate, policy or physical fact is synthesized by the host registration.</summary>
public static class ManufacturingCostRuntimeComposition
{
    public static IServiceCollection AddManufacturingCostRuntimeComposition(this IServiceCollection services)
    {
        services.AddScoped<IManufacturingInputRatePriorityReadPortV1, ManufacturingInputRatePriorityReaderV1>();
        services.AddScoped<IManufacturingCostMovementReadPortV1, ManufacturingInventoryCostMovementReader>();
        services.AddScoped<IManufacturingScrapCostPolicyReadPortV1, ManufacturingSettingsScrapCostPolicyReader>();
        services.AddScoped<IManufacturingScrapUsdRateReadPortV1, ManufacturingScrapUsdRateReader>();
        services.AddScoped<IManufacturingScrapCostDistributionSourceV1, ManufacturingCostCompositionSourceV1>();
        services.AddScoped<IManufacturingScrapCostDistributionPortV1, ManufacturingScrapCostDistributionPort>();
        services.AddScoped<IManufacturingProductionCostPortV2, ManufacturingProductionCostPort>();
        services.AddScoped<ManufacturingInventoryCostGenerationFactoryV1>(provider =>
            (connection, transaction, companyId, audit, writer, cancellationToken) =>
                InventoryManufacturingCostGenerationParticipant.EnlistAsync(connection, transaction, companyId,
                    audit, writer, cancellationToken, provider.GetRequiredService<TimeProvider>()));
        // The plan-allocation host receives this same scoped host for its physical-close callback.
        services.AddScoped<ManufacturingCostGenerationHost>();
        services.AddScoped<IManufacturingClosedOrderCostRecognitionPortV1>(provider =>
            provider.GetRequiredService<ManufacturingCostGenerationHost>());
        services.AddScoped<IManufacturingOrderReopeningInventoryPortV1>(provider =>
            provider.GetRequiredService<ManufacturingCostGenerationHost>());
        return services;
    }
}
