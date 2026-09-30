using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>
/// ST-05 / AT-037 owner registration seam. The central host calls this when it publishes the reviewed
/// authorization/HTTP surface (two permissions, three routes); this extension itself adds no route, permission or
/// grant. It needs <c>AddInventoryModule</c> (the store) and the platform's company context, current user and
/// authority revalidation, which the host already registers.
/// </summary>
public static class InventoryStockCountRegistration
{
    public static IServiceCollection AddInventoryStockCounts(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IInventoryStockCountWriterV1>(provider => provider.GetRequiredService<InventoryStore>());
        services.AddScoped<IInventoryStockCountReaderV1>(provider => provider.GetRequiredService<InventoryStore>());
        services.AddScoped<IInventoryStockCountCommandV1, InventoryStockCountCommandService>();
        services.AddScoped<IInventoryStockCountQueryV1, InventoryStockCountQueryService>();
        return services;
    }
}
