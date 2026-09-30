using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>
/// ST-02 scrap write-off owner registration seam. The central host calls this when it publishes the reviewed
/// authorization/HTTP surface (two permissions, three routes); this extension itself adds no route, permission or
/// grant. It needs <c>AddInventoryModule</c> (the store) and the platform's company context, current user and
/// authority revalidation, which the host already registers.
/// </summary>
public static class InventoryStockScrapRegistration
{
    public static IServiceCollection AddInventoryStockScraps(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IInventoryStockScrapWriterV1>(provider => provider.GetRequiredService<InventoryStore>());
        services.AddScoped<IInventoryStockScrapReaderV1>(provider => provider.GetRequiredService<InventoryStore>());
        services.AddScoped<IInventoryStockScrapCommandV1, InventoryStockScrapCommandService>();
        services.AddScoped<IInventoryStockScrapQueryV1, InventoryStockScrapQueryService>();
        return services;
    }
}
