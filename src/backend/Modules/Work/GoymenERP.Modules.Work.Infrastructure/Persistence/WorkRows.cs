namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

internal interface IWorkTenantRow
{
    Guid CompanyId { get; }
}

internal sealed class OrganizationCompanyReference
{
    public Guid Id { get; set; }
}

internal sealed class WorkDesiredStateSnapshotRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public byte[] SourceKeyDigest { get; set; } = [];
    public byte[] ScopeKeyDigest { get; set; } = [];
    public byte[] SourceKeyUtf16Be { get; set; } = [];
    public byte[] ScopeKeyUtf16Be { get; set; } = [];
    public long HighestObservationOrdinal { get; set; }
    public int HighestAvailability { get; set; }
    public int? HighestDesiredState { get; set; }
    public Guid HighestEvidenceId { get; set; }
    public string HighestPayloadSha256 { get; set; } = string.Empty;
    public long? EffectiveObservationOrdinal { get; set; }
    public int? EffectiveDesiredState { get; set; }
    public Guid? EffectiveEvidenceId { get; set; }
    public string? EffectivePayloadSha256 { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class WorkObservationInboxRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public byte[] SourceKeyDigest { get; set; } = [];
    public byte[] ScopeKeyDigest { get; set; } = [];
    public byte[] SourceKeyUtf16Be { get; set; } = [];
    public byte[] ScopeKeyUtf16Be { get; set; } = [];
    public long ObservationOrdinal { get; set; }
    public int Availability { get; set; }
    public int? DesiredState { get; set; }
    public Guid EvidenceId { get; set; }
    public string PayloadSha256 { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

internal sealed class WorkOutboxRow : IWorkTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public byte[] SourceKeyDigest { get; set; } = [];
    public byte[] ScopeKeyDigest { get; set; } = [];
    public byte[] SourceKeyUtf16Be { get; set; } = [];
    public byte[] ScopeKeyUtf16Be { get; set; } = [];
    public long ObservationOrdinal { get; set; }
    public long AggregateVersion { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? CausationId { get; set; }
}

internal enum WorkOutboxDeliveryStatus
{
    Delivered = 1,
    Failing = 2,
    Poisoned = 3,
}

/// <summary>Dispatcher read position per (company, source). The source outbox itself is never touched.</summary>
internal sealed class WorkOutboxSourceCursorRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Consumer inbox/dedup, retry and poison record: one row per (company, source, consumer, message).</summary>
internal sealed class WorkOutboxDeliveryRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string ConsumerName { get; set; } = string.Empty;
    public Guid MessageId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public string PayloadSha256 { get; set; } = string.Empty;
    public int Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset FirstAttemptAtUtc { get; set; }
    public DateTimeOffset LastAttemptAtUtc { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
    public DateTimeOffset? DeliveredAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
