namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// Cari-owned, authorized source for Manufacturing customer/tolling party-account references.
/// It is not a Manufacturing policy: the consumer still owns the immutable commercial-context
/// and owner/custodian rules. No identifier supplied by a caller is trusted without this source.
/// </summary>
public interface ICounterpartyManufacturingPartyAccountSourceV1
{
    Task<CounterpartyManufacturingPartyAccountSearchAnswerV1> SearchAuthorizedAsync(
        CounterpartyManufacturingPartyAccountSearchV1 query,
        CancellationToken cancellationToken = default);

    Task<CounterpartyManufacturingPartyAccountAnswerV1> GetAuthorizedAsync(
        Guid partyId,
        Guid companyAccountId,
        CancellationToken cancellationToken = default);
}

public enum CounterpartyManufacturingPartyAccountOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    NotFound = 2,
    Found = 3,
}

public sealed record CounterpartyManufacturingPartyAccountSearchV1(
    string? Query,
    int Limit = 25,
    string? Cursor = null);

public sealed record CounterpartyManufacturingPartyAccountSearchAnswerV1(
    CounterpartyManufacturingPartyAccountOutcomeV1 Outcome,
    IReadOnlyList<CounterpartyManufacturingPartyAccountCandidateV1> Items,
    string? NextCursor,
    bool HasMore);

public sealed record CounterpartyManufacturingPartyAccountAnswerV1(
    CounterpartyManufacturingPartyAccountOutcomeV1 Outcome,
    CounterpartyManufacturingPartyAccountV1? Account);

/// <summary>Owner facts only; both role flags are preserved independently.</summary>
public sealed record CounterpartyManufacturingPartyAccountV1(
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CompanyId,
    bool PartyIsActive,
    bool AccountIsActive,
    bool HasCustomerRole,
    bool HasTollingRole,
    string PartyVersion,
    string AccountVersion);

/// <summary>Candidate presentation plus the same immutable owner facts returned by exact validation.</summary>
public sealed record CounterpartyManufacturingPartyAccountCandidateV1(
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CompanyId,
    string Code,
    string LegalName,
    string? TradeName,
    bool PartyIsActive,
    bool AccountIsActive,
    bool HasCustomerRole,
    bool HasTollingRole,
    string PartyVersion,
    string AccountVersion);
