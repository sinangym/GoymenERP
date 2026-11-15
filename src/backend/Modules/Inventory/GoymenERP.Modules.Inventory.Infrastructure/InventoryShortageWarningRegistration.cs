using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Inventory.Infrastructure;

public static class InventoryShortageWarningRegistration
{
    /// <summary>
    /// KLN-D42 (STK-S31): registers the owner read only; route/OpenAPI/shell publication stays central-owned. The purchase source
    /// is the host's adapter (Inventory never references Purchasing) and the similar-product source is the similar-product owner's;
    /// the defaults below are fail-closed (purchases unavailable → 503) and "not configured" until they are replaced.
    /// </summary>
    public static IServiceCollection AddInventoryShortageWarnings(this IServiceCollection services)
    {
        services.TryAddScoped<IInventoryShortageDemandSourceV1, InventoryShortageDemandSource>();
        services.TryAddScoped<IInventoryShortageSupplySourceV1, InventoryUnavailableShortageSupplySourceV1>();
        services.TryAddScoped<IInventoryShortageSimilarStockSourceV1, InventoryNotConfiguredShortageSimilarStockSourceV1>();
        services.AddScoped<InventoryShortageWarningService>(CreateService);
        services.AddScoped<IInventoryShortageWarningsV1>(provider =>
            provider.GetRequiredService<InventoryShortageWarningService>());
        return services;
    }

    private static InventoryShortageWarningService CreateService(IServiceProvider services) =>
        new InventoryShortageWarningService(
            services.GetRequiredService<ICompanyContext>(),
            services.GetRequiredService<ICurrentUser>(),
            services.GetRequiredService<IErpAuthorityRevalidationService>(),
            services.GetService<IInventoryShortageDemandSourceV1>(),
            services.GetService<IInventoryShortageSupplySourceV1>(),
            services.GetService<IInventoryShortageSimilarStockSourceV1>(),
            services.GetService<ISettingsUnitIdentityCatalogV1>(),
            services.GetService<InventoryDbContext>() is { } db
                ? new SqlInventoryShortageStockReader(db)
                : null);
}
