namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// A3 API-30 (ALS-D33, ALS-D21): one delivery slice (termin dilimi, hubvogal's "sıra") of a general purchase-order line, recorded as
/// part of one schedule revision: every schedule command appends the line's whole new set at the order version it produced
/// (<see cref="OrderVersionAfter"/>), so the line's current schedule is its latest revision and every earlier one stays as history.
/// Append only: never updated, never deleted. The slices of one revision sum to the line's ordered quantity — the domain's rule,
/// measured again when the order is rebuilt.
/// </summary>
internal sealed class PurchaseOrderLineDeliverySliceRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }

    public Guid OrderId { get; set; }

    public Guid LineId { get; set; }

    public long OrderVersionAfter { get; set; }

    public int SliceNo { get; set; }

    public Guid SliceId { get; set; }

    public decimal Quantity { get; set; }

    public DateOnly DeliveryDate { get; set; }

    public required string RecordedBy { get; set; }

    public DateTimeOffset RecordedAtUtc { get; set; }

    public required string CorrelationId { get; set; }
}
