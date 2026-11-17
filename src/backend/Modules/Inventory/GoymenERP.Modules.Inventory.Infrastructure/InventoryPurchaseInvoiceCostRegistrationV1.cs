using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>Owner services only. Work subscription, dispatch activation and the Accounting consumer are host-owned.</summary>
public static class InventoryPurchaseInvoiceCostRegistrationV1
{
    public static IServiceCollection AddInventoryPurchaseInvoiceCostsV1(this IServiceCollection services)
    {
        services.AddScoped<IInventoryPurchaseInvoiceCostStoreV1, InventoryPurchaseInvoiceCostStoreV1>();
        return services;
    }
}
