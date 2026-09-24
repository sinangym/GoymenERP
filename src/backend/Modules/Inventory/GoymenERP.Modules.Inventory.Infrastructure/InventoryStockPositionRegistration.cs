using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Inventory.Infrastructure;

public static class InventoryStockPositionRegistration
{
    /// <summary>Registers the owner read only; route/OpenAPI/shell publication stays central-owned.</summary>
    public static IServiceCollection AddInventoryStockPositions(this IServiceCollection services)
    {
        services.AddScoped<InventoryStockPositionService>(CreateService);
        services.AddScoped<IInventoryStockPositionsV1>(provider =>
            provider.GetRequiredService<InventoryStockPositionService>());
        return services;
    }

    private static InventoryStockPositionService CreateService(IServiceProvider services) =>
        new InventoryStockPositionService(
            services.GetRequiredService<ICompanyContext>(),
            services.GetRequiredService<ICurrentUser>(),
            services.GetRequiredService<IErpAuthorityRevalidationService>(),
            services.GetService<InventoryDbContext>() is { } db
                ? new SqlInventoryStockPositionReader(db)
                : null);
}
