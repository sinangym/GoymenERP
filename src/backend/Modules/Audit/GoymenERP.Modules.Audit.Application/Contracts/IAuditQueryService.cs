namespace GoymenERP.Modules.Audit.Application.Contracts;

public interface IAuditQueryService
{
    Task<AuditEventQueryResult> GetEventsAsync(
        int pageSize,
        string? cursor,
        string? correlationId,
        string? entityType,
        string? entityId,
        CancellationToken cancellationToken = default);
}

public enum AuditEventQueryStatus
{
    Ok,
    InvalidCursor,
    Denied,
}

/// <summary>
/// The outcome of one audit read. <see cref="Page"/> is set only for <see cref="AuditEventQueryStatus.Ok"/>:
/// a refused read releases neither rows nor a cursor.
/// </summary>
public sealed record AuditEventQueryResult(AuditEventQueryStatus Status, AuditEventPage? Page)
{
    public static AuditEventQueryResult Ok(AuditEventPage page) => new(AuditEventQueryStatus.Ok, page);

    public static AuditEventQueryResult InvalidCursor { get; } = new(AuditEventQueryStatus.InvalidCursor, null);

    public static AuditEventQueryResult Denied { get; } = new(AuditEventQueryStatus.Denied, null);
}
