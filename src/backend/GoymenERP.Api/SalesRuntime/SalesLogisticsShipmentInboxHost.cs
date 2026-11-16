using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Work.Infrastructure;

namespace GoymenERP.Api.SalesRuntime;

/// <summary>
/// LOJ-D19 (SAT-D285): the host hands the Sales risk-exception usage consumer to the Work dispatcher for the Logistics
/// definite-shipment events (the Sales module does not reference Work's infrastructure). The Logistics source "logistics" is
/// registered first (<c>AddLogisticsDefiniteShipmentOutbox</c>); the dispatcher validates the pair at start. Registering does not
/// enable the dispatcher; that stays a production activation step behind <c>Work:OutboxDispatcher:Enabled</c>.
/// </summary>
internal static class SalesLogisticsShipmentInboxHost
{
    public static IServiceCollection AddSalesLogisticsShipmentInboxConsumer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddWorkInboxConsumer<SalesLogisticsShipmentInboxConsumer>(
            SalesLogisticsShipmentInboxV1.ConsumerName,
            [.. SalesLogisticsShipmentInboxV1.Subscriptions]);
    }

    /// <summary>
    /// SAT-D406 / SAT-D367: binds the Sales planned-quantity port (the risk remainder's plan) to the Logistics owner's read.
    /// </summary>
    public static IServiceCollection AddSalesOrderPlannedQuantityReadAdapter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ISalesOrderPlannedQuantityReadPortV1, SalesOrderPlannedQuantityReadAdapter>();
        return services;
    }
}
