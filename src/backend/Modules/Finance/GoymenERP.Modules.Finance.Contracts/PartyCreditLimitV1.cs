using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

// API-85a (F018 plan §L1; FNS-D27a/D45): the Finance party credit limit master. One limit per company
// account (cari hesap) of the server-resolved company, in ONE currency (a Settings money currency
// candidate, FNS-D24), as exact decimal text (> 0, never rounded, never converted). It moves no money.
//
// FNS-D45 (replaces FNS-D27's one-step path): EVERY change (set, change or removal) is a proposal by one
// person, decided by another: a holder of the PROPOSE authority proposes, a holder of the APPROVE
// authority approves someone else's proposal or rejects it with a reason. There is no one-step direct
// change, for any amount (no threshold). A proposer never decides their own proposal (service and
// database, binary collation), whatever authorities they hold. Each approval is audited with the old and
// the new value.
//
// History is append-only: each approved record supersedes the account's previous effective record, and
// the effective limit is the account's one approved record. At most one proposal is pending per account.
// An approved Remove means "no limit": the Sales risk port answers NoSource (finance_credit_limit_not_set),
// never zero and never unlimited.
//
// NOT built (open): the real amount/role/currency approval matrix (OPEN-009 stays open; FNS-D45 answers
// only the credit limit's approval step) and whether Cari stores the limit (F018 N-2): Finance owns it and
// Cari only shows it, as SAT-D51 says.

[JsonConverter(typeof(JsonStringEnumConverter<PartyCreditLimitKind>))]
public enum PartyCreditLimitKind { Set, Remove }

[JsonConverter(typeof(JsonStringEnumConverter<PartyCreditLimitStatus>))]
public enum PartyCreditLimitStatus { Proposed, Approved, Rejected, Superseded }

// ExpectedRevision is the ACCOUNT's limit revision: "0" while the account never had an approved record,
// then the revision of its latest approved record (see PartyCreditLimitAccountView.Revision).
// A Set proposal names the currency and the amount; a Remove proposal names neither (both null).
public sealed record PartyCreditLimitProposeRequest(Guid PartyId, Guid CompanyAccountId, PartyCreditLimitKind Kind,
    Guid? CurrencyId, string? Amount, string ExpectedRevision);

// A decision names the proposal and the proposal's own version.
public sealed record PartyCreditLimitApproveRequest(Guid ProposalId, string ExpectedVersion);

public sealed record PartyCreditLimitRejectRequest(Guid ProposalId, string ExpectedVersion, string Reason);

// HTTP bodies: the proposal is the route's.
public sealed record PartyCreditLimitApprovalBody(string ExpectedVersion);

public sealed record PartyCreditLimitRejectionBody(string ExpectedVersion, string Reason);

// One record of an account's limit history; every record is a proposal decided by another person (FNS-D45).
// IsDirect is always false since FNS-D45 closed the one-step path (the database refuses a direct record,
// CK_PartyCreditLimits_Decision); the member stays so the V1 shape and stored command results keep reading.
// Revision: the account revision an approved record carries (null for a proposal never approved). No actor
// id is disclosed; a screen learns only whether a proposal is the caller's own.
public sealed record PartyCreditLimitRecordView(
    Guid Id,
    Guid CompanyId,
    Guid PartyId,
    Guid CompanyAccountId,
    PartyCreditLimitKind Kind,
    Guid? CurrencyId,
    string? Amount,
    PartyCreditLimitStatus Status,
    bool IsDirect,
    string? Revision,
    string? RejectionReason,
    DateTimeOffset ProposedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    DateTimeOffset? SupersededAtUtc,
    string Version);

// The account's effective record (an approved Set or Remove; null when none was ever approved), its
// pending proposal (shown only to a caller who may propose or approve), the revision the next proposal
// must name, and whether the pending proposal is the caller's own.
public sealed record PartyCreditLimitAccountView(
    Guid CompanyId,
    Guid PartyId,
    Guid CompanyAccountId,
    PartyCreditLimitRecordView? Effective,
    PartyCreditLimitRecordView? Pending,
    string Revision,
    bool IsOwnPending);

public sealed record PartyCreditLimitHistoryPage(Guid CompanyId, Guid CompanyAccountId,
    IReadOnlyList<PartyCreditLimitRecordView> Items);

// The list: every account with an effective record or (for a proposer/approver) a pending proposal.
public sealed record PartyCreditLimitListItem(PartyCreditLimitAccountView Account);

public sealed record PartyCreditLimitListPage(Guid CompanyId, IReadOnlyList<PartyCreditLimitListItem> Items);

public sealed record PartyCreditLimitCommandResult(CommandOutcome Outcome, PartyCreditLimitRecordView? Resource,
    bool IsReplay, string OriginalCorrelationId, IReadOnlyList<ValidationIssue> Issues);

// Settings' money currency candidates as given: no default, an empty list is "no candidates".
public sealed record PartyCreditLimitCurrencyCandidate(Guid CurrencyId, string Code, string DisplayLabel);

public sealed record PartyCreditLimitCurrencyCandidates(Guid CompanyId, IReadOnlyList<PartyCreditLimitCurrencyCandidate> Items);

// Every command checks fresh company/action authority, including a replay.
public interface IFinancePartyCreditLimitMaster
{
    // FNS-D45: propose authority, for a set, a change and a removal alike; the effective limit does not
    // change until a different actor approves.
    Task<PartyCreditLimitCommandResult> ProposeAsync(FinanceContext context, CommandMetadata metadata,
        PartyCreditLimitProposeRequest request, CancellationToken cancellationToken);

    // FNS-D45: approve authority, on someone else's proposal only.
    Task<PartyCreditLimitCommandResult> ApproveAsync(FinanceContext context, CommandMetadata metadata,
        PartyCreditLimitApproveRequest request, CancellationToken cancellationToken);

    Task<PartyCreditLimitCommandResult> RejectAsync(FinanceContext context, CommandMetadata metadata,
        PartyCreditLimitRejectRequest request, CancellationToken cancellationToken);

    // Under the read authority. Pending proposals are listed only to a caller who may propose or approve.
    Task<ReadResult<PartyCreditLimitListPage>> ListAsync(FinanceContext context, CancellationToken cancellationToken);

    Task<ReadResult<PartyCreditLimitAccountView>> GetAccountAsync(FinanceContext context, Guid companyAccountId,
        CancellationToken cancellationToken);

    // Approved and superseded approved records to every reader; proposals, rejections and superseded
    // proposals only to a caller who may propose or approve.
    Task<ReadResult<PartyCreditLimitHistoryPage>> HistoryAsync(FinanceContext context, Guid companyAccountId,
        CancellationToken cancellationToken);

    // Under the propose or approve authority.
    Task<ReadResult<PartyCreditLimitCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext context,
        CancellationToken cancellationToken);
}
