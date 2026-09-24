using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>
/// Owner registration seam. The central host may call this only when it publishes the separately
/// reviewed authorization/HTTP surface; this extension itself adds no route, permission or grant.
/// </summary>
public static class InventoryLabelGenealogyRegistration
{
    public static IServiceCollection AddInventoryLabelGenealogy(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IInventoryLabelGenealogyWriterV1>(provider => provider.GetRequiredService<InventoryStore>());
        services.AddScoped<IInventoryLabelGenealogyReaderV1>(provider => provider.GetRequiredService<InventoryStore>());
        return services;
    }
}
