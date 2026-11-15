using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Organization.Application.Contracts;

public sealed record CompanySummary(
    Guid Id,
    string Code,
    string LegalName,
    bool IsPlaceholder);

/// <param name="Version">
/// The site's optimistic-concurrency version (starts at 1, moves once per accepted change) — the
/// <c>expectedVersion</c> a site edit (<c>PUT /organization/sites/{id}</c>) sends back.
/// </param>
public sealed record SiteSummary(
    Guid Id,
    string Code,
    string Name,
    string Type,
    bool IsActive,
    long Version);

public sealed record LocationSummary(
    Guid Id,
    Guid SiteId,
    string Code,
    string Name,
    bool IsExternal,
    bool IsActive,
    long Version,
    Guid? OperatorPartyId = null);

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

/// <summary>
/// ORG-04 — one work center's read-only card (W1-ORG / AV-01, Business #66 5702892195). The work-centre row's
/// facts plus the record's own <paramref name="Version"/>, kept lossless for a later stale check and never a
/// command authority here (the card writes nothing). No capacity, shift, load, ownership or location label: the
/// Organization record carries none, and a location's name is its own read.
/// </summary>
/// <param name="CanOpenLocationDetail">
/// The same answer as <see cref="WorkCenterSummary.CanOpenLocationDetail"/>, computed by the same predicate: true
/// exactly when <c>GET /organization/locations/{LocationId}</c> would answer 200 for this caller.
/// </param>
public sealed record WorkCenterDetail(
    Guid Id,
    Guid LocationId,
    string Code,
    string Name,
    string Kind,
    bool IsActive,
    long Version,
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

public sealed record SetLocationOperator(
    [property: JsonRequired] Guid OperatorPartyId,
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
    Created = 5,
    CodeInUse = 6,
    OperatorIneligible = 7,
    DependencyUnavailable = 8,

    /// <summary>KIM-S23 (KIM-D28): the proposed VKN already belongs to another company of the group.</summary>
    TaxIdInUse = 9,

    /// <summary>KIM-S23: the company already has a pending VKN change request.</summary>
    TaxIdChangePending = 10,

    /// <summary>KIM-S23: the change request is not pending (already decided) or is not the one under decision.</summary>
    TaxIdChangeNotPending = 11,

    /// <summary>KIM-S23: the company's VKN or version moved after the request was prepared; it cannot be applied.</summary>
    TaxIdChangeStale = 12,

    /// <summary>KIM-S23: the preparer may not decide their own request (hazırlayan ≠ onaylayan).</summary>
    TaxIdSelfDecision = 13,

    /// <summary>KIM-S23: the proposed VKN equals the company's current VKN.</summary>
    TaxIdUnchanged = 14,
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
    VersionConflictSnapshot? Conflict = null,
    OrganizationCreatedRecord? Created = null);

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
            CommandResult.CodeInUse => new CommandResponseSnapshot(
                result,
                422,
                "Kod kullanımda.",
                "Bu kod şirketteki başka bir kayıtta kullanılıyor; farklı bir kod girin."),
            CommandResult.OperatorIneligible => new CommandResponseSnapshot(
                result,
                422,
                "Lokasyon operatörü uygun değil.",
                "Seçilen cari bu şirketin harici lokasyon operatörü olarak kullanılamıyor."),
            CommandResult.DependencyUnavailable => new CommandResponseSnapshot(
                result,
                503,
                "Lokasyon operatörü doğrulanamadı.",
                "Cari uygunluğu şu anda doğrulanamıyor; daha sonra yeniden deneyin."),
            CommandResult.TaxIdInUse => new CommandResponseSnapshot(
                result,
                422,
                "VKN kullanımda.",
                "Bu VKN gruptaki başka bir şirkette kayıtlı; aynı VKN iki şirkette olamaz."),
            CommandResult.TaxIdChangePending => new CommandResponseSnapshot(
                result,
                409,
                "Bekleyen VKN değişikliği var.",
                "Şirketin onay bekleyen bir VKN değişiklik talebi var; önce o talep onaylanmalı veya reddedilmelidir."),
            CommandResult.TaxIdChangeNotPending => new CommandResponseSnapshot(
                result,
                409,
                "VKN değişikliği beklemede değil.",
                "Bu VKN değişiklik talebi artık onay beklemiyor; ekranı yenileyin."),
            CommandResult.TaxIdChangeStale => new CommandResponseSnapshot(
                result,
                409,
                "VKN değişikliği güncelliğini yitirdi.",
                "Talep hazırlandıktan sonra şirket kaydı değişti; talebi reddedip yeniden hazırlayın."),
            CommandResult.TaxIdSelfDecision => new CommandResponseSnapshot(
                result,
                409,
                "Kendi talebinizi karara bağlayamazsınız.",
                "VKN değişikliğini hazırlayan kişi onaylayamaz veya reddedemez; başka bir yetkili karar vermelidir."),
            CommandResult.TaxIdUnchanged => new CommandResponseSnapshot(
                result,
                409,
                "VKN değişmiyor.",
                "Önerilen VKN şirketin mevcut VKN'si ile aynı."),
            CommandResult.Created => throw new ArgumentException(
                "Created cevabı oluşturulan kaydın kimliğini ve sürümünü içermelidir.",
                nameof(result)),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
        };
    }

    public static CommandResponseSnapshot Created(Guid id, long version)
    {
        return new CommandResponseSnapshot(
            CommandResult.Created,
            201,
            null,
            null,
            Created: new OrganizationCreatedRecord(id, version));
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
