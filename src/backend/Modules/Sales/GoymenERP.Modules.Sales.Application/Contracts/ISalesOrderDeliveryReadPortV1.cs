namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// AT-024 ("Kısmi teslimat sipariş kalanını doğru tutar"): the Sales-declared read port the DELIVERY OWNER answers —
/// Logistics, which records definite shipments and deliveries against the Sales order line its loading-instruction line
/// is bound to (LOG-4 D4, LOG-5). Sales never reads another module's tables; the owner implements this port over its own
/// company-filtered store and registers it in its own module (the <see cref="ISalesOrderLineReferencePortV1"/> shape in
/// the other direction). Until an owner registers it, the Sales read answers 503, never an assumed zero.
/// <para>
/// The port carries NO authority (LOG-4 D6): the consumer (the Sales draft read) measures its own name before and after
/// asking. It reads under the caller's server-resolved company only — another company's records are invisible, never a
/// 403 — and it CREATES NOTHING: no attempt, audit, share or version effect. The quantities are the owner's recorded
/// totals: a replayed command (same key) or a repeated delivery id never produced a second share, so it is never counted
/// twice.
/// </para>
/// </summary>
public interface ISalesOrderDeliveryReadPortV1
{
    /// <summary>
    /// The owner's recorded shipped and delivered totals of every line of <paramref name="orderId"/> in the context
    /// company, grouped by the Sales order line and the unit it was recorded in. A line the owner holds nothing for is
    /// absent (the consumer reads that as zero). An unknown order answers an empty list.
    /// </summary>
    Task<SalesOrderDeliveryReadAnswerV1> ReadOrderDeliveriesAsync(Guid orderId, CancellationToken cancellationToken = default);
}

/// <summary>The owner's answer for one Sales order in the context company.</summary>
public sealed record SalesOrderDeliveryReadAnswerV1(Guid CompanyId, Guid OrderId, IReadOnlyList<SalesOrderLineDeliveryV1> Lines);

/// <summary>
/// One Sales order line's recorded totals in one unit, exact (Ö2: kilograms ≤ 3 decimals, pieces whole; never rounded):
/// <paramref name="ShippedQuantity"/> — definitely shipped (the stock left); <paramref name="DeliveredQuantity"/> —
/// delivered to the customer, always bound to a recorded shipment, so never more than shipped.
/// </summary>
public sealed record SalesOrderLineDeliveryV1(Guid OrderLineId, Guid UnitId, decimal ShippedQuantity, decimal DeliveredQuantity);
