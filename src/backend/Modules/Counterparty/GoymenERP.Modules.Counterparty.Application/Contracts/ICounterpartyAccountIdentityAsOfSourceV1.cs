using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// KLN-D38 (4): the Cari owner's answer for "which VKN/TCKN, legal name and e-belge status did this account's party carry
/// on the document date?" — read by consumers (the Sales and Purchasing invoice drafts) through this versioned port, never
/// from the Counterparty tables.
/// <list type="bullet">
/// <item><b>Found</b>: the identity valid on <c>DocumentDate</c>. Until a party's first dated change its current values
/// apply "since always". The e-belge status is the status of the VKN valid on that date: the dated status history when it
/// exists, otherwise the current status only when it was checked on or before the document date, else
/// <c>Unknown</c> ("not checked", fail-closed). <c>SucceededByPartyId</c> / <c>SucceededOn</c> say that a NEW legal person
/// took over this cari in the requested company (rule 2); the consumer decides what a document dated on or after that day
/// may do — the port never redirects the account.</item>
/// <item><b>NotFound</b>: no such account in the requested company.</item>
/// <item><b>Denied</b>: the server context is not the requested company, the actor is not authenticated, or the actor
/// lacks the company-wide <c>counterparty.parties.read</c>.</item>
/// <item><b>Unavailable</b>: the owner could not answer, including a dated history that has no value for that date
/// (never guessed). A consumer treats Denied and Unavailable as "could not be read" — fail-closed.</item>
/// </list>
/// The port writes nothing (no audit, idempotency or outbox) and grants no authority. OPEN-030 (company VKN) is out of scope.
/// </summary>
public interface ICounterpartyAccountIdentityAsOfSourceV1
{
    Task<CounterpartyAccountIdentityAsOfAnswerV1> GetAuthorizedAsync(
        Guid companyId,
        Guid companyAccountId,
        DateOnly documentDate,
        CancellationToken cancellationToken = default);
}

public enum CounterpartyAccountIdentityAsOfOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    NotFound = 2,
    Found = 3,
}

public sealed record CounterpartyAccountIdentityAsOfAnswerV1(
    CounterpartyAccountIdentityAsOfOutcomeV1 Outcome,
    CounterpartyAccountIdentityAsOfV1? Identity);

/// <summary>
/// The identity of the account's party on <c>DocumentDate</c>. <c>TaxIdentityNumber</c> is null when the party had none on
/// that date. <c>AccountIsActive</c> is the account's state NOW (not on the document date).
/// </summary>
public sealed record CounterpartyAccountIdentityAsOfV1(
    Guid CompanyId,
    Guid CompanyAccountId,
    Guid PartyId,
    PartyKind PartyKind,
    bool AccountIsActive,
    DateOnly DocumentDate,
    string? TaxIdentityNumber,
    string LegalName,
    CounterpartyEDocumentStatusV1 EDocumentStatus,
    Guid? SucceededByPartyId,
    DateOnly? SucceededOn);
