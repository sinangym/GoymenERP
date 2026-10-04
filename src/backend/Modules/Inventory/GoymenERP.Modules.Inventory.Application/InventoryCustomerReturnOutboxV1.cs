using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// Inventory's durable event contract for an approved customer return. Persistence of this event
/// is not publication; dispatch, retry, retention and consumers remain outside this participant.
/// </summary>
public static class InventoryCustomerReturnOutboxV1
{
    public const string EventType = "inventory.customer-return.recorded.v1";
    public const int SchemaVersion = 1;
    public const string SourceOperation = InventoryCustomerReturnContractV1.CommandName;
    public const string AggregateType = "Inventory.MaterialLabel";
}

/// <summary>The original shipment's physical identity and its one compensating movement.</summary>
public sealed record InventoryCustomerReturnRecordedV1(
    Guid CompanyId,
    Guid CustomerReturnId,
    Guid SourceShipmentId,
    Guid LoadingInstructionLineId,
    Guid OriginalShipmentMovementId,
    Guid InventoryMovementId,
    Guid LabelId,
    string LabelVersion,
    Guid ProductId,
    StockPartyKind OwnerKind,
    Guid OwnerId,
    StockPartyKind CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    string QuantityKg,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string? CausationId);
