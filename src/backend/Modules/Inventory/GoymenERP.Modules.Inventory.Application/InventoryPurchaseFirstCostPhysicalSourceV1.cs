using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Frozen owner facts of one first purchase-cost allocation. No guessed shipment order or
/// current graph lookup is needed to read the committed evidence. Root quantities remain exact.</summary>
public sealed record InventoryPurchaseFirstCostPhysicalSourceV1(string SourceVersion, Guid CompanyId,
    Guid AcceptanceId, Guid InvoiceLineId, int InvoiceLineNo, Guid ReceiptMovementId, Guid RootLabelId,
    Guid ProductId, decimal MatchedQuantity, decimal AcceptedQuantity, string AllocationBasis,
    InventoryExactCostRatioV1 RemainingRootQuantityKg, InventoryExactCostRatioV1 NetShippedRootQuantityKg,
    IReadOnlyList<InventoryPurchaseFirstCostLabelV1> Labels, IReadOnlyList<InventoryPurchaseFirstCostShipmentV1> Shipments,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<InventoryPurchaseFirstCostReceiptReversalV1>? ReceiptReversals = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    InventoryPurchaseFirstCostPendingBasisV1? PendingBasis = null);

/// <summary>Remaining uninvoiced source after explicitly attributed supplier returns.</summary>
public sealed record InventoryPurchaseFirstCostPendingBasisV1(InventoryExactCostRatioV1 RootQuantityKg,
    InventoryExactCostRatioV1 StockQuantityKg, InventoryExactCostRatioV1 PurchaseQuantity,
    InventorySupplierReturnCostSourcesV1 SourceSnapshot);

/// <summary>Separate owner-measured purchase quantity and stock kg, joined by the actual adjustment/reversal identity.</summary>
public sealed record InventoryPurchaseFirstCostReceiptReversalV1(Guid ReversalId, Guid MovementId,
    InventoryGoodsReceiptReversalKindV1 Kind, decimal UndoQuantity, decimal QuantityKg);

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
