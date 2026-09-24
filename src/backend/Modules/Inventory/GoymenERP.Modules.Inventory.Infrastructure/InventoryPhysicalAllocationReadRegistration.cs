using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Inventory.Infrastructure;

public static class InventoryPhysicalAllocationReadRegistration
{
    /// <summary>Registers the owner read only; route/OpenAPI/shell publication stays central-owned.</summary>
    public static IServiceCollection AddInventoryPhysicalAllocationReads(this IServiceCollection services)
    {
        services.AddScoped<InventoryPhysicalAllocationReadService>(CreateService);
        services.AddScoped<IInventoryPhysicalAllocationReadsV1>(provider =>
            provider.GetRequiredService<InventoryPhysicalAllocationReadService>());
        return services;
    }

    private static InventoryPhysicalAllocationReadService CreateService(IServiceProvider services) =>
        new InventoryPhysicalAllocationReadService(
            services.GetRequiredService<ICompanyContext>(),
            services.GetRequiredService<ICurrentUser>(),
            services.GetRequiredService<IErpAuthorityRevalidationService>(),
            services.GetService<InventoryDbContext>() is { } db
                ? new SqlInventoryPhysicalAllocationReader(db)
                : null);
}
