using GoymenERP.BuildingBlocks.Search;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Inventory.Infrastructure.Search;

/// <summary>Registers Inventory's owner-owned exact material-label global-search source.</summary>
public static class InventoryGlobalSearchRegistration
{
    public static IServiceCollection AddInventoryGlobalSearchSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IInventoryMaterialLabelSearchReaderV1, SqlInventoryMaterialLabelSearchReaderV1>();
        services.AddScoped<IGlobalSearchSourceV1, InventoryMaterialLabelGlobalSearchSource>();
        return services;
    }
}
