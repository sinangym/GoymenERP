namespace GoymenERP.Modules.Audit.Application.Contracts;

public interface IAuditQueryService
{
    /// <summary>
    /// Reads one keyset page of the current company's audit rows, newest first.
    /// </summary>
    /// <param name="outcome">
    /// Optional outcome filter (F023-5a): <see cref="AuditOutcomes.Succeeded"/> or
    /// <see cref="AuditOutcomes.Refused"/>, matched exactly after trimming. Any other non-blank value is
    /// <see cref="AuditEventQueryStatus.InvalidOutcome"/>. The filter narrows the same
    /// (OccurredAtUtc DESC, Id DESC) keyset, so a cursor stays valid under the same filter.
    /// </param>
    Task<AuditEventQueryResult> GetEventsAsync(
        int pageSize,
        string? cursor,
        string? correlationId,
        string? entityType,
        string? entityId,
        string? outcome,
        CancellationToken cancellationToken = default);
}

public enum AuditEventQueryStatus
{
    Ok,
    InvalidCursor,
    InvalidOutcome,
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

    public static AuditEventQueryResult InvalidOutcome { get; } = new(AuditEventQueryStatus.InvalidOutcome, null);

    public static AuditEventQueryResult Denied { get; } = new(AuditEventQueryStatus.Denied, null);
}
