using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

// API-30: the Finance cash account (kasa) master. A definition only: it holds NO balance and NO opening
// balance and moves no money. It follows the bank-account master's shape (FS0): proposed by one actor,
// approved or rejected by a DIFFERENT actor (maker≠checker, held in the service and in the database).
//
// The currency is one of Settings' money currency candidates of the server-resolved company (FNS-D24:
// TL/USD/EUR are configured in Settings, never hard-coded here), checked exactly as the bank master checks it.
//
// FNS-D32: a cash account carries NO "responsible person" (sorumlu) field (the actor of each cash
// operation is already recorded); if one is ever wanted it is asked again in a separate slice.
//
// Passivation and reactivation (user decision on Issue #66, 13 November 2026, comments 6857132760 and
// 6857269313 — four rules):
//   1. Passivation is the same two-person flow as opening: one actor proposes it, a DIFFERENT actor
//      approves or rejects it (maker≠checker in the service and in the database).
//   2. A cash account whose balance is not zero cannot be passivated. There is no cash movement yet, so
//      the balance is asked through ONE seam (IFinanceCashAccountBalanceReader); today's only answer is
//      "no movement, zero". Whoever adds cash movements replaces that answer; an unknown balance refuses.
//   3. A passive account accepts no new movement; it stays readable. There is no movement command yet:
//      a future one MUST refuse unless CashAccountView.IsActive is true (Status == Approved).
//   4. Reactivation is the same two-person flow.
// A change proposal carries no reason, like an opening proposal; a rejection carries one. Not decided, so
// not built (NEEDS-SINAN): whether movements are held while a passivation is pending, and whether a
// reactivation re-checks the account's currency against today's Settings currencies.

public sealed record CashAccountProposeRequest(string DisplayName, Guid CurrencyId);

public sealed record CashAccountApproveRequest(Guid AccountId, string ExpectedVersion);

public sealed record CashAccountRejectRequest(Guid AccountId, string ExpectedVersion, string Reason);

// HTTP bodies: the account is the route's.
public sealed record CashAccountApprovalBody(string ExpectedVersion);

public sealed record CashAccountRejectionBody(string ExpectedVersion, string Reason);

// A state change of an account past its opening decision: the kind is the route's (and the command's).
[JsonConverter(typeof(JsonStringEnumConverter<CashAccountStateChange>))]
public enum CashAccountStateChange { Passivation, Reactivation }

public sealed record CashAccountStateChangeRequest(Guid AccountId, string ExpectedVersion);

public sealed record CashAccountStateChangeRejectRequest(Guid AccountId, string ExpectedVersion, string Reason);

// HTTP body of a state-change proposal: the account and the kind are the route's.
public sealed record CashAccountStateChangeBody(string ExpectedVersion);

[JsonConverter(typeof(JsonStringEnumConverter<CashAccountStatus>))]
public enum CashAccountStatus { Proposed, Approved, Rejected, Passive }

// IsActive: an approved account is active (usable as a definition, and the only state a cash movement may
// use); a proposal, a rejection or a passive account is not. PendingChange: the passivation or
// reactivation awaiting its decision, if any — the account's state does not move until it is approved.
public sealed record CashAccountView(
    Guid Id,
    Guid CompanyId,
    Guid CurrencyId,
    string DisplayName,
    CashAccountStatus Status,
    bool IsActive,
    string? RejectionReason,
    CashAccountStateChange? PendingChange,
    string Version);

public sealed record CashAccountCommandResult(CommandOutcome Outcome, CashAccountView? Resource,
    bool IsReplay, string OriginalCorrelationId, IReadOnlyList<ValidationIssue> Issues);

// Each account with whether the caller proposed it, so a screen offers the decision only to someone
// else; the same for a pending passivation or reactivation. The server refuses a self-decision regardless.
public sealed record CashAccountMasterItem(CashAccountView Account, bool IsOwnProposal, bool IsOwnPendingChange);

public sealed record CashAccountMasterPage(Guid CompanyId, IReadOnlyList<CashAccountMasterItem> Items);

// Settings' money currency candidates as given: no default, an empty list is "no candidates".
public sealed record CashAccountCurrencyCandidate(Guid CurrencyId, string Code, string DisplayLabel);

public sealed record CashAccountCurrencyCandidates(Guid CompanyId, IReadOnlyList<CashAccountCurrencyCandidate> Items);

// Every command checks fresh company/action authority, including a replay.
public interface IFinanceCashAccountMaster
{
    Task<CashAccountCommandResult> ProposeAsync(FinanceContext context, CommandMetadata metadata,
        CashAccountProposeRequest request, CancellationToken cancellationToken);

    Task<CashAccountCommandResult> ApproveAsync(FinanceContext context, CommandMetadata metadata,
        CashAccountApproveRequest request, CancellationToken cancellationToken);

    Task<CashAccountCommandResult> RejectAsync(FinanceContext context, CommandMetadata metadata,
        CashAccountRejectRequest request, CancellationToken cancellationToken);

    // Passivation or reactivation of an account, proposed by one actor (the kind's propose authority).
    // A passivation is refused while the balance is not zero.
    Task<CashAccountCommandResult> ProposeStateChangeAsync(FinanceContext context, CommandMetadata metadata,
        CashAccountStateChange change, CashAccountStateChangeRequest request, CancellationToken cancellationToken);

    // Approved by a DIFFERENT actor (the kind's approve authority): the account becomes passive or active.
    Task<CashAccountCommandResult> ApproveStateChangeAsync(FinanceContext context, CommandMetadata metadata,
        CashAccountStateChange change, CashAccountStateChangeRequest request, CancellationToken cancellationToken);

    // Rejected by a DIFFERENT actor with a reason (the kind's approve authority): the account is unchanged.
    Task<CashAccountCommandResult> RejectStateChangeAsync(FinanceContext context, CommandMetadata metadata,
        CashAccountStateChange change, CashAccountStateChangeRejectRequest request, CancellationToken cancellationToken);

    // Under the read authority. Proposals and rejections are listed only to a caller who may propose or
    // decide; a read-only caller sees approved and passive accounts.
    Task<ReadResult<CashAccountMasterPage>> ListAsync(FinanceContext context, CancellationToken cancellationToken);

    Task<ReadResult<CashAccountView>> GetAsync(FinanceContext context, Guid accountId, CancellationToken cancellationToken);

    // Under the propose authority.
    Task<ReadResult<CashAccountCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext context,
        CancellationToken cancellationToken);
}

// The balance of a cash account as far as passivation needs it: zero, not zero, or not known. No amount
// crosses this seam (a cash account master holds no money).
public enum CashAccountBalanceState { Unavailable, Zero, NotZero }

/// <summary>
/// The ONE seam through which the cash account master learns whether an account's balance is zero
/// (passivation rule 2). There is no cash movement in the system yet, so the registered implementation
/// answers <see cref="CashAccountBalanceState.Zero"/> for every account. The slice that adds cash
/// movements replaces that registration with the real reading; nothing else in the master changes.
/// The company is the server-resolved one; an implementation must read only its rows.
/// </summary>
public interface IFinanceCashAccountBalanceReader
{
    Task<CashAccountBalanceState> ReadAsync(Guid companyId, Guid cashAccountId, CancellationToken cancellationToken);
}
