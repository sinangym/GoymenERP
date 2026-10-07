using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Inventory.Infrastructure;

public static class InventoryStockPositionRegistration
{
    /// <summary>Registers the owner read only; route/OpenAPI/shell publication stays central-owned.</summary>
    public static IServiceCollection AddInventoryStockPositions(this IServiceCollection services)
    {
        services.AddScoped<InventoryStockPositionService>(CreateService);
        services.AddScoped<IInventoryStockPositionsV1>(provider =>
            provider.GetRequiredService<InventoryStockPositionService>());
        return services;
    }

    /// <summary>
    /// Host-owned cursor publication seam. The host supplies reviewed secret material; Inventory creates no
    /// key, grant or fallback. Without this registration, a continuation-required read fails closed.
    /// </summary>
    public static IServiceCollection AddInventoryStockPositionCursor(
        this IServiceCollection services,
        string? signingKeyBase64)
    {
        services.AddSingleton(new InventoryStockPositionCursorRuntimeOptions(signingKeyBase64));
        services.AddSingleton<IInventoryStockPositionCursorCodec, InventoryStockPositionCursorCodec>();
        return services;
    }

    private static InventoryStockPositionService CreateService(IServiceProvider services) =>
        new InventoryStockPositionService(
            services.GetRequiredService<ICompanyContext>(),
            services.GetRequiredService<ICurrentUser>(),
            services.GetRequiredService<IErpAuthorityRevalidationService>(),
            services.GetService<InventoryDbContext>() is { } db
                ? new SqlInventoryStockPositionReader(db)
                : null,
            services.GetService<IInventoryStockPositionCursorCodec>());
}
