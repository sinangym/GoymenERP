using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.BuildingBlocks.Work;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>
/// SAT-D304 / SAT-D372 opening-stock cost owner registration seam. The central host calls this when it publishes the
/// reviewed authorization/HTTP surface (two permissions, two routes); this extension itself adds no route, permission or
/// grant. It needs <c>AddInventoryModule</c> (the store) and the platform's company context, current user and authority
/// revalidation, which the host already registers.
/// </summary>
public static class InventoryOpeningCostRegistration
{
    public static IServiceCollection AddInventoryOpeningCosts(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IInventoryOpeningCostWriterV1>(provider => provider.GetRequiredService<InventoryStore>());
        services.AddScoped<IInventoryOpeningCostCommandV1, InventoryOpeningCostCommandService>();
        // WP5 (YOL-D41): the pending submissions in the Work inbox; the decision is given from the inbox (WP8).
        services.TryAddScoped<WorkInbox.InventoryWorkInboxFence>();
        services.AddScoped<IWorkInboxSourceV1, WorkInbox.InventoryOpeningCostWorkInboxSourceV1>();
        return services;
    }
}
