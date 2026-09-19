namespace GoymenERP.Modules.Finance.Contracts;

// FS0: the Finance bank account master (card #66 5731563923). An account carries exactly the
// fields of BankAccountView; bank name, branch, IBAN, account number and type are not decided and
// not modelled. It moves no money.

// Propose: the company comes from the server context, never from the request.
public sealed record BankAccountProposeRequest(string DisplayName, Guid CurrencyId);

// Approve: Ö4 (#66 5722637822) — a single authorized person other than the one who proposed it.
public sealed record BankAccountApproveRequest(Guid AccountId, string ExpectedVersion);

public sealed record BankAccountCommandResult(CommandOutcome Outcome, BankAccountView? Resource,
    bool IsReplay, string OriginalCorrelationId, IReadOnlyList<ValidationIssue> Issues);

// FIN-UI-1 HTTP: the approval body. The account is the route's; only the expected version travels.
public sealed record BankAccountApprovalBody(string ExpectedVersion);

// F-04's list (FIN-UI-1): each account with whether the caller proposed it, so a screen can offer the
// approval only to someone else (Ö4). The server refuses a self-approval regardless.
public sealed record BankAccountMasterItem(BankAccountView Account, bool IsOwnProposal);
public sealed record BankAccountMasterPage(Guid CompanyId, IReadOnlyList<BankAccountMasterItem> Items);

// The currencies a proposal may name: Settings' money currency candidates of the server-resolved
// company, passed through as the owner gave them. Order is undefined, no row is a default, and an
// empty list means "no candidates", never a made-up currency (Business #66 5702138790).
public sealed record BankAccountCurrencyCandidate(Guid CurrencyId, string Code, string DisplayLabel);
public sealed record BankAccountCurrencyCandidates(Guid CompanyId, IReadOnlyList<BankAccountCurrencyCandidate> Items);

// Every command checks fresh company/action authority, including a replay.
public interface IFinanceBankAccountMaster
{
    Task<BankAccountCommandResult> ProposeAsync(FinanceContext context, CommandMetadata metadata,
        BankAccountProposeRequest request, CancellationToken cancellationToken);

    Task<BankAccountCommandResult> ApproveAsync(FinanceContext context, CommandMetadata metadata,
        BankAccountApproveRequest request, CancellationToken cancellationToken);

    // Under the read authority: the company's accounts for F-04, proposals included.
    Task<ReadResult<BankAccountMasterPage>> ListMasterAsync(FinanceContext context, CancellationToken cancellationToken);

    // Under the propose authority: the currencies a proposal may name, never a default.
    Task<ReadResult<BankAccountCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext context,
        CancellationToken cancellationToken);
}
