namespace GoymenERP.Modules.Organization.Application.Contracts;

public sealed record CompanySummary(
    Guid Id,
    string Code,
    string LegalName,
    bool IsPlaceholder);

public sealed record SiteSummary(
    Guid Id,
    string Code,
    string Name,
    string Type,
    bool IsActive);

public sealed record LocationSummary(
    Guid Id,
    Guid SiteId,
    string Code,
    string Name,
    bool IsExternal,
    bool IsActive,
    long Version);

public sealed record WorkCenterSummary(
    Guid Id,
    Guid LocationId,
    string Code,
    string Name,
    string Kind,
    bool IsActive);

public sealed record PageResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record SetLocationActiveState(
    bool IsActive,
    long ExpectedVersion,
    string Reason);

public sealed record CommandMetadata(
    Guid CompanyId,
    string ActorId,
    string CorrelationId,
    string? CausationId,
    string SourceChannel,
    string? ClientIpAddress,
    string? UserAgent,
    string IdempotencyKey);

public enum CommandResult
{
    Succeeded = 1,
    NotFound = 2,
    VersionConflict = 3,
}
