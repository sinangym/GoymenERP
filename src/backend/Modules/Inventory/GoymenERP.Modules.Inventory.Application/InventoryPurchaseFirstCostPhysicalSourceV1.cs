using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Frozen owner facts of one first purchase-cost allocation. No guessed shipment order or
/// current graph lookup is needed to read the committed evidence. Root quantities remain exact.</summary>
public sealed record InventoryPurchaseFirstCostPhysicalSourceV1(string SourceVersion, Guid CompanyId,
    Guid AcceptanceId, Guid InvoiceLineId, int InvoiceLineNo, Guid ReceiptMovementId, Guid RootLabelId,
    Guid ProductId, decimal MatchedQuantity, decimal AcceptedQuantity, string AllocationBasis,
    InventoryExactCostRatioV1 RemainingRootQuantityKg, InventoryExactCostRatioV1 NetShippedRootQuantityKg,
    IReadOnlyList<InventoryPurchaseFirstCostLabelV1> Labels, IReadOnlyList<InventoryPurchaseFirstCostShipmentV1> Shipments);

public sealed record InventoryPurchaseFirstCostLabelV1(Guid LabelId, long VersionBefore, long VersionAfter,
    string ProductVersion, InventoryExactCostRatioV1 RootShare,
    IReadOnlyList<InventoryPurchaseFirstCostPhysicalPositionV1> Positions);

public sealed record InventoryPurchaseFirstCostPhysicalPositionV1(StockPartyKind OwnerKind, Guid OwnerId,
    StockPartyKind CustodianKind, Guid CustodianId, Guid LocationId, BlockState Block, decimal QuantityKg);

public sealed record InventoryPurchaseFirstCostShipmentV1(Guid ShipmentId, Guid MovementId, int MovementLineNumber,
    Guid LoadingInstructionLineId, Guid LabelId, Guid ProductId, string ProductVersion, Guid LocationId,
    decimal PhysicalQuantityKg, InventoryExactCostRatioV1 RootShare, InventoryExactCostRatioV1 NetRootQuantityKg,
    IReadOnlyList<InventoryPurchaseFirstCostReturnV1> Returns, DateTimeOffset RecordedAtUtc,
    long? ProvenOriginalSequence = null);

public sealed record InventoryPurchaseFirstCostReturnV1(Guid ReturnId, Guid MovementId, int MovementLineNumber,
    Guid SourceIssueMovementId, decimal QuantityKg, DateTimeOffset RecordedAtUtc);
