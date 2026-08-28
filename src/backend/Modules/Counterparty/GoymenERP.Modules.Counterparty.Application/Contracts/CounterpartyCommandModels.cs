using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

public sealed record CreateCounterpartyCommand(
    PartyKind Kind,
    string LegalName,
    string? TradeName,
    PartyRole InitialRoles,
    string? ExternalIntegrationCode);

public sealed record SetCompanyPartyAccountRolesCommand(
    PartyRole Roles,
    long ExpectedVersion);

public sealed record SetCompanyPartyAccountExternalCodeCommand(
    string? ExternalIntegrationCode,
    long ExpectedVersion);

public sealed record SetCompanyPartyAccountActiveStateCommand(
    bool IsActive,
    long ExpectedVersion,
    string Reason);

public sealed record CounterpartyCommandMetadata(
    Guid CompanyId,
    string ActorId,
    string CorrelationId,
    string? CausationId,
    string SourceChannel,
    string? ClientIpAddress,
    string? UserAgent,
    string IdempotencyKey);

public enum CounterpartyCommandResult
{
    Created = 1,
    Succeeded = 2,
    NotFound = 3,
    VersionConflict = 4,
    IdempotencyConflict = 5,
}

public sealed record CounterpartyCreatedSnapshot(
    Guid PartyId,
    string PartyCode,
    Guid CompanyPartyAccountId,
    long PartyVersion,
    long CompanyPartyAccountVersion);

public sealed record CounterpartyVersionConflictSnapshot(
    long ExpectedVersion,
    long CurrentVersion);

public sealed record CounterpartyCommandResponseSnapshot(
    CounterpartyCommandResult Result,
    int StatusCode,
    string? Title,
    string? Detail,
    CounterpartyCreatedSnapshot? Created = null,
    CounterpartyVersionConflictSnapshot? Conflict = null);

public sealed record CounterpartyCommandExecutionResult(
    CounterpartyCommandResponseSnapshot Response,
    bool IsReplay,
    string OriginalCorrelationId)
{
    public CounterpartyCommandResult Result => Response.Result;
}

public static class CounterpartyCommandResponses
{
    public static CounterpartyCommandResponseSnapshot Created(
        CounterpartyCreatedSnapshot created) =>
        new(
            CounterpartyCommandResult.Created,
            201,
            null,
            null,
            created);

    public static CounterpartyCommandResponseSnapshot For(
        CounterpartyCommandResult result)
    {
        return result switch
        {
            CounterpartyCommandResult.Succeeded => new(
                result,
                204,
                null,
                null),
            CounterpartyCommandResult.NotFound => new(
                result,
                404,
                "Kayıt bulunamadı.",
                "İstenen cari kayıt bulunamadı veya bu şirket bağlamında kullanılamıyor."),
            CounterpartyCommandResult.IdempotencyConflict => new(
                result,
                409,
                "Idempotency-Key çakışması.",
                "Aynı Idempotency-Key farklı bir komut içeriği veya kullanıcıyla tekrar kullanılamaz."),
            CounterpartyCommandResult.Created => throw new ArgumentException(
                "Created cevabı oluşturulan kayıt bilgisini içermelidir.",
                nameof(result)),
            CounterpartyCommandResult.VersionConflict => throw new ArgumentException(
                "Version conflict cevabı beklenen ve mevcut sürümü içermelidir.",
                nameof(result)),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
        };
    }

    public static CounterpartyCommandResponseSnapshot VersionConflict(
        long expectedVersion,
        long currentVersion) =>
        new(
            CounterpartyCommandResult.VersionConflict,
            409,
            "Kayıt güncellendi.",
            "Cari hesap başka bir işlem tarafından değiştirildi; veriyi yenileyip tekrar deneyin.",
            Conflict: new CounterpartyVersionConflictSnapshot(
                expectedVersion,
                currentVersion));
}
