namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// API-11 (ALIS_F017 v1 §3.6; board contract "purchase-line-closed", consumer: the Inventory owner): the integration event Purchasing
/// writes to its transactional outbox (<c>purchasing.OutboxMessages</c>, ADR-009) in the same save as a general line's closure
/// (<see cref="PurchasingOrderLineClosedPayloadV1.Closed"/> true) or reopening (false). One message per command; the aggregate is
/// the general line and the sequence is the order version the command produced, so (type, line, sequence) is unique and a consumer
/// deduplicates by the message id or by (line, version) in its inbox. Technical slices close and reopen with their general line and
/// emit nothing of their own. The payload carries identities and the state only: no quantity, price or party.
/// </summary>
public static class PurchasingOrderLineClosedEventV1
{
    /// <summary>The outbox event type (board draft <c>purchasing.order-line.closed</c>, schema v1).</summary>
    public const string EventType = "purchasing.order-line.closed.v1";

    public const int SchemaVersion = 1;

    /// <summary>The aggregate the message is about: a general purchase-order line (<c>AggregateId</c> = the line id).</summary>
    public const string AggregateType = "Purchasing.PurchaseOrderLine";
}

/// <summary>
/// The v1 payload (camelCase JSON): the line's company, its order and line identities, the new closure state and the order version
/// the closure or reopening produced (the expected version a later Purchasing command must carry). The board's contract is
/// <c>{CompanyId, PurchaseOrderLineId, Closed, Version}</c>; <see cref="PurchaseOrderId"/> is additive (a line id is unique only within
/// its order's company, and the Inventory allocation names both).
/// </summary>
public sealed record PurchasingOrderLineClosedPayloadV1(
    Guid CompanyId,
    Guid PurchaseOrderId,
    Guid PurchaseOrderLineId,
    bool Closed,
    long Version);
