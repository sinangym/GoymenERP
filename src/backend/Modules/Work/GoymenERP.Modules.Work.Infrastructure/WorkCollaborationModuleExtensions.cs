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
        // YOL-D26: registered so the path exists; it refuses unless Work:NoteDisposal:Enabled is true AND the record owner
        // confirms the disposal. Nothing calls it today (no record disposal job exists).
        services.AddScoped<IWorkNoteDisposalV1, WorkNoteDisposalV1>();
        return services.AddWorkCollaborationScheduler(configuration);
    }
}
