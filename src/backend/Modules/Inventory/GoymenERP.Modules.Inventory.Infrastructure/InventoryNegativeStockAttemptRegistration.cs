using GoymenERP.Modules.Inventory.Application;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Inventory.Infrastructure;

public static class InventoryNegativeStockAttemptRegistration
{
    public static IServiceCollection AddInventoryNegativeStockAttempts(this IServiceCollection services)
    {
        services.AddScoped<InventoryNegativeStockAttempts>();
        services.AddScoped<IInventoryNegativeStockAttemptsV1>(provider => provider.GetRequiredService<InventoryNegativeStockAttempts>());
        services.AddScoped<IInventoryNegativeStockAttemptRecorderV1>(provider => provider.GetRequiredService<InventoryNegativeStockAttempts>());
        return services;
    }
}
