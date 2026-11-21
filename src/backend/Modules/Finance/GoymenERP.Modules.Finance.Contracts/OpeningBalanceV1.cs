using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

// FIN-S9 (FNS-D23; FINANS_HAZIRLIK_PLANI_v1 API-14, manual entry): the cash, bank and party (cari) opening balance.
//
// FNS-D23: an opening balance is entered ONLY while the Settings "opening period" (açılış dönemi) is open. A Finance
// specialist proposes it, a Finance manager approves or rejects it, never its proposer (maker≠approver, person level, in
// the service and in a database check). When the period is closed the opening screen is not shown. The opening period
// key is owned by Settings; Finance only reads it through IFinanceOpeningPeriodReader. While no Settings adapter is
// registered, the default reader answers Unavailable and every proposal and approval fails closed.
//
// One live (proposed or approved) opening balance per (target, currency): a cash account and a bank account carry their
// own currency; a party opening names the (party, company account) pair, one Settings money currency candidate and a
// direction (Debit: the party owes the company; Credit: the company owes the party). The amount is exact decimal text
// greater than zero, never rounded, never converted.
//
// FNS-D86: AsOfDate is a Europe/Istanbul business date; a date after today's Istanbul business date is refused
// (ASSUMPTION-FIN-S9-ASOF: a future opening date is not entered), and when the business day cannot be resolved the
// proposal fails closed. The Settings period version a proposal (or a reversal proposal) is accepted under is written on
// the record; a decision taken when the period's version has changed since is refused (opening_period_version_changed).
//
// FNS-D84: approving an opening writes exactly ONE opening movement (finance.OpeningBalanceMovements, kind Opening) with
// the opening's target, currency, direction and exact amount on AsOfDate; a unique (company, opening, kind) key and the
// command's idempotency record keep it single. A party opening's movement appears in the party ledger with the "opening"
// (Açılış) entry kinds and counts in the party balance.
//
// FNS-D85: an approved opening is corrected by a reversal and a new proposal. A reversal is proposed under the propose
// authority and decided by another person under the approve authority (ASSUMPTION-FIN-S9-REVERSAL: the same two-person
// flow as the opening). Its approval writes one reversal movement (the reverse side, same amount and AsOfDate) and marks
// the opening Reversed, which frees the (target, currency) slot for the corrected proposal. Only while the period is open.
//
// NOT built (not confirmed, so not enabled by assumption): an open item for a party opening (allocation and risk against
// an opening), the treasury (cash/bank) balance effect of an opening movement, and the openings of portfolio
// cheques/notes, loans and DBS/collateral limits. A negative bank opening (overdraft) is refused: whether it is allowed is
// not decided.

[JsonConverter(typeof(JsonStringEnumConverter<OpeningBalanceTargetKind>))]
public enum OpeningBalanceTargetKind { CashAccount, BankAccount, PartyAccount }

[JsonConverter(typeof(JsonStringEnumConverter<OpeningBalanceDirection>))]
public enum OpeningBalanceDirection { Debit, Credit }

[JsonConverter(typeof(JsonStringEnumConverter<OpeningBalanceStatus>))]
public enum OpeningBalanceStatus { Proposed, Approved, Rejected, Reversed }

[JsonConverter(typeof(JsonStringEnumConverter<OpeningBalanceReversalStatus>))]
public enum OpeningBalanceReversalStatus { Proposed, Approved, Rejected }

// Open: entry allowed. Closed: the opening screen is not shown and every command is refused. Unavailable: the Settings key
// cannot be read (fail closed, as Closed for commands).
[JsonConverter(typeof(JsonStringEnumConverter<OpeningPeriodState>))]
public enum OpeningPeriodState { Unavailable, Open, Closed }

// A new opening balance proposal. TargetId: the cash account, the bank account or the company account (cari hesap).
// PartyId and Direction: only for a party opening (null otherwise). AsOfDate: the date the balance stands on.
public sealed record OpeningBalanceProposeRequest(OpeningBalanceTargetKind TargetKind, Guid TargetId, Guid? PartyId,
    Guid CurrencyId, OpeningBalanceDirection? Direction, string Amount, DateOnly AsOfDate);

public sealed record OpeningBalanceApproveRequest(Guid OpeningBalanceId, string ExpectedVersion);

public sealed record OpeningBalanceRejectRequest(Guid OpeningBalanceId, string ExpectedVersion, string Reason);

// HTTP bodies: the opening balance is the route's.
public sealed record OpeningBalanceApproveBody(string ExpectedVersion);

public sealed record OpeningBalanceRejectBody(string ExpectedVersion, string Reason);

// FNS-D85: a reversal proposal of an APPROVED opening. ExpectedVersion: the opening's version.
public sealed record OpeningBalanceReversalProposeRequest(Guid OpeningBalanceId, string ExpectedVersion, string Reason);

// The decision on the opening's pending reversal. ExpectedVersion: the reversal's version.
public sealed record OpeningBalanceReversalApproveRequest(Guid OpeningBalanceId, string ExpectedVersion);

public sealed record OpeningBalanceReversalRejectRequest(Guid OpeningBalanceId, string ExpectedVersion, string Reason);

