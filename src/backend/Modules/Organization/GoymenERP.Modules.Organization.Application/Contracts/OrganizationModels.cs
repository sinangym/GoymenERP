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
    IdempotencyConflict = 4,
}

public sealed record CommandExecutionResult(
    CommandResponseSnapshot Response,
    bool IsReplay,
    string OriginalCorrelationId)
{
    public CommandResult Result => Response.Result;
}

public sealed record CommandResponseSnapshot(
    CommandResult Result,
    int StatusCode,
    string? Title,
    string? Detail);

public static class CommandResponses
{
    public static CommandResponseSnapshot For(CommandResult result)
    {
        return result switch
        {
            CommandResult.Succeeded => new CommandResponseSnapshot(
                result,
                204,
                null,
                null),
            CommandResult.NotFound => new CommandResponseSnapshot(
                result,
                404,
                "Kayıt bulunamadı.",
                "İstenen kayıt bulunamadı veya bu bağlamda kullanılamıyor."),
            CommandResult.VersionConflict => new CommandResponseSnapshot(
                result,
                409,
                "Kayıt güncellendi.",
                "Kayıt başka bir işlem tarafından değiştirildi; veriyi yenileyin."),
            CommandResult.IdempotencyConflict => new CommandResponseSnapshot(
                result,
                409,
                "Idempotency-Key çakışması.",
                "Aynı Idempotency-Key farklı bir komut içeriği veya kullanıcıyla tekrar kullanılamaz."),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
        };
    }
}
