using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the proposed bank credit limits waiting for a decision, as Finance lists them for the
/// Work inbox. A limit waits while its status is Proposed (the decide command accepts only that status; the status codes
/// are the credit account's) since it was proposed; a proposal is not edited. It is decided on the credits screen by a
/// holder of <c>finance.credits.approve</c> other than the proposer (the decide command refuses only the proposer:
/// <c>credit_limit_maker_cannot_approve</c>). The owner's permission port answers only a company-wide grant, so scoped
/// approvers are not accepted. Every proposed limit of the company is returned, the caller's own included: Work hides a
/// maker's own item.
/// <para>
/// The one rule carries the stored proposed amount as the actual value; no threshold exists and the current limit is not
/// re-derived here (the owner checks it when the decision is given).
/// </para>
/// </summary>
internal sealed class FinanceBankCreditLimitWorkInboxSourceV1(
    FinanceDbContext dbContext,
    FinanceWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "finance";
    public const string KindName = "finance.bank-credit-limit.v1";
    public const string ScreenPath = "/finans/krediler";

    /// <summary>Every proposed bank credit limit needs one decision by someone other than its proposer.</summary>
    public const string ApprovalRequiredRule = "finance.bank-credit-limit-approval-required";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => ErpPermissions.FinanceCreditsApprove;

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        FinanceWorkInboxRead.ListAsync(fence, context, ApproverPermission, maxCount, ReadAsync, Item, cancellationToken);

    private async Task<(List<FinanceCreditLimitRow> Rows, bool Lookup)> ReadAsync(
        Guid companyId, int maxCount, CancellationToken cancellationToken)
    {
        var rows = await dbContext.CreditLimits.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Status == FinanceCreditAccountService.Proposed)
            .OrderBy(row => row.ProposedAtUtc).ThenBy(row => row.Id)
            .Take(maxCount + 1)
            .ToListAsync(cancellationToken);
        return (rows, true);
    }

    private static WorkInboxItemV1? Item(FinanceCreditLimitRow row, bool _)
    {
        if (row.Id == Guid.Empty || row.CreditAccountId == Guid.Empty || row.LenderPartyId == Guid.Empty
            || row.LenderCompanyPartyAccountId == Guid.Empty || row.CurrencyId == Guid.Empty
            || string.IsNullOrWhiteSpace(row.ProposedBy)
            || FinanceWorkInboxRead.Amount(row.AmountText) is not { } amount)
        {
            return null;
        }

        return new WorkInboxItemV1(ModuleName, KindName, row.Id, FinanceWorkInboxFence.ShortReference(row.Id),
            row.ProposedAtUtc.ToUniversalTime(), ScreenPath, row.ProposedBy,
            [new WorkApprovalRuleResultV1(ApprovalRequiredRule, null, amount, null, null)],
            [
                FinanceWorkInboxRead.Reference("credit-account", row.CreditAccountId),
                FinanceWorkInboxRead.Reference("lender-party", row.LenderPartyId),
                FinanceWorkInboxRead.Reference("company-account", row.LenderCompanyPartyAccountId),
                FinanceWorkInboxRead.Reference("currency", row.CurrencyId),
            ]);
    }
}
