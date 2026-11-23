using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the proposed reversals of approved opening balances (FIN-S9, FNS-D85) waiting for a
/// decision, as Finance lists them for the Work inbox. A reversal waits while its status is Proposed and its opening is
/// Approved (the approve and reject commands accept only that pair) since the reversal was proposed. It is decided on the
/// opening balance screen by a holder of <c>finance.opening-balances.approve</c> other than the reversal's proposer (the
/// approve command refuses only that proposer: <c>finance_approver_is_proposer</c>; the opening's own proposer or approver
/// is not refused). The owner's permission port answers only a company-wide grant, so scoped approvers are not accepted.
/// Every pending reversal of the company is returned, the caller's own included: Work hides a maker's own item.
/// <para>
/// The one rule carries the opening's stored amount as the actual value; no threshold exists. A pending reversal whose
/// opening Finance does not find, or finds not approved, breaks the owner's invariant (the opening is marked reversed in
/// the same save that approves the reversal), so the answer is Unavailable.
/// </para>
/// </summary>
internal sealed class FinanceOpeningBalanceReversalWorkInboxSourceV1(
    FinanceDbContext dbContext,
    FinanceWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "finance";
    public const string KindName = "finance.opening-balance-reversal.v1";
    public const string ScreenPath = "/finans/acilis-bakiyeleri";

    /// <summary>Every reversal of an approved opening balance needs one approval by someone other than its proposer (FNS-D85).</summary>
    public const string ApprovalRequiredRule = "finance.opening-balance-reversal-approval-required";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => ErpPermissions.FinanceOpeningBalancesApprove;

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        FinanceWorkInboxRead.ListAsync(fence, context, ApproverPermission, maxCount, ReadAsync, Item, cancellationToken);

    private async Task<(List<OpeningBalanceReversalRow> Rows, IReadOnlyDictionary<Guid, OpeningBalanceRow> Openings)> ReadAsync(
        Guid companyId, int maxCount, CancellationToken cancellationToken)
    {
        var rows = await dbContext.OpeningBalanceReversals.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Status == FinanceOpeningBalanceService.Proposed)
            .OrderBy(row => row.ProposedAtUtc).ThenBy(row => row.Id)
            .Take(maxCount + 1)
            .ToListAsync(cancellationToken);
        var openingIds = rows.Take(maxCount).Select(row => row.OpeningBalanceId).Distinct().ToArray();
        var openings = await dbContext.OpeningBalances.AsNoTracking()
            .Where(row => row.CompanyId == companyId && openingIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        return (rows, openings);
    }

    private static WorkInboxItemV1? Item(OpeningBalanceReversalRow row, IReadOnlyDictionary<Guid, OpeningBalanceRow> openings)
    {
        if (row.Id == Guid.Empty || row.OpeningBalanceId == Guid.Empty || string.IsNullOrWhiteSpace(row.ProposedBy)
            || !openings.TryGetValue(row.OpeningBalanceId, out var opening)
            || opening.Status != FinanceOpeningBalanceService.Approved
            || FinanceWorkInboxRead.Amount(opening.AmountText) is not { } amount
            || FinanceOpeningBalanceWorkInboxSourceV1.References(opening) is not { } references)
        {
            return null;
        }

        references.Insert(0, FinanceWorkInboxRead.Reference("opening-balance", opening.Id));
        return new WorkInboxItemV1(ModuleName, KindName, row.Id, FinanceWorkInboxFence.ShortReference(opening.Id),
            row.ProposedAtUtc.ToUniversalTime(), ScreenPath, row.ProposedBy,
            [new WorkApprovalRuleResultV1(ApprovalRequiredRule, null, amount, null, null)],
            references);
    }
}
