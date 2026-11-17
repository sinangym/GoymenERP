using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Inventory.Infrastructure;

public static class InventorySubcontractAllocationReleaseRegistration
{
    public static IServiceCollection AddInventorySubcontractAllocationRelease(this IServiceCollection services)
    {
        services.AddScoped<IInventorySubcontractAllocationReleaseStoreV1, InventorySubcontractAllocationReleaseStoreV1>();
        services.AddScoped<IInventorySubcontractAllocationReleaseServiceV1, InventorySubcontractAllocationReleaseServiceV1>();
        return services;
    }
}
