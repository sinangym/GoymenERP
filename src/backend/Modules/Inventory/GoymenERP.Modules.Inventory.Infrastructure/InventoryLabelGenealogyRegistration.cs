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
        // R19: the fenced user command over the writer. Its only dependencies are the platform's company
        // context, current user and authority revalidation, which the host already registers.
        services.AddScoped<IInventoryLabelTransformationCommandV1, InventoryLabelTransformationCommandService>();
        return services;
    }
}
