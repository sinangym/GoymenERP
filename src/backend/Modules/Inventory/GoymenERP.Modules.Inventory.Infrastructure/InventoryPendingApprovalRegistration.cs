using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>
/// D-05 Inventory owner read registration. This adds no route, permission, grant or decision command.
/// </summary>
public static class InventoryPendingApprovalRegistration
{
    public static IServiceCollection AddInventoryPendingApprovalReads(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IInventoryPendingApprovalReaderV1>(
            provider => provider.GetRequiredService<InventoryStore>());
        services.AddScoped<InventoryPendingApprovalReadServiceV1>();
        services.AddScoped<IInventoryStockCountPendingApprovalReadServiceV1>(
            provider => provider.GetRequiredService<InventoryPendingApprovalReadServiceV1>());
        services.AddScoped<IInventoryStockScrapPendingApprovalReadServiceV1>(
            provider => provider.GetRequiredService<InventoryPendingApprovalReadServiceV1>());
        return services;
    }
}
