namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// API-11 (ALS-D10/ALS-D12, F-017 §3.6): one closure (<see cref="Closed"/> true) or reopening (false) of a general purchase-order
/// line, recorded at the order version it produced (<see cref="OrderVersionAfter"/>) with its actor, time and reason. The line's
/// stored <c>Closed</c> total is the current state; these rows are its history and are replayed when the order is rebuilt.
/// Append only: never updated, never deleted.
/// </summary>
internal sealed class PurchaseOrderLineClosureEventRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }

    public Guid OrderId { get; set; }

    public Guid LineId { get; set; }

    public long OrderVersionAfter { get; set; }

    public bool Closed { get; set; }

    public required string Reason { get; set; }

    public required string RecordedBy { get; set; }

    public DateTimeOffset RecordedAtUtc { get; set; }

    public required string CorrelationId { get; set; }
}
