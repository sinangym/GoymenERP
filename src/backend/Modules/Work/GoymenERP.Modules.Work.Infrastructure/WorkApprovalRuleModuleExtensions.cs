using GoymenERP.Modules.Work.Infrastructure.ApprovalRules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Work.Infrastructure;

public static class WorkApprovalRuleModuleExtensions
{
    /// <summary>
    /// WP1: the versioned approval rule table (service and inbox reader) and the business time zone source (P7 / SAT-D10,
    /// Europe/Istanbul). A host may register its own <see cref="IWorkBusinessTimeZoneSourceV1"/> first (e.g. pinned tzdata).
    /// </summary>
    public static IServiceCollection AddWorkApprovalRulesV1(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IWorkBusinessTimeZoneSourceV1, SystemWorkBusinessTimeZoneSourceV1>();
        services.AddScoped<IWorkApprovalRuleServiceV1, WorkApprovalRuleServiceV1>();
        services.AddScoped<IWorkApprovalRuleReaderV1, WorkApprovalRuleReaderV1>();
        return services;
    }
}
