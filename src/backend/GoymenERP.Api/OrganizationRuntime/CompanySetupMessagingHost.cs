using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Infrastructure;

namespace GoymenERP.Api.OrganizationRuntime;

internal static class CompanySetupMessagingHost
{
    /// <summary>Registration only; Work dispatcher activation remains OFF and human-owned.</summary>
    public static IServiceCollection AddCompanySetupMessaging(this IServiceCollection services)
    {
        services.AddWorkOutboxEventSource<OrganizationCompanySetupOutboxSource>(OrganizationCompanyOpenedV1.SourceName,
            new WorkOutboxEventTypeV1(OrganizationCompanyOpenedV1.EventType, 1));
        services.AddWorkOutboxEventSource<IdentityCompanySetupOutboxSource>(IdentityCompanyFirstAdminAssignedV1.SourceName,
            new WorkOutboxEventTypeV1(IdentityCompanyFirstAdminAssignedV1.EventType, 1));
        services.AddWorkInboxConsumer<IdentityCompanySetupService>(IdentityCompanySetupService.ConsumerName,
            new WorkInboxSubscriptionV1(OrganizationCompanyOpenedV1.SourceName, OrganizationCompanyOpenedV1.EventType, 1));
        services.AddWorkInboxConsumer<OrganizationCompanySetupInboxConsumer>(OrganizationCompanySetupInboxConsumer.ConsumerName,
            new WorkInboxSubscriptionV1(IdentityCompanyFirstAdminAssignedV1.SourceName, IdentityCompanyFirstAdminAssignedV1.EventType, 1));
        return services;
    }
}
