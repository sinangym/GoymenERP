using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Work.Infrastructure;

namespace GoymenERP.Api.SalesRuntime;

/// <summary>
/// SAT-S10 (LOJ-D19, SAT-D285; plan L-5): the host hands the Sales usage mirror to the Work dispatcher as the inbox consumer
/// <c>sales.risk-exception-usage</c> of the Logistics source <c>logistics</c> (the Sales module does not reference Work's
/// infrastructure). Call after the Logistics source is registered: the registry checks at host start that every subscription
/// names a registered source and an event it declares. Registering the consumer does not enable the dispatcher; that stays a
/// production activation step behind <c>Work:OutboxDispatcher:Enabled</c> (a human gate).
/// </summary>
internal static class SalesRiskExceptionUsageInboxHost
{
    public static IServiceCollection AddSalesRiskExceptionUsageInbox(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddWorkInboxConsumer<SalesLogisticsShipmentInboxConsumer>(
            SalesLogisticsShipmentInboxV1.ConsumerName,
            [.. SalesLogisticsShipmentInboxV1.Subscriptions]);
    }
}
