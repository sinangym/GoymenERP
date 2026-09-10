using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;

using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

public interface ICounterpartyCommandService
{
    Task<CounterpartyCommandExecution> CreateAsync(
        CreateCounterparty command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken);

    Task<CounterpartyCommandExecution> SetCompanyAccountActiveStateAsync(
        Guid partyId,
        SetCompanyPartyAccountActiveState command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken);
}

public interface ICounterpartyCompanyAccountLinkService
{
    Task<CounterpartyCommandExecution> CreateCompanyAccountAsync(
        Guid partyId,
        CreateCompanyPartyAccount command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken);
}

public sealed record CreateCounterparty(
    PartyKind Kind,
    string LegalName,
    string? TradeName = null,
    PartyRole Roles = PartyRole.None);

public sealed record CreateCompanyPartyAccount(
    PartyRole Roles = PartyRole.None);

public sealed record SetCompanyPartyAccountActiveState(
    [property: JsonRequired] bool IsActive,
    [property: JsonRequired] long ExpectedVersion,
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
    AlreadyLinked = 6,
}

public static class CounterpartyProblemCodesV1
{
    public const string ValidationFailed = "validation_failed";

    public const string CommandMetadataInvalid = "command_metadata_invalid";

    public const string InvalidCursor = "invalid_cursor";

    public const string NotFound = "not_found";

    public const string AlreadyLinked = "already_linked";

    public const string VersionConflict = "version_conflict";

    public const string IdempotencyConflict = "idempotency_conflict";

    public static string RequiredForProblem(CounterpartyCommandResult result)
    {
        return result switch
        {
            CounterpartyCommandResult.NotFound => NotFound,
            CounterpartyCommandResult.VersionConflict => VersionConflict,
            CounterpartyCommandResult.AlreadyLinked => AlreadyLinked,
            CounterpartyCommandResult.IdempotencyConflict => IdempotencyConflict,
            _ => throw new InvalidOperationException(
                $"Cari terminal sonucu stable ProblemDetails koduyla eşleşmiyor: {result}."),
        };
    }
}

public sealed record CounterpartyVersionConflict(
    long ExpectedVersion,
    long CurrentVersion);

public sealed record CounterpartyCommandResponse(
    CounterpartyCommandResult Result,
    int StatusCode,
    string? Title,
    string? Detail,
    PartyCoreView? Resource,
    CounterpartyVersionConflict? Conflict);

public sealed record CounterpartyCommandExecution(
    CounterpartyCommandResponse Response,
    bool IsReplay,
    string OriginalCorrelationId);
