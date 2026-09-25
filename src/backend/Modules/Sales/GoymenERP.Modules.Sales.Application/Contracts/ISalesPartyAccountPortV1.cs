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

/// <summary>
/// SALES-A2a (Business 5745076917 item 2): the Cari owner's contact information of one party and its
/// company account — the operational addresses and contacts a sales draft may name as its delivery
/// address and contact. Identities, the owner's versions, activity and presentation text only: no
/// channel, phone or e-mail crosses into Sales, and reading creates nothing. The central host
/// implements it over the authorized Counterparty contact-information read; the answer is the owner's
/// for the server-resolved company and the given account only.
/// </summary>
public interface ISalesPartyContactInformationPortV1
{
    Task<SalesPartyContactInformationAnswerV1> GetContactInformationAsync(Guid partyId, Guid companyAccountId,
        CancellationToken cancellationToken = default);
}

public enum SalesPartyContactInformationOutcomeV1
{
    /// <summary>The owner cannot answer (no provider, its own failure): fail-closed, never an empty list.</summary>
    Unavailable = 0,

    /// <summary>The server context is not the request's, or the owner refuses the read.</summary>
    Denied = 1,

    /// <summary>No such party, or the account is not that party's in this company.</summary>
    NotFound = 2,

    Found = 3,
}

public sealed record SalesPartyContactInformationAnswerV1(SalesPartyContactInformationOutcomeV1 Outcome,
    SalesPartyContactInformationV1? Information);

/// <summary>The owner's account-level version and its addresses and contacts (active and inactive; the consumer filters).</summary>
public sealed record SalesPartyContactInformationV1(Guid PartyId, Guid CompanyAccountId, Guid CompanyId, string AccountVersion,
    IReadOnlyList<SalesPartyAddressV1> Addresses, IReadOnlyList<SalesPartyContactV1> Contacts);

/// <summary>One operational address of the owner; <paramref name="Version"/> is the owner's text and is what a draft stores as the expected version.</summary>
public sealed record SalesPartyAddressV1(Guid Id, string Version, bool IsActive, string Purpose, string CountryCode,
    string Line1, string? Line2, string? City, string? District, string? PostalCode);

/// <summary>One operational contact person of the owner (no channels: a draft names the person, never a phone or e-mail).</summary>
public sealed record SalesPartyContactV1(Guid Id, string Version, bool IsActive, string DisplayName, string? JobTitle, string Purpose);
