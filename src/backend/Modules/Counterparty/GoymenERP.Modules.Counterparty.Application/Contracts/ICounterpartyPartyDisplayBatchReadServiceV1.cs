namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// Cari-owned, company-scoped presentation lookup for consumers that already hold Party ids.
/// The request identities bind the call to the current server context; they never override it.
/// Missing, foreign and inactive records are indistinguishable omissions.
/// </summary>
public interface ICounterpartyPartyDisplayBatchReadServiceV1
{
    Task<CounterpartyPartyDisplayBatchReadAnswerV1> ReadAuthorizedAsync(
        CounterpartyPartyDisplayBatchReadRequestV1 request,
        CancellationToken cancellationToken = default);
}

public enum CounterpartyPartyDisplayBatchReadOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    Found = 2,
}

public sealed record CounterpartyPartyDisplayBatchReadRequestV1(
    Guid CompanyId,
    string ActorId,
    IReadOnlyList<Guid> PartyIds);

public sealed record CounterpartyPartyDisplayBatchReadAnswerV1(
    CounterpartyPartyDisplayBatchReadOutcomeV1 Outcome,
    IReadOnlyList<CounterpartyPartyDisplayV1> Items);

public sealed record CounterpartyPartyDisplayV1(
    Guid PartyId,
    string Code,
    string LegalName,
    string? TradeName);
