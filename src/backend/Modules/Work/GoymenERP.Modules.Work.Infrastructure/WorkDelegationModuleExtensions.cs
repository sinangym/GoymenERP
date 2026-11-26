using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Work.Infrastructure;

public static class WorkDelegationModuleExtensions
{
    public static IServiceCollection AddWorkDelegationsV1(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WorkDelegationOptionsV1>().Bind(configuration.GetSection(WorkDelegationOptionsV1.SectionName));
        services.AddScoped<WorkDelegationServiceV1>();
        services.AddScoped<IWorkDelegationServiceV1>(provider => provider.GetRequiredService<WorkDelegationServiceV1>());
        services.AddScoped<IWorkDelegationReaderV1>(provider => provider.GetRequiredService<WorkDelegationServiceV1>());
        return services;
    }
}
