namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>ALC-S6 / ALS-D92/D93: one outbox event per general line actually revived by a net receipt correction.</summary>
public static class PurchasingOrderLineRevivedEventV1
{
    public const string EventType = "purchasing.order-line.revived.v1";
    public const int SchemaVersion = 1;
    public const string AggregateType = "Purchasing.PurchaseOrderLine";
}

/// <summary>
/// Exactly one of ReceiptAdjustmentId or BatchId identifies the immutable owner operation.
/// Inventory owns historical affected Sales links, captured by the host under the same transaction and line locks.
/// A delayed consumer must use this immutable snapshot, not links created after the event. Null is legacy/missing evidence.
/// NetOpenedQuantity is positive, canonical purchase-unit text. No money, price, grant or external notification is carried.
/// </summary>
public sealed record PurchasingOrderLineRevivedPayloadV1(Guid CompanyId, Guid PurchaseOrderId,
    Guid PurchaseOrderLineId, long OrderVersion, string NetOpenedQuantity, Guid? ReceiptAdjustmentId, Guid? BatchId,
    IReadOnlyList<PurchaseRevivalSalesTargetV1>? SalesTargets = null);

public sealed record PurchaseRevivalSalesTargetV1(Guid SalesOrderId, Guid SalesOrderLineId);

/// <summary>Server-only staging evidence. Available-empty is explicit; missing or inconsistent evidence is not empty success.</summary>
public sealed record PurchaseRevivalNotificationSnapshotV1(Guid CompanyId, Guid PurchaseOrderId, Guid GeneralLineId,
    string ObservedOrderVersion, IReadOnlyList<PurchaseRevivalSalesTargetV1> SalesTargets);
