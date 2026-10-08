namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// Cari-owned projection for a central Sales adapter that has already authorized the Sales records
/// from which every requested Party id was obtained. This is not a general Cari read, search, list,
/// or card contract and it must never accept arbitrary client-provided Party ids.
/// </summary>
public interface ICounterpartySalesRecordPartyDisplaySourceV1
{
    Task<CounterpartySalesRecordPartyDisplayAnswerV1> ReadForAuthorizedSalesRecordsAsync(
        CounterpartySalesRecordPartyDisplayRequestV1 request,
        CancellationToken cancellationToken = default);
}

public enum CounterpartySalesRecordPartyDisplayOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    Found = 2,
}

/// <summary>
/// CompanyId and ActorId are copies of the server-resolved context, never caller overrides.
/// PartyIds must be the distinct ids extracted from already-authorized Sales records.
/// </summary>
public sealed record CounterpartySalesRecordPartyDisplayRequestV1(
    Guid CompanyId,
    string ActorId,
    IReadOnlyList<Guid> PartyIds);

public sealed record CounterpartySalesRecordPartyDisplayAnswerV1(
    CounterpartySalesRecordPartyDisplayOutcomeV1 Outcome,
    IReadOnlyList<CounterpartySalesRecordPartyDisplayV1> Items);

public sealed record CounterpartySalesRecordPartyDisplayV1(
    Guid PartyId,
    string Code,
    string LegalName,
    string? TradeName);
