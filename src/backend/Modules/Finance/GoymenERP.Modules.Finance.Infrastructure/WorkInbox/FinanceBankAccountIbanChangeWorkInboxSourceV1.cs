using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the IBAN changes proposed on approved bank accounts (D4, Ö4) waiting for approval, as
/// Finance lists them for the Work inbox. A change waits while the account carries a pending IBAN (the approve command
/// accepts only an account whose pending IBAN, proposer and reason are set) since the change was proposed. It is approved
/// on the bank account screen by a holder of <c>finance.bank-account.approve</c> other than the change's proposer (the
/// approve command refuses only that proposer: <c>finance_approver_is_proposer</c>; the account's original proposer is not
/// refused). The owner's permission port answers only a company-wide grant, so scoped approvers are not accepted. Every
/// pending change of the company is returned, the caller's own included: Work hides a maker's own item.
/// <para>
/// The four pending columns are set or cleared together (the owner's invariant); a row with only some of them is
/// unreadable, so the answer is Unavailable. Neither the current nor the proposed IBAN is carried into the inbox.
/// </para>
/// </summary>
internal sealed class FinanceBankAccountIbanChangeWorkInboxSourceV1(
    FinanceDbContext dbContext,
    FinanceWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "finance";
    public const string KindName = "finance.bank-account-iban-change.v1";
    public const string ScreenPath = "/finans/banka-hesaplari";

    /// <summary>An IBAN change on an approved bank account needs one approval by someone other than its proposer (D4).</summary>
    public const string ApprovalRequiredRule = "finance.bank-account-iban-change-approval-required";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => ErpPermissions.FinanceBankAccountApprove;

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        FinanceWorkInboxRead.ListAsync(fence, context, ApproverPermission, maxCount, ReadAsync, Item, cancellationToken);

    private async Task<(List<BankAccountRow> Rows, bool Lookup)> ReadAsync(
        Guid companyId, int maxCount, CancellationToken cancellationToken)
    {
        var rows = await dbContext.BankAccounts.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.PendingIban != null)
            .OrderBy(row => row.PendingIbanProposedAtUtc).ThenBy(row => row.Id)
            .Take(maxCount + 1)
            .ToListAsync(cancellationToken);
        return (rows, true);
    }

    private static WorkInboxItemV1? Item(BankAccountRow row, bool _)
    {
        if (row.Id == Guid.Empty || row.CurrencyId == Guid.Empty
            || string.IsNullOrWhiteSpace(row.PendingIban) || string.IsNullOrWhiteSpace(row.PendingIbanProposedBy)
            || row.PendingIbanProposedAtUtc is not { } proposedAt || string.IsNullOrWhiteSpace(row.PendingIbanReason))
        {
            return null;
        }

        return new WorkInboxItemV1(ModuleName, KindName, row.Id, FinanceWorkInboxFence.ShortReference(row.Id),
            proposedAt.ToUniversalTime(), ScreenPath, row.PendingIbanProposedBy,
            [new WorkApprovalRuleResultV1(ApprovalRequiredRule, null, null, null, null)],
            [
                FinanceWorkInboxRead.Reference("bank-account", row.Id, FinanceWorkInboxRead.Name(row.DisplayName, row.Id)),
                FinanceWorkInboxRead.Reference("currency", row.CurrencyId),
            ]);
    }
}
