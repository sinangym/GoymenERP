using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

// API-85a (F018 plan §L1; FNS-D27/D27a): the Finance party credit limit master. One limit per company
// account (cari hesap) of the server-resolved company, in ONE currency (a Settings money currency
// candidate, FNS-D24), as exact decimal text (> 0, never rounded, never converted). It moves no money.
//
// FNS-D27: a holder of the APPROVE authority sets, changes or removes a limit in ONE step: the record is
// approved at once and audited with its old and new value. FNS-D27a: a holder of only the PROPOSE
// authority proposes; a holder of the approve authority approves the proposal or rejects it with a
// reason. A proposer never decides their own proposal (service and database, binary collation).
// FNS-D56 and FNS-D57(3), the newer user decisions, keep this one-step change (no threshold, no second
// approval); the two-person flow of FNS-D45 is not applied.
//
// History is append-only: each approved record supersedes the account's previous effective record, and
// the effective limit is the account's one approved record. At most one proposal is pending per account;
// a direct change by an approver supersedes it. An approved Remove means "no limit": the Sales risk port
// answers NoSource (finance_credit_limit_not_set), never zero and never unlimited.
//
// NOT built: a threshold or a second approval (FNS-D56/FNS-D57(4): a later, separate decision) and whether Cari
// stores the limit (F018 N-2): Finance owns it and Cari only shows it, as SAT-D51 says.

[JsonConverter(typeof(JsonStringEnumConverter<PartyCreditLimitKind>))]
public enum PartyCreditLimitKind { Set, Remove }

[JsonConverter(typeof(JsonStringEnumConverter<PartyCreditLimitStatus>))]
public enum PartyCreditLimitStatus { Proposed, Approved, Rejected, Superseded }

// ExpectedRevision is the ACCOUNT's limit revision: "0" while the account never had an approved record,
// then the revision of its latest approved record (see PartyCreditLimitAccountView.Revision).
public sealed record PartyCreditLimitSetRequest(Guid PartyId, Guid CompanyAccountId, Guid CurrencyId, string Amount,
    string ExpectedRevision);

public sealed record PartyCreditLimitRemoveRequest(Guid PartyId, Guid CompanyAccountId, string ExpectedRevision);

// A Set proposal names the currency and the amount; a Remove proposal names neither (both null).
public sealed record PartyCreditLimitProposeRequest(Guid PartyId, Guid CompanyAccountId, PartyCreditLimitKind Kind,
    Guid? CurrencyId, string? Amount, string ExpectedRevision);

// A decision names the proposal and the proposal's own version.
public sealed record PartyCreditLimitApproveRequest(Guid ProposalId, string ExpectedVersion);

public sealed record PartyCreditLimitRejectRequest(Guid ProposalId, string ExpectedVersion, string Reason);

// HTTP bodies: the proposal is the route's.
public sealed record PartyCreditLimitApprovalBody(string ExpectedVersion);

public sealed record PartyCreditLimitRejectionBody(string ExpectedVersion, string Reason);

// One record of an account's limit history. IsDirect: set by an approver in one step (FNS-D27). Revision:
// the account revision an approved record carries (null for a proposal never approved). No actor id is
// disclosed; a screen learns only whether a proposal is the caller's own.
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
// pending proposal (shown only to a caller who may propose or approve), the revision the next direct
// change or proposal must name, and whether the pending proposal is the caller's own.
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
    // FNS-D27: approve authority; approved at once.
    Task<PartyCreditLimitCommandResult> SetAsync(FinanceContext context, CommandMetadata metadata,
        PartyCreditLimitSetRequest request, CancellationToken cancellationToken);

    // FNS-D27: approve authority; an approved Remove ("no limit") at once.
    Task<PartyCreditLimitCommandResult> RemoveAsync(FinanceContext context, CommandMetadata metadata,
        PartyCreditLimitRemoveRequest request, CancellationToken cancellationToken);

    // FNS-D27a: propose authority; the effective limit does not change until a different actor approves.
    Task<PartyCreditLimitCommandResult> ProposeAsync(FinanceContext context, CommandMetadata metadata,
        PartyCreditLimitProposeRequest request, CancellationToken cancellationToken);

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
