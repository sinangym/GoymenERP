namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// One unpublished Counterparty-owned event stored in the producer transaction. This module has no dispatcher yet and
/// never mutates publication state; the row is append-only and belongs to the company that wrote it.
/// </summary>
internal sealed class CounterpartyOutboxRow
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public int SchemaVersion { get; set; }

    public string SourceOperation { get; set; } = string.Empty;

    public string AggregateType { get; set; } = string.Empty;

    public Guid AggregateId { get; set; }

    public long AggregateVersion { get; set; }

    public long Sequence { get; set; }

    public string PayloadJson { get; set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public string? CausationId { get; set; }

    public DateTimeOffset RecordedAtUtc { get; set; }
}
