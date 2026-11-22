using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.AssistantMemory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Work.Infrastructure;

public static class WorkAssistantMemoryModuleExtensions
{
    /// <summary>
    /// WP-C6 personal assistant memory. Binds <c>Work:AssistantMemory</c>; every value is unset by default, which keeps writing
    /// closed (pilot off, retention and entry limit undecided, information notice not approved). Opening any of them is a human
    /// gate. The host already composes <see cref="IWorkPersonDirectoryV1"/> for W1-C.
    /// </summary>
    public static IServiceCollection AddWorkAssistantMemoryV1(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<WorkAssistantMemoryOptions>().Bind(configuration.GetSection(WorkAssistantMemoryOptions.SectionName));
        services.AddScoped<WorkAssistantMemoryServiceV1>();
        services.AddScoped<IWorkAssistantMemoryServiceV1>(provider => provider.GetRequiredService<WorkAssistantMemoryServiceV1>());
        services.AddScoped<IWorkAssistantMemoryReaderV1>(provider => provider.GetRequiredService<WorkAssistantMemoryServiceV1>());
        return services;
    }
}
