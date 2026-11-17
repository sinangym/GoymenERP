using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Application;

public enum InventoryShipmentCostReadOutcomeV1 { Available = 1, NotFound = 2, Unreadable = 3 }

/// <summary>
/// One actual definite-shipment stock exit and its immutable TL stamp. No current average, invoice price,
/// USD or later cost-generation effect is substituted for the original stamp. Quantity/position and the
/// loading-instruction identities come from Inventory's original movement and recorded shipment envelope.
/// </summary>
public sealed record InventoryShipmentCostLineV1(int MovementLineNumber, Guid LoadingInstructionId,
    Guid LoadingInstructionLineId, Guid AllocationId, Guid LabelId, string LabelCode, Guid ProductId,
    string ProductVersion, StockPartyKind OwnerKind, Guid OwnerId, StockPartyKind CustodianKind,
    Guid CustodianId, Guid LocationId, BlockState Block, decimal QuantityKg, InventoryMovementCostStampV1 Tl);

/// <summary>
/// Available means the original physical identity/provenance was read successfully. It is NOT a claim that
/// TL is known: a legacy/missing/unavailable stamp remains explicitly Unavailable on the line. The consumer
/// must admit that cost status independently. This read neither triggers nor authorizes Accounting/SMM.
/// </summary>
public sealed record InventoryShipmentCostReadV1(InventoryShipmentCostReadOutcomeV1 Outcome, Guid CompanyId,
    Guid ShipmentId, Guid MovementId, IReadOnlyList<InventoryShipmentCostLineV1> Lines, string SourceVersion,
    string? Reason = null);

/// <summary>The consumer proves its real Logistics shipment/movement linkage and its own fresh authority.</summary>
public interface IInventoryShipmentCostReadPortV1
{
    Task<InventoryShipmentCostReadV1> GetAsync(Guid companyId, Guid shipmentId, Guid movementId,
        CancellationToken cancellationToken = default);
}
