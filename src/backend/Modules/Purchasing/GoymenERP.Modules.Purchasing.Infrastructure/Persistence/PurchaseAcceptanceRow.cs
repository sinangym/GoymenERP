namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

internal sealed class PurchaseAcceptanceRow : IPurchasingTenantRow
{
    public Guid AcceptanceId { get; set; }

    public Guid CompanyId { get; set; }

    public Guid OrderId { get; set; }

    public Guid LineId { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>AK-3 (ALS-D119/ALS-D121): the weighed kilograms of a piece-line acceptance; null on a kilogram line and on older rows.</summary>
    public decimal? MeasuredQuantityKg { get; set; }

    public long OrderVersionAfter { get; set; }

    public DateTimeOffset RecordedAtUtc { get; set; }

    public required string IdempotencyKeyHash { get; set; }

    public required string RequestHash { get; set; }

    public required string ActorId { get; set; }

    public required string CorrelationId { get; set; }

    public required string ResultJson { get; set; }
}
