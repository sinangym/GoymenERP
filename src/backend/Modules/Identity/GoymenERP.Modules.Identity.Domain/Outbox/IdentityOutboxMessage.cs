namespace GoymenERP.Modules.Identity.Domain.Outbox;

/// <summary>
/// An append-only transactional outbox row of the identity module. It is written in the same transaction as the
/// state change it reports and is never updated or deleted by the writer. A group-level event (for example
/// <c>identity.founder.bootstrapped.v1</c>) has no company, so <see cref="CompanyId"/> is nullable. Dispatch
/// belongs to the platform outbox runner; this module only records.
/// </summary>
public sealed class IdentityOutboxMessage
{
    public const int EventTypeMaxLength = 160;

    public const int CorrelationMaxLength = 128;

    public const int SourceOperationMaxLength = 160;

    public const int AggregateTypeMaxLength = 100;

    private IdentityOutboxMessage()
    {
    }

    public IdentityOutboxMessage(
        Guid id,
        Guid? companyId,
        string eventType,
        int schemaVersion,
        string sourceOperation,
        string aggregateType,
        Guid aggregateId,
        long aggregateVersion,
        string payloadJson,
        DateTimeOffset occurredAtUtc,
        string correlationId,
        string? causationId)
    {
        if (id == Guid.Empty || aggregateId == Guid.Empty)
        {
            throw new ArgumentException("Giden olay kimlikleri boş olamaz.");
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Şirket kimliği verilirse boş olamaz.", nameof(companyId));
        }

        if (schemaVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        }

        if (aggregateVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(aggregateVersion));
        }

        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Olay anı UTC olmalıdır.", nameof(occurredAtUtc));
        }

        Id = id;
        CompanyId = companyId;
        EventType = Bounded(eventType, EventTypeMaxLength, nameof(eventType));
        SchemaVersion = schemaVersion;
        SourceOperation = Bounded(sourceOperation, SourceOperationMaxLength, nameof(sourceOperation));
        AggregateType = Bounded(aggregateType, AggregateTypeMaxLength, nameof(aggregateType));
        AggregateId = aggregateId;
        AggregateVersion = aggregateVersion;
        PayloadJson = string.IsNullOrWhiteSpace(payloadJson)
            ? throw new ArgumentException("Olay gövdesi boş olamaz.", nameof(payloadJson))
            : payloadJson;
        OccurredAtUtc = occurredAtUtc;
        CorrelationId = Bounded(correlationId, CorrelationMaxLength, nameof(correlationId));
        CausationId = causationId is null ? null : Bounded(causationId, CorrelationMaxLength, nameof(causationId));
        RecordedAtUtc = occurredAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid? CompanyId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public int SchemaVersion { get; private set; }

    public string SourceOperation { get; private set; } = string.Empty;

    public string AggregateType { get; private set; } = string.Empty;

    public Guid AggregateId { get; private set; }

    public long AggregateVersion { get; private set; }

    public string PayloadJson { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public string CorrelationId { get; private set; } = string.Empty;

    public string? CausationId { get; private set; }

    public DateTimeOffset RecordedAtUtc { get; private set; }

    private static string Bounded(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
        {
            throw new ArgumentException("Değer boş olamaz ve üst sınırı aşamaz.", parameterName);
        }

        return value;
    }
}
