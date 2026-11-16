using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

// FIN-SC47 (FNS-D87, FNS-D88, FNS-D93..FNS-D97): the skeleton of the finance credit sub-area (krediler).
//
// FNS-D87: a credit account carries ONE of four groups as a type field: cash credit (nakdi), non-cash credit
// (gayrinakdi: letter of guarantee and similar), instrument-backed credit and factoring (çek/senet + faktoring) and
// leasing. Group-specific fields (letter-of-guarantee kind, factoring recourse, leasing contract fields) are NOT decided
// and not built (NEEDS-SINAN).
//
// FNS-D88: a credit account is proposed by a holder of finance.credits.propose and approved or rejected by a holder of
// finance.credits.approve other than its proposer (maker≠checker, person level, in the service and in a database check).
// The approval writes no movement and no voucher.
//
// FNS-D93/FNS-D94: the bank's repayment plan is imported AS GIVEN: each installment line carries the installment, the
// principal, the interest, the BSMV and the KKDF as exact decimal text, never rounded, never converted. The system never
// calculates interest; it only checks the totals with one deterministic backend calculator (also exposed as a
// side-effect-free validation endpoint so the screen does not re-implement it):
//   * every installment = principal + interest + BSMV + KKDF, exactly;
//   * the sum of the principals = the credit amount, exactly;
//   * a plan that fails cannot be proposed (and so never reaches approval); the approval checks the stored plan again.
//
// NOT built in this slice (W6): the credit screen, FNS-D95 limit per (bank, group) with its warning (a warning only, never
// a block; who enters the limit and its form are not decided), and the FNS-D97 voucher proposal (Settings account mapping,
// "eşleme eksik" when missing, idempotent, never a final voucher).

[JsonConverter(typeof(JsonStringEnumConverter<CreditAccountGroup>))]
public enum CreditAccountGroup { Cash, NonCash, InstrumentAndFactoring, Leasing }

[JsonConverter(typeof(JsonStringEnumConverter<CreditAccountStatus>))]
public enum CreditAccountStatus { Proposed, Approved, Rejected }

// One line of the bank's repayment plan, as the bank gives it. Sequence: 1..n in order. Every amount is exact
// non-negative decimal text in the credit's currency.
public sealed record CreditPlanInstallmentInput(int Sequence, DateOnly DueDate, string Installment, string Principal,
    string Interest, string Bsmv, string Kkdf);

// A new credit account proposal. LenderName: the bank or institution as written on the contract (there is no lender master
// yet). ContractReference: the bank's contract or credit number. PrincipalAmount: the credit amount (exact positive text).
public sealed record CreditAccountProposeRequest(CreditAccountGroup Group, string LenderName, string ContractReference,
    Guid CurrencyId, string PrincipalAmount, DateOnly StartDate, IReadOnlyList<CreditPlanInstallmentInput> Installments);

public sealed record CreditAccountApproveRequest(Guid CreditAccountId, string ExpectedVersion);

public sealed record CreditAccountRejectRequest(Guid CreditAccountId, string ExpectedVersion, string Reason);

// HTTP bodies: the credit account is the route's.
public sealed record CreditAccountApproveBody(string ExpectedVersion);

public sealed record CreditAccountRejectBody(string ExpectedVersion, string Reason);

// The plan totals the calculator derived from the lines (exact sums, never rounded).
public sealed record CreditPlanTotalsView(string Installment, string Principal, string Interest, string Bsmv,
    string Kkdf);

public sealed record CreditPlanInstallmentView(int Sequence, DateOnly DueDate, string Installment, string Principal,
    string Interest, string Bsmv, string Kkdf);

// One credit account. IsOwnProposal: the caller proposed it (the approve control is not offered on it). No actor id is
// disclosed.
public sealed record CreditAccountView(
    Guid Id,
    Guid CompanyId,
    CreditAccountGroup Group,
    string LenderName,
    string ContractReference,
    Guid CurrencyId,
    string PrincipalAmount,
    DateOnly StartDate,
    CreditAccountStatus Status,
    bool IsOwnProposal,
    DateTimeOffset ProposedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    string? RejectionReason,
    string Version,
    CreditPlanTotalsView PlanTotals,
    IReadOnlyList<CreditPlanInstallmentView> Installments);

public sealed record CreditAccountListPage(Guid CompanyId, IReadOnlyList<CreditAccountView> Items);

public sealed record CreditAccountCommandResult(CommandOutcome Outcome, CreditAccountView? Resource, bool IsReplay,
    string OriginalCorrelationId, IReadOnlyList<ValidationIssue> Issues);

// FNS-D94: the side-effect-free plan check (the same calculator the proposal and the approval use).
public sealed record CreditPlanValidationRequest(string PrincipalAmount,
    IReadOnlyList<CreditPlanInstallmentInput> Installments);

// IsValid: no issue. Totals: null when a line could not be read as exact non-negative text.
public sealed record CreditPlanValidationView(Guid CompanyId, bool IsValid, CreditPlanTotalsView? Totals,
    IReadOnlyList<ValidationIssue> Issues);

public sealed record CreditAccountCurrencyCandidate(Guid CurrencyId, string Code, string DisplayLabel);

public sealed record CreditAccountCurrencyCandidates(Guid CompanyId,
    IReadOnlyList<CreditAccountCurrencyCandidate> Items);

// Every command checks fresh company/action authority, including a replay.
public interface IFinanceCreditAccountService
{
    // FNS-D88: the proposal (propose authority). A plan that fails the FNS-D94 check is refused and not stored.
    Task<CreditAccountCommandResult> ProposeAsync(FinanceContext context, CommandMetadata metadata,
        CreditAccountProposeRequest request, CancellationToken cancellationToken);

    // FNS-D88: the decision (approve authority; never the proposer). The approval re-checks the stored plan.
    Task<CreditAccountCommandResult> ApproveAsync(FinanceContext context, CommandMetadata metadata,
        CreditAccountApproveRequest request, CancellationToken cancellationToken);

    Task<CreditAccountCommandResult> RejectAsync(FinanceContext context, CommandMetadata metadata,
        CreditAccountRejectRequest request, CancellationToken cancellationToken);

    // Under the read authority. Newest proposal first.
    Task<ReadResult<CreditAccountListPage>> ListAsync(FinanceContext context, CancellationToken cancellationToken);

    // Under the read authority. An unknown or other company's account answers NotFound.
    Task<ReadResult<CreditAccountView>> GetAsync(FinanceContext context, Guid creditAccountId,
        CancellationToken cancellationToken);

    // Under the propose authority; stores nothing.
    Task<ReadResult<CreditPlanValidationView>> ValidatePlanAsync(FinanceContext context,
        CreditPlanValidationRequest request, CancellationToken cancellationToken);

    // Under the propose authority.
    Task<ReadResult<CreditAccountCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext context,
        CancellationToken cancellationToken);
}
