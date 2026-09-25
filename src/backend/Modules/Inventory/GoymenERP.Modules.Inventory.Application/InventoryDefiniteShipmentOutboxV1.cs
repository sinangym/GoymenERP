namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// Inventory's durable event contract for a definite shipment. Persistence of this envelope is not
/// publication: dispatcher, retry, retention and consumer behavior remain outside this slice.
/// </summary>
public static class InventoryDefiniteShipmentOutboxV1
{
    public const string EventType = "inventory.definite-shipment.recorded.v1";
    public const int SchemaVersion = 1;
    public const string SourceOperation = "inventory.definite-shipment.record.v1";
    public const string AggregateType = "Inventory.StockAllocation";
}

/// <summary>The exact business payload persisted under <see cref="InventoryDefiniteShipmentOutboxV1.EventType"/>.</summary>
public sealed record InventoryDefiniteShipmentRecordedV1(
    Guid CompanyId,
    Guid ShipmentId,
    Guid LoadingInstructionId,
    Guid LoadingInstructionLineId,
    Guid AllocationId,
    string AllocationVersion,
    Guid LabelId,
    string LabelVersion,
    Guid MovementId,
    string QuantityKg,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string? CausationId);
