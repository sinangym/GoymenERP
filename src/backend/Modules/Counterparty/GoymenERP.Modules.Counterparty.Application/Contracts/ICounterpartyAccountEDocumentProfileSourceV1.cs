namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// SAT-D102 / SAT-D119: the Cari owner's answer for "how is this account's customer invoiced electronically, and with
/// which e-Fatura scenario on this date?" — read by consumers through this versioned port, never from the Counterparty
/// tables. The profile is the customer's VKN-shared record (SAT-D119): the port resolves the account to its party's
/// VKN and returns the one group-level profile. The intended consumer is the Sales invoice draft (API-32, S5
/// follow-up), which must refuse to prepare an e-Fatura draft until the status is known and the scenario is Resolved.
/// <list type="bullet">
/// <item><b>Found</b>: the customer's VKN-shared status (Unknown is explicit — "not checked" or no VKN yet, never
/// inferred), the check's time and checker, the override, and the effective scenario resolved for <c>effectiveOn</c>:
/// the override if present and listed in the Settings code list in force, else the Settings default, else Unresolved
/// (the Settings default starts EMPTY, SAT-D102). <c>TaxIdentityNumberPresent</c> is false when the party has no VKN:
/// no profile exists, so the status is Unknown and the scenario is Unresolved (fail-closed). Settings that could not
/// be read make the scenario <c>Unavailable</c>, never a guess.</item>
/// <item><b>NotFound</b>: no such account in the requested company.</item>
/// <item><b>Denied</b>: the server context is not the requested company, the actor is not authenticated, or the actor
/// lacks the company-wide <c>counterparty.parties.read</c>.</item>
/// <item><b>Unavailable</b>: the owner could not answer (database or any other failure). A consumer treats Denied and
/// Unavailable as "could not be read" — fail-closed.</item>
/// </list>
/// The scenario is resolved whatever the status is; whether it applies (an e-Arşiv customer has no e-Fatura scenario)
/// is the consumer's rule. The port writes nothing (no audit, idempotency or outbox) and grants no authority.
/// </summary>
public interface ICounterpartyAccountEDocumentProfileSourceV1
{
    Task<CounterpartyAccountEDocumentProfileAnswerV1> GetAuthorizedAsync(
        Guid companyId,
        Guid companyAccountId,
        DateOnly effectiveOn,
        CancellationToken cancellationToken = default);
}

public enum CounterpartyAccountEDocumentProfileOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    NotFound = 2,
    Found = 3,
}

public sealed record CounterpartyAccountEDocumentProfileAnswerV1(
    CounterpartyAccountEDocumentProfileOutcomeV1 Outcome,
    CounterpartyAccountEDocumentProfileV1? Account);

/// <summary>Owner facts plus the effective scenario for <c>EffectiveOn</c>.</summary>
public sealed record CounterpartyAccountEDocumentProfileV1(
    Guid CompanyId,
    Guid CompanyAccountId,
    Guid PartyId,
    bool AccountIsActive,
    bool TaxIdentityNumberPresent,
    CounterpartyEDocumentStatusV1 Status,
    DateTimeOffset? StatusCheckedAtUtc,
    string? StatusCheckedByUserId,
    string? ScenarioOverrideCode,
    DateOnly EffectiveOn,
    CounterpartyEInvoiceScenarioResolutionV1 EffectiveScenario,
    string AccountVersion);
