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
/// Inventory owns historical affected Sales links; no request intent or recipient is guessed here.
/// NetOpenedQuantity is positive, canonical purchase-unit text. No money, price, grant or external notification is carried.
/// </summary>
public sealed record PurchasingOrderLineRevivedPayloadV1(Guid CompanyId, Guid PurchaseOrderId,
    Guid PurchaseOrderLineId, long OrderVersion, string NetOpenedQuantity, Guid? ReceiptAdjustmentId, Guid? BatchId);
