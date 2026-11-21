namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Reading;

/// <summary>
/// ADR-SEC-001 section 5: the company's read of its own security event channel. The read is itself recorded as
/// <c>security.events.read</c> before any row is read, and the write is fail-closed: an unrecorded read returns
/// <see cref="SecurityEventQueryStatusV1.Unavailable"/> and releases nothing.
/// </summary>
public interface ISecurityEventQueryServiceV1
{
    Task<SecurityEventQueryResultV1> GetEventsAsync(
        int pageSize,
        string? cursor,
        string? eventType,
        string? correlationId,
        CancellationToken cancellationToken = default);
}

public enum SecurityEventQueryStatusV1
{
    Ok,
    InvalidCursor,
    InvalidFilter,
    Denied,
    Unavailable,
}

public sealed record SecurityEventQueryResultV1(SecurityEventQueryStatusV1 Status, SecurityEventPageV1? Page)
{
    public static SecurityEventQueryResultV1 InvalidCursor { get; } = new(SecurityEventQueryStatusV1.InvalidCursor, null);

    public static SecurityEventQueryResultV1 InvalidFilter { get; } = new(SecurityEventQueryStatusV1.InvalidFilter, null);

    public static SecurityEventQueryResultV1 Denied { get; } = new(SecurityEventQueryStatusV1.Denied, null);

    public static SecurityEventQueryResultV1 Unavailable { get; } = new(SecurityEventQueryStatusV1.Unavailable, null);

    public static SecurityEventQueryResultV1 Ok(SecurityEventPageV1 page) => new(SecurityEventQueryStatusV1.Ok, page);
}

public sealed record SecurityEventPageV1(
    IReadOnlyList<SecurityEventItemV1> Items,
    int PageSize,
    string? NextCursor);

/// <summary>
/// One company-scoped security event row. The retention policy reference and the legal hold flag stay inside the
/// module: they are retention administration, not something the reader acts on.
/// </summary>
public sealed record SecurityEventItemV1(
    Guid Id,
    int SchemaVersion,
    DateTimeOffset OccurredAtUtc,
    Guid CompanyId,
    Guid? RequestedCompanyId,
    string ActorType,
    string? ActorId,
    string EventType,
    string Phase,
    string Outcome,
    string? ReasonCode,
    string Severity,
    string? PermissionCode,
    string? PolicyId,
    int? PolicyVersion,
    string CorrelationId,
    string? CausationId,
    string SourceChannel,
    string? AiToolId,
    string? AiToolVersion,
    string? AiRiskClass,
    string? AiDataClass,
    DateTimeOffset RetainUntilUtc,
    int OccurrenceCount,
    DateTimeOffset? AggregationWindowStartUtc,
    DateTimeOffset? AggregationWindowEndUtc,
    string IntegrityHash);
