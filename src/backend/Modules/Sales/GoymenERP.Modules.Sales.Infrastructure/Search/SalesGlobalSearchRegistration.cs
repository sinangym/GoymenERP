using GoymenERP.BuildingBlocks.Search;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Sales.Infrastructure.Search;

/// <summary>
/// AT-002: registers the Sales-owned global search source. Called by the host's global search composition
/// (<c>AddGlobalSearch</c>) after <c>AddSalesModule</c>, which supplies the DbContext and the permission bindings.
/// </summary>
public static class SalesGlobalSearchRegistration
{
    public static IServiceCollection AddSalesGlobalSearchSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IGlobalSearchSourceV1, SalesOrderGlobalSearchSource>();
        return services;
    }
}
