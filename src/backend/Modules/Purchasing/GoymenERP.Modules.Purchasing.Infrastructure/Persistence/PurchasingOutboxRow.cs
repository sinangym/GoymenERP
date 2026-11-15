namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// Purchasing's transactional outbox (ADR-009, the Sales <c>sales.OutboxMessages</c> shape): one integration event per row, written
/// in the same save as the state change that produced it and never changed afterwards. API-11 (F-017 §3.6) writes the first type,
/// <c>purchasing.order-line.closed.v1</c>; the dispatcher that publishes the rows to their consumers is not part of this slice.
/// (Company, event type, aggregate, sequence) is unique, so one state change emits one message per type.
/// </summary>
internal sealed class PurchasingOutboxRow : IPurchasingTenantRow
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }

    public required string EventType { get; set; }

    public int SchemaVersion { get; set; }

    public required string SourceOperation { get; set; }

    public required string AggregateType { get; set; }

    public Guid AggregateId { get; set; }

    public long AggregateVersion { get; set; }

    public int Sequence { get; set; }

    public required string PayloadJson { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public required string CorrelationId { get; set; }

    public string? CausationId { get; set; }

    public DateTimeOffset RecordedAtUtc { get; set; }
}
