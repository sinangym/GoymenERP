namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

internal sealed class PurchaseAcceptanceRow : IPurchasingTenantRow
{
    public Guid AcceptanceId { get; set; }

    public Guid CompanyId { get; set; }

    public Guid OrderId { get; set; }

    public Guid LineId { get; set; }

    public decimal Quantity { get; set; }

    public long OrderVersionAfter { get; set; }

    public DateTimeOffset RecordedAtUtc { get; set; }

    public required string IdempotencyKeyHash { get; set; }

    public required string RequestHash { get; set; }

    public required string ActorId { get; set; }

    public required string CorrelationId { get; set; }

    public required string ResultJson { get; set; }
}
