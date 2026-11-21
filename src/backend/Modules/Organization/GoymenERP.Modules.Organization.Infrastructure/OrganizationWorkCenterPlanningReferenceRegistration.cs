using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Organization.Infrastructure;

/// <summary>
/// Registers only Organization's internal work-center planning reads (V1 and its calendar-carrying V2). The API host
/// owns the single composition-root call; no route, permission, grant or public contract is introduced here.
/// </summary>
public static class OrganizationWorkCenterPlanningReferenceRegistration
{
    public static IServiceCollection AddOrganizationWorkCenterPlanningReferences(this IServiceCollection services)
    {
        services.AddScoped<IOrganizationWorkCenterPlanningReferenceV1,
            OrganizationWorkCenterPlanningReferenceReader>();
        // B-15: V2 adds the work centre's calendar; V1 stays registered unchanged for its existing consumers.
        services.AddScoped<IOrganizationWorkCenterPlanningReferenceV2,
            OrganizationWorkCenterPlanningReferenceV2Reader>();
        return services;
    }
}
