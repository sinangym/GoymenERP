using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// Inventory's durable event contract for one supplier-return exit (B-19). Persistence of this event is not
/// publication; dispatch, retry, retention and consumers remain outside this participant.
/// </summary>
public static class InventorySupplierReturnExitOutboxV1
{
    public const string EventType = "inventory.supplier-return-exit.recorded.v1";
    public const int SchemaVersion = 1;
    public const string SourceOperation = InventorySupplierReturnExitContractV1.CommandName;
    public const string AggregateType = "Inventory.MaterialLabel";
}

/// <summary>
/// The original receipt's physical identity, the position the kilograms left and the original unit cost the exit
/// carries for the average-cost ledger (SAT-D306; null = unavailable).
/// </summary>
public sealed record InventorySupplierReturnExitRecordedV1(
    Guid CompanyId,
    Guid SupplierReturnId,
    Guid SupplierReturnLineId,
    Guid GoodsAcceptanceId,
    Guid OriginalReceiptMovementId,
    Guid InventoryMovementId,
    Guid LabelId,
    string LabelVersion,
    Guid ProductId,
    StockPartyKind OwnerKind,
    Guid OwnerId,
    StockPartyKind CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    BlockState SourceBlock,
    string QuantityKg,
    string? OriginalUnitCostTryPerKg,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string? CausationId);
