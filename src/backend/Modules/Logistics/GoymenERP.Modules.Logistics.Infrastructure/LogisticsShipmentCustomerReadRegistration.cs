using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Modules.Logistics.Infrastructure;

public static class LogisticsShipmentCustomerReadRegistration
{
    /// <summary>STK-S33: registers the shipment-customer read only; it adds no route, permission or grant.</summary>
    public static IServiceCollection AddLogisticsShipmentCustomerRead(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<ILogisticsShipmentCustomerReadPortV1, LogisticsShipmentCustomerReadPort>();
        return services;
    }
}
