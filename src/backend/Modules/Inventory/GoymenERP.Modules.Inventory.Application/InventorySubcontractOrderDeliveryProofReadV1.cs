namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Price-free evidence for requested immutable subcontract shipments and this order's remaining physical holds.</summary>
public interface IInventorySubcontractOrderDeliveryProofReadPortV1
{
    Task<InventorySubcontractOrderDeliveryProofReadV1> ReadAsync(Guid companyId, Guid salesOrderId,
        IReadOnlyList<Guid> shipmentIds, Func<CancellationToken, Task> freshConsumerFence, CancellationToken token = default);
}
public sealed record InventorySubcontractOrderDeliveryProofReadV1(Guid CompanyId, Guid SalesOrderId, bool Available,
    bool HasOpenAllocations, IReadOnlyList<InventorySubcontractShipmentProofV1> Shipments, string? SourceVersion);
public sealed record InventorySubcontractShipmentProofV1(Guid ShipmentId, Guid InstructionId, Guid InstructionLineId,
    Guid MovementId, Guid AllocationId, decimal PhysicalKg, InventorySubcontractDeliverySourceV1 Source);
