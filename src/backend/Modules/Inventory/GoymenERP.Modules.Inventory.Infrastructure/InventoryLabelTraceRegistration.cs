using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Inventory.Infrastructure;

public static class InventoryLabelTraceRegistration
{
    /// <summary>
    /// STK-S33 (KLN-D44): registers the owner read only; it adds no permission, grant or capability (the trace rides the label
    /// detail's). The production, party and supplier-search sources are the host's adapters (Inventory never references
    /// Manufacturing, Purchasing or Logistics); the defaults below answer "not answered" until they are replaced, so the trace
    /// still shows Inventory's own chain and says what it could not enrich.
    /// </summary>
    public static IServiceCollection AddInventoryLabelTrace(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IInventoryLabelTraceProductionSourceV1, InventoryNotAnsweredLabelTraceProductionSourceV1>();
        services.TryAddScoped<IInventoryLabelTracePartySourceV1, InventoryNotAnsweredLabelTracePartySourceV1>();
        services.TryAddScoped<IInventoryLabelTraceSupplierSearchSourceV1, InventoryNotAnsweredLabelTraceSupplierSearchSourceV1>();
        services.AddScoped<InventoryLabelTraceService>(CreateService);
        services.AddScoped<IInventoryLabelTraceV1>(provider => provider.GetRequiredService<InventoryLabelTraceService>());
        return services;
    }

    private static InventoryLabelTraceService CreateService(IServiceProvider services) =>
        new InventoryLabelTraceService(
            services.GetRequiredService<ICompanyContext>(),
            services.GetRequiredService<ICurrentUser>(),
            services.GetRequiredService<IErpAuthorityRevalidationService>(),
            services.GetService<IInventoryLabelGenealogyReaderV1>(),
            services.GetService<InventoryDbContext>() is { } db ? new SqlInventoryLabelTraceReader(db) : null,
            services.GetService<IInventoryLabelTraceProductionSourceV1>(),
            services.GetService<IInventoryLabelTracePartySourceV1>(),
            services.GetService<IInventoryStockOwnerDisplaySourceV1>(),
            services.GetService<IInventoryLabelTraceSupplierSearchSourceV1>());
}
