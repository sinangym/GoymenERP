using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Work.Infrastructure;

public static class WorkApprovalInboxModuleExtensions
{
    /// <summary>W1-A: Work's inbox measurement store. Owner read ports are registered by their modules.</summary>
    public static IServiceCollection AddWorkApprovalInboxMeasurementsV1(this IServiceCollection services)
    {
        services.AddScoped<IWorkApprovalInboxMeasurementsV1, WorkApprovalInboxMeasurementStoreV1>();
        return services;
    }
}
