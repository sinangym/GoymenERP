using System.Text.Json.Serialization;

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

/// <param name="CanOpenLocationDetail">
/// Whether the caller's current <c>organization.locations.read</c> scope reaches THIS
/// row's location — true exactly when <c>GET /organization/locations/{LocationId}</c>
/// would answer 200 for the same caller. It is the caller's own authority over a row it
/// already sees, never a scope list, so it discloses nothing beyond that row.
/// </param>
public sealed record WorkCenterSummary(
    Guid Id,
    Guid LocationId,
    string Code,
    string Name,
    string Kind,
    bool IsActive,
    bool CanOpenLocationDetail);

public sealed record PageResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record SetLocationActiveState(
    [property: JsonRequired] bool IsActive,
    [property: JsonRequired] long ExpectedVersion,
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
    string? Detail,
    VersionConflictSnapshot? Conflict = null);

public sealed record VersionConflictSnapshot(
    long ExpectedVersion,
    long CurrentVersion);

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
            CommandResult.VersionConflict => throw new ArgumentException(
                "Version conflict cevabı beklenen ve mevcut sürümü içermelidir.",
                nameof(result)),
            CommandResult.IdempotencyConflict => new CommandResponseSnapshot(
                result,
                409,
                "Idempotency-Key çakışması.",
                "Aynı Idempotency-Key farklı bir komut içeriği veya kullanıcıyla tekrar kullanılamaz."),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
        };
    }

    public static CommandResponseSnapshot VersionConflict(
        long expectedVersion,
        long currentVersion)
    {
        return new CommandResponseSnapshot(
            CommandResult.VersionConflict,
            409,
            "Kayıt güncellendi.",
            "Kayıt başka bir işlem tarafından değiştirildi; beklenen ve mevcut sürüm farkını inceleyip veriyi yenileyin.",
            new VersionConflictSnapshot(expectedVersion, currentVersion));
    }
}
