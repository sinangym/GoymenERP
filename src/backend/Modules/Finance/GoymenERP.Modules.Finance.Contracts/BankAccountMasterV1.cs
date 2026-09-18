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

// Every command checks fresh company/action authority, including a replay.
public interface IFinanceBankAccountMaster
{
    Task<BankAccountCommandResult> ProposeAsync(FinanceContext context, CommandMetadata metadata,
        BankAccountProposeRequest request, CancellationToken cancellationToken);

    Task<BankAccountCommandResult> ApproveAsync(FinanceContext context, CommandMetadata metadata,
        BankAccountApproveRequest request, CancellationToken cancellationToken);
}
