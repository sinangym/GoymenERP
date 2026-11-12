using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

// API-30: the Finance cash account (kasa) master. A definition only: it holds NO balance and NO opening
// balance and moves no money. It follows the bank-account master's shape (FS0): proposed by one actor,
// approved or rejected by a DIFFERENT actor (maker≠checker, held in the service and in the database).
//
// The currency is one of Settings' money currency candidates of the server-resolved company (FNS-D24:
// TL/USD/EUR are configured in Settings, never hard-coded here), checked exactly as the bank master checks it.
//
// OPEN (NEEDS-SINAN): a "responsible person" (sorumlu) for a cash account is NOT modelled here. Whether it
// is a user id, a free label, or an authority that gates who may move money through the account is an
// unanswered business question; a field would silently choose an answer.
// OPEN: passivating an approved cash account (aktif → pasif) has no command in this slice; an approved
// account is active, and the state is shown so a later command can move it.

public sealed record CashAccountProposeRequest(string DisplayName, Guid CurrencyId);

public sealed record CashAccountApproveRequest(Guid AccountId, string ExpectedVersion);

public sealed record CashAccountRejectRequest(Guid AccountId, string ExpectedVersion, string Reason);

// HTTP bodies: the account is the route's.
public sealed record CashAccountApprovalBody(string ExpectedVersion);

public sealed record CashAccountRejectionBody(string ExpectedVersion, string Reason);

[JsonConverter(typeof(JsonStringEnumConverter<CashAccountStatus>))]
public enum CashAccountStatus { Proposed, Approved, Rejected }

// IsActive: an approved account is active (usable as a definition); a proposal or a rejection is not.
public sealed record CashAccountView(
    Guid Id,
    Guid CompanyId,
    Guid CurrencyId,
    string DisplayName,
    CashAccountStatus Status,
    bool IsActive,
    string? RejectionReason,
    string Version);

public sealed record CashAccountCommandResult(CommandOutcome Outcome, CashAccountView? Resource,
    bool IsReplay, string OriginalCorrelationId, IReadOnlyList<ValidationIssue> Issues);

// Each account with whether the caller proposed it, so a screen offers the decision only to someone
// else. The server refuses a self-decision regardless.
public sealed record CashAccountMasterItem(CashAccountView Account, bool IsOwnProposal);

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

    // Under the read authority. Proposals and rejections are listed only to a caller who may propose or
    // decide; a read-only caller sees approved accounts.
    Task<ReadResult<CashAccountMasterPage>> ListAsync(FinanceContext context, CancellationToken cancellationToken);

    Task<ReadResult<CashAccountView>> GetAsync(FinanceContext context, Guid accountId, CancellationToken cancellationToken);

    // Under the propose authority.
    Task<ReadResult<CashAccountCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext context,
        CancellationToken cancellationToken);
}
