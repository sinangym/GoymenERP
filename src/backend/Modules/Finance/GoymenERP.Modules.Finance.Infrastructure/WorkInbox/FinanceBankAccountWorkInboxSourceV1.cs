using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the proposed bank accounts (API-30, Ö4) waiting for approval, as Finance lists them for
/// the Work inbox. An account waits while its status is Proposed (the approve command accepts only that status) since it
/// was proposed; a proposed card is not edited. It is approved on the bank account screen by a holder of
/// <c>finance.bank-account.approve</c> other than the proposer (the approve command refuses only the proposer:
/// <c>finance_approver_is_proposer</c>). The owner's permission port answers only a company-wide grant, so scoped
/// approvers are not accepted. Every proposed account of the company is returned, the caller's own included: Work hides a
/// maker's own item.
/// <para>
/// The approval has no numbers (no threshold, no amount): the one rule only says why it waits. The account's IBAN is not
/// carried into the inbox; the card is read on the owner's screen.
/// </para>
/// </summary>
internal sealed class FinanceBankAccountWorkInboxSourceV1(
    FinanceDbContext dbContext,
    FinanceWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "finance";
    public const string KindName = "finance.bank-account.v1";
    public const string ScreenPath = "/finans/banka-hesaplari";

    /// <summary>Every new bank account card needs one approval by someone other than its proposer (Ö4).</summary>
    public const string ApprovalRequiredRule = "finance.bank-account-approval-required";

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
            .Where(row => row.CompanyId == companyId && row.Status == FinanceBankAccountService.Proposed)
            .OrderBy(row => row.ProposedAtUtc).ThenBy(row => row.Id)
            .Take(maxCount + 1)
            .ToListAsync(cancellationToken);
        return (rows, true);
    }

    private static WorkInboxItemV1? Item(BankAccountRow row, bool _)
    {
        if (row.Id == Guid.Empty || row.CurrencyId == Guid.Empty || string.IsNullOrWhiteSpace(row.ProposedBy))
        {
            return null;
        }

        return new WorkInboxItemV1(ModuleName, KindName, row.Id, FinanceWorkInboxFence.ShortReference(row.Id),
            row.ProposedAtUtc.ToUniversalTime(), ScreenPath, row.ProposedBy,
            [new WorkApprovalRuleResultV1(ApprovalRequiredRule, null, null, null, null)],
            [
                FinanceWorkInboxRead.Reference("bank-account", row.Id, FinanceWorkInboxRead.Name(row.DisplayName, row.Id)),
                FinanceWorkInboxRead.Reference("currency", row.CurrencyId),
            ]);
    }
}
