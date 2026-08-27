namespace GoymenERP.Modules.Audit.Application.Contracts;

public interface IAuditQueryService
{
    Task<AuditEventPage?> GetEventsAsync(
        int pageSize,
        string? cursor,
        string? correlationId,
        string? entityType,
        string? entityId,
        CancellationToken cancellationToken = default);
}
