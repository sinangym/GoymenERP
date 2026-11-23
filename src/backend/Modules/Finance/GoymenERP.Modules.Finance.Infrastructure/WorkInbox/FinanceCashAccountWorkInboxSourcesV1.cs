using System.Globalization;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

/// <summary>
/// WP5 (YOL-D41, AI-D32): the three cash-account approvals (API-30, Ö4) — a new account, its passivation and its
/// reactivation — as Finance lists them for the Work inbox. They have no screen of their own (YOL-D41): the decision is
/// given from the inbox (WP8) and the detail is the explanation card, so the link opens the inbox's own approval detail
/// (<c>/onaylarim/{kind}/{id}</c>). Each kind is decided by its own company-wide permission (the owner's permission port
/// answers only a company-wide grant, so scoped approvers are not accepted); the owner refuses only the proposer
/// (<c>finance_approver_is_proposer</c>), so the proposer is the maker and no one else is excluded (YOL-D23). Every pending
/// record of the company is returned, the caller's own included: Work hides a maker's own item. No numbers exist: the cash
/// account carries no amount in its approval (a passivation additionally requires a zero balance, which the owner checks
/// when it decides).
/// </summary>
internal abstract class FinanceCashAccountWorkInboxSourceBaseV1(
    FinanceDbContext dbContext,
    FinanceWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "finance";

    /// <summary>The Work inbox's own approval detail route (no owner screen exists, YOL-D41).</summary>
    public const string InboxDetailPath = "/onaylarim";

    public string Module => ModuleName;

    public abstract string Kind { get; }

    public abstract string ApproverPermission { get; }

    private protected abstract string RuleCode { get; }

    /// <summary>The pending rows of the company: filtered, oldest first, untracked.</summary>
    private protected abstract IQueryable<CashAccountRow> Pending(IQueryable<CashAccountRow> accounts, Guid companyId);

    private protected abstract (string? Maker, DateTimeOffset? Since) Waiting(CashAccountRow row);

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        FinanceWorkInboxRead.ListAsync(fence, context, ApproverPermission, maxCount, ReadAsync, Item, cancellationToken);

    private async Task<(List<CashAccountRow> Rows, bool Lookup)> ReadAsync(
        Guid companyId, int maxCount, CancellationToken cancellationToken)
    {
        var rows = await Pending(dbContext.CashAccounts.AsNoTracking(), companyId)
            .Take(maxCount + 1)
            .ToListAsync(cancellationToken);
        return (rows, true);
    }

    private WorkInboxItemV1? Item(CashAccountRow row, bool _)
    {
        var (maker, since) = Waiting(row);
        if (row.Id == Guid.Empty || row.CurrencyId == Guid.Empty || string.IsNullOrWhiteSpace(maker) || since is null)
        {
            return null;
        }

        return new WorkInboxItemV1(ModuleName, Kind, row.Id, FinanceWorkInboxFence.ShortReference(row.Id),
            since.Value.ToUniversalTime(), InboxLink(Kind, row.Id), maker,
            [new WorkApprovalRuleResultV1(RuleCode, null, null, null, null)],
            [
                FinanceWorkInboxRead.Reference("cash-account", row.Id, FinanceWorkInboxRead.Name(row.DisplayName, row.Id)),
                FinanceWorkInboxRead.Reference("currency", row.CurrencyId),
            ]);
    }

    internal static string InboxLink(string kind, Guid id) =>
        InboxDetailPath + "/" + kind + "/" + id.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>WP5 (YOL-D41): a proposed cash account waits for <c>finance.cash-account.approve</c> since it was proposed.</summary>
internal sealed class FinanceCashAccountWorkInboxSourceV1(FinanceDbContext dbContext, FinanceWorkInboxFence fence)
    : FinanceCashAccountWorkInboxSourceBaseV1(dbContext, fence)
{
    public const string KindName = "finance.cash-account.v1";

    /// <summary>Every new cash account needs one approval by someone other than its proposer (Ö4).</summary>
    public const string ApprovalRequiredRule = "finance.cash-account-approval-required";

    public override string Kind => KindName;

    public override string ApproverPermission => ErpPermissions.FinanceCashAccountApprove;

    private protected override string RuleCode => ApprovalRequiredRule;

    private protected override IQueryable<CashAccountRow> Pending(IQueryable<CashAccountRow> accounts, Guid companyId) =>
        accounts.Where(row => row.CompanyId == companyId && row.Status == FinanceCashAccountService.Proposed)
            .OrderBy(row => row.ProposedAtUtc).ThenBy(row => row.Id);

    private protected override (string? Maker, DateTimeOffset? Since) Waiting(CashAccountRow row) =>
        (row.ProposedBy, row.ProposedAtUtc);
}

/// <summary>
/// WP5 (YOL-D41): an active cash account with a pending passivation waits for
/// <c>finance.cash-account.passivation.approve</c> since the change was proposed.
/// </summary>
internal sealed class FinanceCashAccountPassivationWorkInboxSourceV1(FinanceDbContext dbContext, FinanceWorkInboxFence fence)
    : FinanceCashAccountWorkInboxSourceBaseV1(dbContext, fence)
{
    public const string KindName = "finance.cash-account-passivation.v1";

    /// <summary>Passivating a cash account (zero balance, no money effect) needs one approval by another person (Ö4).</summary>
    public const string ApprovalRequiredRule = "finance.cash-account-passivation-approval-required";

    public override string Kind => KindName;

    public override string ApproverPermission => ErpPermissions.FinanceCashAccountPassivationApprove;

    private protected override string RuleCode => ApprovalRequiredRule;

    private protected override IQueryable<CashAccountRow> Pending(IQueryable<CashAccountRow> accounts, Guid companyId) =>
        accounts.Where(row => row.CompanyId == companyId && row.Status == FinanceCashAccountService.Approved
                && row.ChangeStatus == FinanceCashAccountService.ChangePending
                && row.ChangeKind == FinanceCashAccountService.PassivationKind)
            .OrderBy(row => row.ChangeProposedAtUtc).ThenBy(row => row.Id);

    private protected override (string? Maker, DateTimeOffset? Since) Waiting(CashAccountRow row) =>
        (row.ChangeProposedBy, row.ChangeProposedAtUtc);
}

/// <summary>
/// WP5 (YOL-D41): a passive cash account with a pending reactivation waits for
/// <c>finance.cash-account.reactivation.approve</c> since the change was proposed.
/// </summary>
internal sealed class FinanceCashAccountReactivationWorkInboxSourceV1(FinanceDbContext dbContext, FinanceWorkInboxFence fence)
    : FinanceCashAccountWorkInboxSourceBaseV1(dbContext, fence)
{
    public const string KindName = "finance.cash-account-reactivation.v1";

    /// <summary>Reactivating a passive cash account (no money effect) needs one approval by another person (Ö4).</summary>
    public const string ApprovalRequiredRule = "finance.cash-account-reactivation-approval-required";

    public override string Kind => KindName;

    public override string ApproverPermission => ErpPermissions.FinanceCashAccountReactivationApprove;

    private protected override string RuleCode => ApprovalRequiredRule;

    private protected override IQueryable<CashAccountRow> Pending(IQueryable<CashAccountRow> accounts, Guid companyId) =>
        accounts.Where(row => row.CompanyId == companyId && row.Status == FinanceCashAccountService.Passive
                && row.ChangeStatus == FinanceCashAccountService.ChangePending
                && row.ChangeKind == FinanceCashAccountService.ReactivationKind)
            .OrderBy(row => row.ChangeProposedAtUtc).ThenBy(row => row.Id);

    private protected override (string? Maker, DateTimeOffset? Since) Waiting(CashAccountRow row) =>
        (row.ChangeProposedBy, row.ChangeProposedAtUtc);
}
