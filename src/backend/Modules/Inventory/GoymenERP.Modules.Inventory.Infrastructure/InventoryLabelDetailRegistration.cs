using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Inventory.Infrastructure;

public static class InventoryLabelDetailRegistration
{
    /// <summary>Registers the owner read; route/OpenAPI/shell publication remains central-owned.</summary>
    public static IServiceCollection AddInventoryLabelDetails(this IServiceCollection services)
    {
        services.AddScoped<InventoryLabelDetailService>(CreateService);
        services.AddScoped<IInventoryLabelDetailsV1>(provider =>
            provider.GetRequiredService<InventoryLabelDetailService>());
        return services;
    }

    private static InventoryLabelDetailService CreateService(IServiceProvider services) =>
        new InventoryLabelDetailService(
            services.GetRequiredService<ICompanyContext>(),
            services.GetRequiredService<ICurrentUser>(),
            services.GetRequiredService<IErpAuthorityRevalidationService>(),
            services.GetService<InventoryDbContext>() is { } db
                ? new SqlInventoryLabelDetailReader(db)
                : null);
}
