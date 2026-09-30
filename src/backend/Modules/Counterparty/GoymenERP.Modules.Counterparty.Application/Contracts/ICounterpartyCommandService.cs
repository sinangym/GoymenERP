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

    /// <summary>AT-009: the party already has a number; replacing it is a change request (409).</summary>
    TaxIdentityChangeRequiresApproval = 7,

    /// <summary>AT-009: a change was proposed for a party that has no number yet (409).</summary>
    TaxIdentityNotSet = 8,

    /// <summary>AT-009: the proposal equals the current number (409).</summary>
    TaxIdentityUnchanged = 9,

    /// <summary>AT-009: a change of this party is already pending in this company (409).</summary>
    TaxIdentityChangePending = 10,

    /// <summary>AT-009: the named request is no longer pending (409).</summary>
    TaxIdentityChangeNotPending = 11,

    /// <summary>AT-009: the party's number moved after the request was made; it cannot be approved (409).</summary>
    TaxIdentityChangeStale = 12,

    /// <summary>AT-009: the proposer tried to decide their own request (409, maker-checker).</summary>
    TaxIdentitySelfDecision = 13,
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

    /// <summary>AT-009: the number fails the VKN/TCKN format or check digits (400, never stored).</summary>
    public const string TaxIdentityInvalid = "tax_identity_invalid";

    public const string TaxIdentityChangeRequiresApproval = "tax_identity_change_requires_approval";

    public const string TaxIdentityNotSet = "tax_identity_not_set";

    public const string TaxIdentityUnchanged = "tax_identity_unchanged";

    public const string TaxIdentityChangePending = "tax_identity_change_pending";

    public const string TaxIdentityChangeNotPending = "tax_identity_change_not_pending";

    public const string TaxIdentityChangeStale = "tax_identity_change_stale";

    public const string TaxIdentitySelfDecision = "tax_identity_self_decision";

    public static string RequiredForProblem(CounterpartyCommandResult result)
    {
        return result switch
        {
            CounterpartyCommandResult.NotFound => NotFound,
            CounterpartyCommandResult.VersionConflict => VersionConflict,
            CounterpartyCommandResult.AlreadyLinked => AlreadyLinked,
            CounterpartyCommandResult.IdempotencyConflict => IdempotencyConflict,
            CounterpartyCommandResult.TaxIdentityChangeRequiresApproval => TaxIdentityChangeRequiresApproval,
            CounterpartyCommandResult.TaxIdentityNotSet => TaxIdentityNotSet,
            CounterpartyCommandResult.TaxIdentityUnchanged => TaxIdentityUnchanged,
            CounterpartyCommandResult.TaxIdentityChangePending => TaxIdentityChangePending,
            CounterpartyCommandResult.TaxIdentityChangeNotPending => TaxIdentityChangeNotPending,
            CounterpartyCommandResult.TaxIdentityChangeStale => TaxIdentityChangeStale,
            CounterpartyCommandResult.TaxIdentitySelfDecision => TaxIdentitySelfDecision,
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
