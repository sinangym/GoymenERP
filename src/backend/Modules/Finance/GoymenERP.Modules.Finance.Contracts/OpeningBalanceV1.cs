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
// NOT built (FNS-D23 limits; not confirmed, so not enabled by assumption): the money/ledger effect of an approved opening
// (a treasury balance, a party ledger line or an open item), marking a record with an "opening" type, correcting an
// opening by a reversal, and the openings of portfolio cheques/notes, loans and DBS/collateral limits. An approved opening
// balance is recorded and audited here and moves no money yet. A negative bank opening (overdraft) is refused: whether it
// is allowed is not decided.

[JsonConverter(typeof(JsonStringEnumConverter<OpeningBalanceTargetKind>))]
public enum OpeningBalanceTargetKind { CashAccount, BankAccount, PartyAccount }

[JsonConverter(typeof(JsonStringEnumConverter<OpeningBalanceDirection>))]
public enum OpeningBalanceDirection { Debit, Credit }

[JsonConverter(typeof(JsonStringEnumConverter<OpeningBalanceStatus>))]
public enum OpeningBalanceStatus { Proposed, Approved, Rejected }

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
    string Version);

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
