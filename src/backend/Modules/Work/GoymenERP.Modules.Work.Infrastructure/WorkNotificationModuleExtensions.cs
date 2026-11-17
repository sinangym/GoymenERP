using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Work.Infrastructure;

public static class WorkNotificationModuleExtensions
{
    /// <summary>Storage/service registration only. Host composes owner read ports and inbox subscription; no dispatcher activation.</summary>
    public static IServiceCollection AddWorkNotificationStorageV1(this IServiceCollection services)
    {
        services.AddScoped<WorkNotificationStoreV1>();
        services.AddScoped<IWorkNotificationDeliveryV1>(provider => provider.GetRequiredService<WorkNotificationStoreV1>());
        services.AddScoped<IWorkNotificationMaintenanceV1>(provider => provider.GetRequiredService<WorkNotificationStoreV1>());
        services.AddScoped<IWorkNotificationReadV1, WorkNotificationReadV1>();
        return services;
    }
}
