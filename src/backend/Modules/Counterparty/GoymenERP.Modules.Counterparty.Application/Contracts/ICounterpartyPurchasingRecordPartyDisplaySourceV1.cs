namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// Cari-owned projection for a central Purchasing adapter that has already authorized the Purchasing records
/// from which every requested Party id was obtained. This is not a general Cari read, search, list,
/// or card contract and it must never accept arbitrary client-provided Party ids. Same-company historical
/// records remain displayable after Party or account deactivation; this contract grants no selection or
/// new-transaction eligibility.
/// </summary>
public interface ICounterpartyPurchasingRecordPartyDisplaySourceV1
{
    Task<CounterpartyPurchasingRecordPartyDisplayAnswerV1> ReadForAuthorizedPurchasingRecordsAsync(
        CounterpartyPurchasingRecordPartyDisplayRequestV1 request,
        CancellationToken cancellationToken = default);
}

public enum CounterpartyPurchasingRecordPartyDisplayOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    Found = 2,
}

/// <summary>
/// CompanyId and ActorId are copies of the server-resolved context, never caller overrides.
/// PartyIds must be the distinct ids extracted from already-authorized Purchasing records.
/// </summary>
public sealed record CounterpartyPurchasingRecordPartyDisplayRequestV1(
    Guid CompanyId,
    string ActorId,
    IReadOnlyList<Guid> PartyIds);

public sealed record CounterpartyPurchasingRecordPartyDisplayAnswerV1(
    CounterpartyPurchasingRecordPartyDisplayOutcomeV1 Outcome,
    IReadOnlyList<CounterpartyPurchasingRecordPartyDisplayV1> Items);

public sealed record CounterpartyPurchasingRecordPartyDisplayV1(
    Guid PartyId,
    string Code,
    string LegalName,
    string? TradeName);
