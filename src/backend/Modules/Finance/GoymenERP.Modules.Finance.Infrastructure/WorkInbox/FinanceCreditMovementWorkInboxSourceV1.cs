using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the proposed credit movements (drawdown, repayment, interest accrual, opening balance)
/// waiting for a decision, as Finance lists them for the Work inbox. A movement waits while its status is Proposed (the
/// decide command accepts only that status) since it was proposed; a proposal is not edited. It is decided on the credits
/// screen by a holder of <c>finance.credits.approve</c> other than the proposer (the decide command refuses only the
/// proposer: <c>credit_movement_maker_cannot_approve</c>). The owner's permission port answers only a company-wide grant,
/// so scoped approvers are not accepted. Every proposed movement of the company is returned, the caller's own included:
/// Work hides a maker's own item.
/// <para>
/// The rule names the movement's kind. Only the stored bank amount (the movement's bank effect, when it has one) is carried
/// as the actual value; the movement's components live in the owner's source document and are read on the owner's screen.
/// </para>
/// </summary>
internal sealed class FinanceCreditMovementWorkInboxSourceV1(
    FinanceDbContext dbContext,
    FinanceWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "finance";
    public const string KindName = "finance.credit-movement.v1";
    public const string ScreenPath = "/finans/krediler";

    /// <summary>A proposed drawdown of the credit.</summary>
    public const string DrawdownRule = "finance.credit-movement-drawdown";

    /// <summary>A proposed repayment of the credit.</summary>
    public const string RepaymentRule = "finance.credit-movement-repayment";

    /// <summary>A proposed interest accrual of the credit.</summary>
    public const string InterestAccrualRule = "finance.credit-movement-interest-accrual";

    /// <summary>A proposed opening balance of the credit.</summary>
    public const string OpeningBalanceRule = "finance.credit-movement-opening-balance";

    private const int Proposed = (int)CreditMovementStatusV1.Proposed;

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => ErpPermissions.FinanceCreditsApprove;

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        FinanceWorkInboxRead.ListAsync(fence, context, ApproverPermission, maxCount, ReadAsync, Item, cancellationToken);

    private async Task<(List<FinanceCreditMovementRow> Rows, IReadOnlyDictionary<Guid, string> Banks)> ReadAsync(
        Guid companyId, int maxCount, CancellationToken cancellationToken)
    {
        var rows = await dbContext.CreditMovements.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Status == Proposed)
            .OrderBy(row => row.ProposedAtUtc).ThenBy(row => row.Id)
            .Take(maxCount + 1)
            .ToListAsync(cancellationToken);
        var bankIds = rows.Take(maxCount).Where(row => row.BankAccountId != null)
            .Select(row => row.BankAccountId!.Value).Distinct().ToArray();
        var banks = await dbContext.BankAccounts.AsNoTracking()
            .Where(row => row.CompanyId == companyId && bankIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.DisplayName, cancellationToken);
        return (rows, banks);
    }

    private static WorkInboxItemV1? Item(FinanceCreditMovementRow row, IReadOnlyDictionary<Guid, string> banks)
    {
        if (row.Id == Guid.Empty || row.CreditAccountId == Guid.Empty || row.CurrencyId == Guid.Empty
            || string.IsNullOrWhiteSpace(row.ProposedBy))
        {
            return null;
        }

        string? bankAmount = null;
        if (row.BankAccountId is { } bank)
        {
            if (bank == Guid.Empty || FinanceWorkInboxRead.Amount(row.BankAmountText) is not { } stored)
            {
                return null;
            }

            bankAmount = stored;
        }
        else if (row.BankAmountText is not null)
        {
            return null;
        }

        WorkApprovalRuleResultV1 rule;
        switch (row.Kind)
        {
            case (int)CreditMovementKindV1.Drawdown:
                rule = new(DrawdownRule, null, bankAmount, null, null);
                break;
            case (int)CreditMovementKindV1.Repayment:
                rule = new(RepaymentRule, null, bankAmount, null, null);
                break;
            case (int)CreditMovementKindV1.InterestAccrual:
                rule = new(InterestAccrualRule, null, bankAmount, null, null);
                break;
            case (int)CreditMovementKindV1.OpeningBalance:
                rule = new(OpeningBalanceRule, null, bankAmount, null, null);
                break;
            default:
                return null;
        }

        var references = new List<WorkApprovalReferenceV1>(4)
        {
            FinanceWorkInboxRead.Reference("credit-account", row.CreditAccountId),
            FinanceWorkInboxRead.Reference("currency", row.CurrencyId),
        };
        if (row.BankAccountId is { } bankId)
        {
            references.Add(FinanceWorkInboxRead.Reference("bank-account", bankId,
                FinanceWorkInboxRead.Name(banks.GetValueOrDefault(bankId), bankId)));
        }

        if (row.ReplacesOpeningMovementId is { } replaced && replaced != Guid.Empty)
        {
            references.Add(FinanceWorkInboxRead.Reference("replaced-opening-movement", replaced));
        }

        return new WorkInboxItemV1(ModuleName, KindName, row.Id, FinanceWorkInboxFence.ShortReference(row.Id),
            row.ProposedAtUtc.ToUniversalTime(), ScreenPath, row.ProposedBy, [rule], references);
    }
}
