namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// The Cari owner's answer about one customer at command time: does this company account belong to
/// this party, and is it usable in the server-resolved company? A consumer adapter boundary over
/// the authorized Counterparty party read (the central host implements it, as it does the Finance
/// party port); not a second catalogue. Every value is the owner's; the draft stores the identities
/// and the owner's versions, never a copy of the master.
/// </summary>
public interface ISalesPartyAccountPortV1
{
    Task<SalesPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId,
        CancellationToken cancellationToken = default);
}

public enum SalesPartyAccountOutcomeV1
{
    /// <summary>The owner cannot answer (no provider, its own failure): fail-closed, never a default.</summary>
    Unavailable = 0,

    /// <summary>The server context is not the request's, or the owner refuses the read.</summary>
    Denied = 1,

    /// <summary>No such party, or no such account of that party in this company.</summary>
    NotFound = 2,

    Found = 3,
}

public sealed record SalesPartyAccountAnswerV1(SalesPartyAccountOutcomeV1 Outcome, SalesPartyAccountV1? Account);

/// <summary>The owner's identities and state for the customer; versions are the owner's text.</summary>
public sealed record SalesPartyAccountV1(Guid PartyId, Guid CompanyAccountId, Guid CompanyId,
    bool PartyIsActive, bool AccountIsActive, string PartyVersion, string AccountVersion);
