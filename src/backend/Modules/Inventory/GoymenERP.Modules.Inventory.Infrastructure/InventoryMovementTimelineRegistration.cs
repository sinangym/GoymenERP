using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Inventory.Infrastructure;

public static class InventoryMovementTimelineRegistration
{
    /// <summary>Registers the owner read only; route/OpenAPI/shell publication stays central-owned.</summary>
    public static IServiceCollection AddInventoryMovementTimeline(this IServiceCollection services)
    {
        services.AddScoped<InventoryMovementTimelineService>(CreateService);
        services.AddScoped<IInventoryMovementTimelineV1>(provider =>
            provider.GetRequiredService<InventoryMovementTimelineService>());
        return services;
    }

    /// <summary>
    /// Host-owned cursor seam. The host supplies reviewed secret material; Inventory creates no key or fallback.
    /// </summary>
    public static IServiceCollection AddInventoryMovementTimelineCursor(
        this IServiceCollection services,
        string? signingKeyBase64)
    {
        services.AddSingleton(new InventoryMovementTimelineCursorRuntimeOptions(signingKeyBase64));
        services.AddSingleton<IInventoryMovementTimelineCursorCodec, InventoryMovementTimelineCursorCodec>();
        return services;
    }

    private static InventoryMovementTimelineService CreateService(IServiceProvider services) =>
        new InventoryMovementTimelineService(
            services.GetRequiredService<ICompanyContext>(),
            services.GetRequiredService<ICurrentUser>(),
            services.GetRequiredService<IErpAuthorityRevalidationService>(),
            services.GetService<InventoryDbContext>() is { } db
                ? new SqlInventoryMovementTimelineReader(db)
                : null,
            services.GetService<IInventoryMovementTimelineCursorCodec>());
}
