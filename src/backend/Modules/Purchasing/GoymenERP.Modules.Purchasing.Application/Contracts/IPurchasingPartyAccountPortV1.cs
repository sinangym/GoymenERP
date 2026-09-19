namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// The Cari owner's answer about the order's supplier at command time (PUR-4a; the Logistics LOG-4 D4
/// shape): does this company account belong to this party, is it usable in the server-resolved company,
/// and does the account carry the Supplier role? A consumer adapter boundary over the authorized
/// Counterparty party read (the central host implements it, as it does the Sales, Finance and Logistics
/// party ports); not a second catalogue. Identities, flags and the owner's versions only; nothing of the
/// master is copied.
/// </summary>
public interface IPurchasingPartyAccountPortV1
{
    Task<PurchasingPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default);
}

public enum PurchasingPartyAccountOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    NotFound = 2,
    Found = 3,
}

public sealed record PurchasingPartyAccountAnswerV1(PurchasingPartyAccountOutcomeV1 Outcome, PurchasingPartyAccountV1? Account);

/// <param name="AccountHasSupplierRole">The Cari account's Supplier role flag as the owner stores it; a party without it is not a supplier here (Business: no guessing).</param>
public sealed record PurchasingPartyAccountV1(Guid PartyId, Guid CompanyAccountId, Guid CompanyId,
    bool PartyIsActive, bool AccountIsActive, bool AccountHasSupplierRole, string PartyVersion, string AccountVersion);
