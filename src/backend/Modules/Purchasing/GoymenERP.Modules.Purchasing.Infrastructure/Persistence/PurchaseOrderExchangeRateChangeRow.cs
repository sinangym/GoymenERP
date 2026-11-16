namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// ALC-SC33 (FNS-D79/D80/D83/D91): one recorded exchange rate of a foreign-currency order — the automatic default it was opened with
/// (<see cref="OrderVersionAfter"/> 1, the creation version, <see cref="Source"/> automatic) or a preparer's change before approval
/// (the version that change produced). The rate is a NUMBER with its kind, rate day and target currency; it is never recomputed from a
/// bulletin. The row with the highest version is the order's current rate; the rows are replayed when the order is rebuilt. Append
/// only: never updated, never deleted.
/// </summary>
internal sealed class PurchaseOrderExchangeRateChangeRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }

    public Guid OrderId { get; set; }

    public long OrderVersionAfter { get; set; }

    public decimal Rate { get; set; }

    public Guid TargetCurrencyId { get; set; }

    public int Kind { get; set; }

    public DateOnly RateDay { get; set; }

    public int Source { get; set; }

    public DateOnly? BulletinDate { get; set; }

    public string? Reason { get; set; }

    public required string RecordedBy { get; set; }

    public DateTimeOffset RecordedAtUtc { get; set; }

    public required string CorrelationId { get; set; }
}
