using GoymenERP.BuildingBlocks.OpenApi;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Collaboration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Work.Infrastructure;

public static class WorkCollaborationModuleExtensions
{
    /// <summary>
    /// W1-C notes, mentions, tasks and the collaboration inbox. The host composes <see cref="IWorkRecordAccessPortV1"/>,
    /// <see cref="IWorkPersonDirectoryV1"/> and <see cref="IWorkDigestScheduleReaderV1"/> over the owners. The scheduler (overdue
    /// notifications and digest release) is registered ONLY when <c>Work:CollaborationScheduler:Enabled</c> is true; it is off by
    /// default and switching it on is a human gate.
    /// </summary>
    public static IServiceCollection AddWorkCollaborationV1(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IWorkCollaborationServiceV1, WorkCollaborationServiceV1>();
        services.AddScoped<IWorkCollaborationMaintenanceV1, WorkCollaborationMaintenanceV1>();
        var section = configuration.GetSection(WorkCollaborationSchedulerOptions.SectionName);
        services.Configure<WorkCollaborationSchedulerOptions>(section);
        var options = new WorkCollaborationSchedulerOptions();
        section.Bind(options);
        if (!options.Enabled) return services;
        _ = options.ResolvePollInterval();
        services.TryAddSingleton<WorkCollaborationScheduler>();
        if (!OpenApiDocumentGenerationMode.IsActive) services.AddHostedService<WorkCollaborationSchedulerHostedService>();
        return services;
    }
}
