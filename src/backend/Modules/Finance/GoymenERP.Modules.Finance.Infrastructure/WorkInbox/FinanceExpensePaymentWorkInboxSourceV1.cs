using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the expense payments (FNS-D25, API-25) waiting for approval, as Finance lists them for
/// the Work inbox. A payment waits while its status is Draft (the approve command accepts only a draft) since it was
/// prepared; Finance keeps no separate time for a later draft edit, so the preparation time stands. It is approved on the
/// expense payment screen by a holder of <c>finance.expense-payment.approve</c> who neither prepared nor revised the draft
/// (FNS-D118: the approve command refuses the preparer and everyone whose draft save succeeded; the first preparer is the
/// item's maker, the other revisers are its excluded deciders). The owner's permission port answers
/// only a company-wide grant, so scoped approvers are not accepted. Every draft of the company is returned, the caller's
/// own included: Work hides a maker's own item.
/// <para>
/// Finance holds no approval matrix, threshold or limit for this payment, so the one rule carries the stored amount as
/// the actual value and nothing else. The treasury account is a bank or a cash account as the payment's source kind says.
/// </para>
/// </summary>
internal sealed class FinanceExpensePaymentWorkInboxSourceV1(
    FinanceDbContext dbContext,
    FinanceWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "finance";
    public const string KindName = "finance.expense-payment.v1";
    public const string ScreenPath = "/finans/masraf-odemeleri";

    /// <summary>Every expense payment needs one approval by someone other than its preparer (FNS-D25).</summary>
    public const string ApprovalRequiredRule = "finance.expense-payment-approval-required";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => FinancePermissionNames.ExpensePaymentApprove;

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        FinanceWorkInboxRead.ListAsync(fence, context, ApproverPermission, maxCount, ReadAsync, Item, cancellationToken);

    private async Task<(List<ExpensePaymentRow> Rows, Lookup Lookup)> ReadAsync(
        Guid companyId, int maxCount, CancellationToken cancellationToken)
    {
        var rows = await dbContext.ExpensePayments.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Status == ExpensePaymentStatuses.Draft)
            .OrderBy(row => row.PreparedAtUtc).ThenBy(row => row.Id)
            .Take(maxCount + 1)
            .ToListAsync(cancellationToken);
        var returned = rows.Take(maxCount).ToArray();
        var bankIds = returned.Where(row => row.SourceKind == ExpensePaymentSourceKinds.Bank)
            .Select(row => row.TreasuryAccountId).Distinct().ToArray();
        var cashIds = returned.Where(row => row.SourceKind == ExpensePaymentSourceKinds.Cash)
            .Select(row => row.TreasuryAccountId).Distinct().ToArray();
        var banks = await dbContext.BankAccounts.AsNoTracking()
            .Where(row => row.CompanyId == companyId && bankIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.DisplayName, cancellationToken);
        var cash = await dbContext.CashAccounts.AsNoTracking()
            .Where(row => row.CompanyId == companyId && cashIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.DisplayName, cancellationToken);
        var revisers = await FinanceDraftRevisers.ExpensePaymentsAsync(dbContext, companyId,
            returned.Select(row => row.Id).ToArray(), cancellationToken);
        return (rows, new Lookup(banks, cash, revisers));
    }

    private static WorkInboxItemV1? Item(ExpensePaymentRow row, Lookup lookup)
    {
        if (row.Id == Guid.Empty || row.TreasuryAccountId == Guid.Empty || row.ExpenseItemId == Guid.Empty
            || row.CurrencyId == Guid.Empty || string.IsNullOrWhiteSpace(row.PreparedBy)
            || FinanceWorkInboxRead.Amount(row.AmountText) is not { } amount
            || lookup.Revisers is null
            || FinanceDraftRevisers.ExcludedBesides(lookup.Revisers, row.Id, row.PreparedBy) is not { } excluded)
        {
            return null;
        }

        WorkApprovalReferenceV1 treasury;
        switch (row.SourceKind)
        {
            case ExpensePaymentSourceKinds.Bank:
                treasury = FinanceWorkInboxRead.Reference("bank-account", row.TreasuryAccountId,
                    FinanceWorkInboxRead.Name(lookup.Banks.GetValueOrDefault(row.TreasuryAccountId), row.TreasuryAccountId));
                break;
            case ExpensePaymentSourceKinds.Cash:
                treasury = FinanceWorkInboxRead.Reference("cash-account", row.TreasuryAccountId,
                    FinanceWorkInboxRead.Name(lookup.Cash.GetValueOrDefault(row.TreasuryAccountId), row.TreasuryAccountId));
                break;
            default:
                return null;
        }

        return new WorkInboxItemV1(ModuleName, KindName, row.Id, FinanceWorkInboxRead.Label(row.DocumentNumber, row.Id),
            row.PreparedAtUtc.ToUniversalTime(), ScreenPath, row.PreparedBy,
            [new WorkApprovalRuleResultV1(ApprovalRequiredRule, null, amount, null, null)],
            [
                treasury,
                FinanceWorkInboxRead.Reference("expense-item", row.ExpenseItemId),
                FinanceWorkInboxRead.Reference("currency", row.CurrencyId),
            ],
            excluded.Count == 0 ? null : excluded);
    }

    private sealed record Lookup(
        IReadOnlyDictionary<Guid, string> Banks,
        IReadOnlyDictionary<Guid, string> Cash,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? Revisers);
}
