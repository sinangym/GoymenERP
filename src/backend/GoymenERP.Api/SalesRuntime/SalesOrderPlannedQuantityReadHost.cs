using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.SalesRuntime;

/// <summary>
/// SAT-D406 / SAT-D367: the host binds the Sales planned-quantity port (the risk remainder's plan) to the Logistics owner's read.
/// The Sales inbox consumer of the Logistics definite-shipment events is registered by
/// <see cref="SalesRiskExceptionUsageInboxHost"/> (SAT-S10), not here.
/// </summary>
internal static class SalesOrderPlannedQuantityReadHost
{
    public static IServiceCollection AddSalesOrderPlannedQuantityReadAdapter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ISalesOrderPlannedQuantityReadPortV1, SalesOrderPlannedQuantityReadAdapter>();
        return services;
    }
}
