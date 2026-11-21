namespace GoymenERP.Modules.Logistics.Application.Outbox;

/// <summary>
/// LOJ-D19 / SAT-SC33: the versioned events Logistics publishes from its own outbox when a definite shipment is recorded
/// (and, once the shipment cancel command exists, cancelled). The event row is written in the SAME save as the shipment,
/// so it exists exactly when the shipment does. The Work dispatcher reads it through the registered source
/// <see cref="SourceName"/>; registering the source does not enable the dispatcher (a human gate).
/// </summary>
public static class LogisticsDefiniteShipmentOutboxV1
{
    /// <summary>The Work outbox source name; it is the dispatcher's cursor/inbox key and is never renamed.</summary>
    public const string SourceName = "logistics";

    public const string RecordedEventType = "logistics.definite-shipment.recorded.v1";

    /// <summary>
    /// Declared for the Sales consumer; no producer yet. The shipment cancel (reversal) command is not decided, so no
    /// cancel event is written until it is (it must carry the recorded shipment's exception reference unchanged).
    /// </summary>
    public const string CancelledEventType = "logistics.definite-shipment.cancelled.v1";

    public const int Version = 1;
}

/// <summary>
/// Payload of both events (web JSON). <see cref="RiskExceptionNo"/> is the Sales order's exception number the shipment
/// consumed at the risk gate, null when the shipment was within the limit. <see cref="RiskExceptionId"/> is reserved by the
/// Sales consumer contract and always null here: the gate port answers only the number. No amount is carried (SAT-D162).
/// </summary>
public sealed record LogisticsDefiniteShipmentEventV1(
    Guid CompanyId,
    Guid ShipmentId,
    Guid SalesOrderId,
    Guid LoadingInstructionId,
    int? RiskExceptionNo,
    Guid? RiskExceptionId);
