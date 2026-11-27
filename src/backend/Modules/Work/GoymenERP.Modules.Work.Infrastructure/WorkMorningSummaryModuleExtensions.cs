using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Collaboration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Work.Infrastructure;

public static class WorkMorningSummaryModuleExtensions
{
    /// <summary>Read/generate composition only. Reuses the Work collaboration owner ports and schedule;
    /// does not register a job, hosted service, model, write command or external delivery.</summary>
    public static IServiceCollection AddWorkMorningSummaryV1(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IWorkMorningSummaryReadV1, WorkMorningSummaryReadV1>();
        return services;
    }
}