// HTTP body of a reversal proposal: the opening balance is the route's. The decisions reuse the approve/reject bodies.
public sealed record OpeningBalanceReversalProposeBody(string ExpectedVersion, string Reason);

// The opening's latest reversal (pending, approved or rejected). OpeningPeriodVersion: the period version the reversal was
// proposed under. IsOwnProposal: the caller proposed the reversal. No actor id is disclosed.
public sealed record OpeningBalanceReversalView(
    Guid Id,
    OpeningBalanceReversalStatus Status,
    string Reason,
    string OpeningPeriodVersion,
    bool IsOwnProposal,
    DateTimeOffset ProposedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    string? RejectionReason,
    string Version);

// One opening balance. OpeningPeriodVersion: the Settings period version the proposal was accepted under (the decision's
// own period version is in its audit record). IsOwnProposal: the caller proposed it (the approve control is not offered on it). No actor id is
// disclosed.
public sealed record OpeningBalanceView(
    Guid Id,
    Guid CompanyId,
    OpeningBalanceTargetKind TargetKind,
    Guid TargetId,
    Guid? PartyId,
    Guid CurrencyId,
    OpeningBalanceDirection? Direction,
    string Amount,
    DateOnly AsOfDate,
    OpeningBalanceStatus Status,
    string OpeningPeriodVersion,
    bool IsOwnProposal,
    DateTimeOffset ProposedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    string? RejectionReason,
    string Version,
    OpeningBalanceReversalView? Reversal);

public sealed record OpeningBalanceListPage(Guid CompanyId, IReadOnlyList<OpeningBalanceView> Items);

public sealed record OpeningPeriodView(Guid CompanyId, OpeningPeriodState State, string? Version);

public sealed record OpeningBalanceCommandResult(CommandOutcome Outcome, OpeningBalanceView? Resource, bool IsReplay,
    string OriginalCorrelationId, IReadOnlyList<ValidationIssue> Issues);

public sealed record OpeningBalanceCurrencyCandidate(Guid CurrencyId, string Code, string DisplayLabel);

public sealed record OpeningBalanceCurrencyCandidates(Guid CompanyId,
    IReadOnlyList<OpeningBalanceCurrencyCandidate> Items);

// Every command checks fresh company/action authority, including a replay, and the Settings opening period.
public interface IFinanceOpeningBalanceService
{
    // FNS-D23: the specialist's proposal (propose authority).
    Task<OpeningBalanceCommandResult> ProposeAsync(FinanceContext context, CommandMetadata metadata,
        OpeningBalanceProposeRequest request, CancellationToken cancellationToken);

    // FNS-D23: the manager's decision (approve authority; never the proposer).
    Task<OpeningBalanceCommandResult> ApproveAsync(FinanceContext context, CommandMetadata metadata,
        OpeningBalanceApproveRequest request, CancellationToken cancellationToken);

    Task<OpeningBalanceCommandResult> RejectAsync(FinanceContext context, CommandMetadata metadata,
        OpeningBalanceRejectRequest request, CancellationToken cancellationToken);

    // FNS-D85: the reversal proposal of an approved opening (propose authority).
    Task<OpeningBalanceCommandResult> ProposeReversalAsync(FinanceContext context, CommandMetadata metadata,
        OpeningBalanceReversalProposeRequest request, CancellationToken cancellationToken);

    // FNS-D85: the decision on a pending reversal (approve authority; never the reversal's proposer).
    Task<OpeningBalanceCommandResult> ApproveReversalAsync(FinanceContext context, CommandMetadata metadata,
        OpeningBalanceReversalApproveRequest request, CancellationToken cancellationToken);

    Task<OpeningBalanceCommandResult> RejectReversalAsync(FinanceContext context, CommandMetadata metadata,
        OpeningBalanceReversalRejectRequest request, CancellationToken cancellationToken);

    // Under the read authority. The period state is answered even when it is closed (the screen shows only the notice).
    Task<ReadResult<OpeningPeriodView>> GetPeriodAsync(FinanceContext context, CancellationToken cancellationToken);

    // Under the read authority, only while the period is open: a closed period answers NotFound (the screen is not shown),
    // an unreadable one Unavailable.
    Task<ReadResult<OpeningBalanceListPage>> ListAsync(FinanceContext context, CancellationToken cancellationToken);

    // Under the propose authority.
    Task<ReadResult<OpeningBalanceCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext context,
        CancellationToken cancellationToken);
}

// ---- The consumer side of the Settings opening period key (FNS-D23; owner: Settings) ----

// State and the key's version as Settings answers it. Version: Settings' version text of the key (changes when the period
// is opened or closed); required when the state is Open or Closed.
public sealed record FinanceOpeningPeriodAnswer(OpeningPeriodState State, string? Version)
{
    public static FinanceOpeningPeriodAnswer NotAvailable { get; } = new(OpeningPeriodState.Unavailable, null);
}

// Implemented by a Settings adapter (the draft ISettingsFinancePolicyProviderV1.OpeningPeriodOpen, KIM-S19). Until one is
// registered the Finance default answers Unavailable: the period is never assumed open.
public interface IFinanceOpeningPeriodReader
{
    Task<FinanceOpeningPeriodAnswer> ReadAsync(Guid companyId, CancellationToken cancellationToken);
}
