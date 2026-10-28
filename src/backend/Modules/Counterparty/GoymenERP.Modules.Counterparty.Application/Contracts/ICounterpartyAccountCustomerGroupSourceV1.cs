namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// SAT-D57 / SAT-D58: the Cari owner's answer for "which customer group is this company account in?" — for the price
/// chain's group tier (MOD-02), read by consumers through this versioned port, never from the Counterparty tables.
/// <list type="bullet">
/// <item><b>Found</b> with <c>CustomerGroupId = null</c>: the account has no group; the group tier is skipped.</item>
/// <item><b>Found</b> with a group: its id and its active flag. A PASSIVE group's existing assignment is a usable
/// Found group: under SAT-D77 (Sinan, 2026-10-28, EK12) an account already in a group that is later made passive keeps
/// getting that group's price; passive only closes the group to NEW assignments, which the assign command enforces.
/// <c>CustomerGroupIsActive = false</c> is therefore information, never a reason to skip the tier or to answer
/// "could not be read".</item>
/// <item><b>NotFound</b>: no such account in the requested company.</item>
/// <item><b>Denied</b>: the server context is not the requested company, the actor is not authenticated, or the actor
/// lacks the company-wide <c>counterparty.parties.read</c>.</item>
/// <item><b>Unavailable</b>: the owner could not answer (database or any other failure). A consumer must treat Denied
/// and Unavailable as "could not be read" — fail-closed, never as "no group" (SAT-D58).</item>
/// </list>
/// The port writes nothing (no audit, idempotency or outbox) and grants no authority.
/// </summary>
public interface ICounterpartyAccountCustomerGroupSourceV1
{
    Task<CounterpartyAccountCustomerGroupAnswerV1> GetAuthorizedAsync(
        Guid companyId,
        Guid companyAccountId,
        CancellationToken cancellationToken = default);
}

public enum CounterpartyAccountCustomerGroupOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    NotFound = 2,
    Found = 3,
}

public sealed record CounterpartyAccountCustomerGroupAnswerV1(
    CounterpartyAccountCustomerGroupOutcomeV1 Outcome,
    CounterpartyAccountCustomerGroupV1? Account);

/// <summary>
/// Owner facts only. <c>CustomerGroupIsActive</c> is null exactly when <c>CustomerGroupId</c> is null; false marks a
/// passive group whose existing assignment still prices (SAT-D77).
/// </summary>
public sealed record CounterpartyAccountCustomerGroupV1(
    Guid CompanyId,
    Guid CompanyAccountId,
    Guid PartyId,
    bool AccountIsActive,
    Guid? CustomerGroupId,
    bool? CustomerGroupIsActive,
    string AccountVersion);
