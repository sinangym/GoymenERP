using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the proposed opening balances (FIN-S9, FNS-D23, FNS-D84) waiting for a decision, as
/// Finance lists them for the Work inbox. An opening waits while its status is Proposed (the approve and reject commands
/// accept only that status) since it was proposed; a proposal is not edited. It is decided on the opening balance screen
/// by a holder of <c>finance.opening-balances.approve</c> other than the proposer (the approve command refuses only the
/// proposer: <c>finance_approver_is_proposer</c>). The owner's permission port answers only a company-wide grant, so
/// scoped approvers are not accepted. Every proposed opening of the company is returned, the caller's own included: Work
/// hides a maker's own item.
/// <para>
/// The one rule carries the stored amount as the actual value; no threshold exists. Whether the opening period has moved
/// since the proposal is the owner's check when the decision is given (it is another module's period, not read here).
/// </para>
/// </summary>
internal sealed class FinanceOpeningBalanceWorkInboxSourceV1(
    FinanceDbContext dbContext,
    FinanceWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "finance";
    public const string KindName = "finance.opening-balance.v1";
    public const string ScreenPath = "/finans/acilis-bakiyeleri";

    /// <summary>Every opening balance needs one approval by someone other than its proposer (FNS-D23).</summary>
    public const string ApprovalRequiredRule = "finance.opening-balance-approval-required";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => ErpPermissions.FinanceOpeningBalancesApprove;

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        FinanceWorkInboxRead.ListAsync(fence, context, ApproverPermission, maxCount, ReadAsync, Item, cancellationToken);

    private async Task<(List<OpeningBalanceRow> Rows, bool Lookup)> ReadAsync(
        Guid companyId, int maxCount, CancellationToken cancellationToken)
    {
        var rows = await dbContext.OpeningBalances.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Status == FinanceOpeningBalanceService.Proposed)
            .OrderBy(row => row.ProposedAtUtc).ThenBy(row => row.Id)
            .Take(maxCount + 1)
            .ToListAsync(cancellationToken);
        return (rows, true);
    }

    private static WorkInboxItemV1? Item(OpeningBalanceRow row, bool _)
    {
        if (row.Id == Guid.Empty || string.IsNullOrWhiteSpace(row.ProposedBy)
            || FinanceWorkInboxRead.Amount(row.AmountText) is not { } amount
            || References(row) is not { } references)
        {
            return null;
        }

        return new WorkInboxItemV1(ModuleName, KindName, row.Id, FinanceWorkInboxFence.ShortReference(row.Id),
            row.ProposedAtUtc.ToUniversalTime(), ScreenPath, row.ProposedBy,
            [new WorkApprovalRuleResultV1(ApprovalRequiredRule, null, amount, null, null)],
            references);
    }

    /// <summary>The opening's target (by its kind), party and currency; null when the row breaks the owner's invariants.</summary>
    internal static List<WorkApprovalReferenceV1>? References(OpeningBalanceRow row)
    {
        if (row.TargetId == Guid.Empty || row.CurrencyId == Guid.Empty || row.PartyId == Guid.Empty)
        {
            return null;
        }

        var role = row.TargetKind switch
        {
            FinanceOpeningBalanceService.CashAccountKind => "cash-account",
            FinanceOpeningBalanceService.BankAccountKind => "bank-account",
            FinanceOpeningBalanceService.PartyAccountKind => "company-account",
            _ => null,
        };
        if (role is null)
        {
            return null;
        }

        var references = new List<WorkApprovalReferenceV1>(3) { FinanceWorkInboxRead.Reference(role, row.TargetId) };
        if (row.PartyId is { } party)
        {
            references.Add(FinanceWorkInboxRead.Reference("party", party));
        }

        references.Add(FinanceWorkInboxRead.Reference("currency", row.CurrencyId));
        return references;
    }
}
