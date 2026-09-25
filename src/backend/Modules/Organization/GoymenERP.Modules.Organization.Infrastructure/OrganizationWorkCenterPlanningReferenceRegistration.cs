using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Organization.Infrastructure;

/// <summary>
/// Registers only Organization's internal work-center planning read. The API host owns the single
/// composition-root call; no route, permission, grant or public contract is introduced here.
/// </summary>
public static class OrganizationWorkCenterPlanningReferenceRegistration
{
    public static IServiceCollection AddOrganizationWorkCenterPlanningReferences(this IServiceCollection services)
    {
        services.AddScoped<IOrganizationWorkCenterPlanningReferenceV1,
            OrganizationWorkCenterPlanningReferenceReader>();
        return services;
    }
}
