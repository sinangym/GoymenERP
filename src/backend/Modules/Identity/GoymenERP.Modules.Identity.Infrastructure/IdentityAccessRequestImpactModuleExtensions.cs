using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Identity.Infrastructure;

public static class IdentityAccessRequestImpactModuleExtensions
{
    /// <summary>Read-only composition beside the real Identity module. No grant, policy, AI tool, endpoint or job activation.</summary>
    public static IServiceCollection AddIdentityAccessRequestImpactV1(this IServiceCollection services)
    {
        services.TryAddScoped<IIdentityAccessRequestImpactReadV1, IdentityAccessRequestImpactReadV1>();
        return services;
    }
}
