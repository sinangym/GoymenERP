using GoymenERP.Modules.Logistics.Application.Outbox;
using GoymenERP.Modules.Logistics.Infrastructure.Outbox;
using GoymenERP.Modules.Work.Infrastructure;

namespace GoymenERP.Api.LogisticsRuntime;

/// <summary>
/// STK-S27 (LOJ-D19): the host registers Logistics's outbox as the Work dispatcher source <c>logistics</c> (the Logistics
/// module does not reference Work's infrastructure). Registering the source does not enable the dispatcher; that stays a
/// production activation step behind <c>Work:OutboxDispatcher:Enabled</c>.
/// </summary>
internal static class LogisticsRiskGateHost
{
    public static IServiceCollection AddLogisticsDefiniteShipmentOutbox(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddWorkOutboxEventSource<LogisticsDefiniteShipmentOutboxEventSource>(
            LogisticsDefiniteShipmentOutboxV1.SourceName,
            new(LogisticsDefiniteShipmentOutboxV1.RecordedEventType, LogisticsDefiniteShipmentOutboxV1.Version),
            new(LogisticsDefiniteShipmentOutboxV1.CancelledEventType, LogisticsDefiniteShipmentOutboxV1.Version));
    }
}
