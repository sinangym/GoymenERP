using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32): the invoiced-shipment cost component cancellations prepared and waiting for approval, as
/// Accounting lists them for the Work inbox. Every cancellation step appends a version; a cancellation waits while the
/// LATEST version of its inventory cost proposal has the stored status 1 (Prepared: the approve command refuses any other)
/// since that version was recorded. It is approved on the invoiced-shipment reversal approval screen by a company-wide
/// holder of <c>accounting.journal.reverse.approve</c> (the permission the owner binds to this approval) other than its
/// preparer (<c>inventory_cost_approval_refused</c>); no other user is refused. The item is the inventory cost proposal the
/// approve command addresses. The approve command accepts only a company-wide grant (D11: no scoped approver).
/// </summary>
internal sealed class AccountingInventoryCostCancellationWorkInboxSourceV1(
    AccountingDbContext dbContext,
    AccountingWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "accounting";
    public const string KindName = "accounting.inventory-cost-cancellation.v1";
    public const string ScreenPath = "/muhasebe/sevk-smm-ters-kayit-onay";

    /// <summary>The stored cancellation status the approve command accepts (Prepared).</summary>
    internal const int PreparedStatus = 1;

    /// <summary>The source invoice was cancelled; its posted cost components are reversed only with a different person's approval.</summary>
    public const string ApprovalRequiredRule = "accounting.inventory-cost-cancellation-approval-required";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => AccountingJournalReversalPermissionNamesV1.Approve;

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        fence.ListAsync(context, ApproverPermission, maxCount,
            token =>
            {
                var versions = dbContext.InventoryCostCancellationVersions.AsNoTracking()
                    .Where(row => row.CompanyId == context.CompanyId);
                return versions
                    .Where(row => row.Status == PreparedStatus
                        && !versions.Any(newer => newer.InventoryCostProposalId == row.InventoryCostProposalId
                            && newer.Version > row.Version))
                    .OrderBy(row => row.RecordedAtUtc).ThenBy(row => row.InventoryCostProposalId)
                    .Take(maxCount + 1)
                    .ToListAsync(token);
            },
            rows => AccountingWorkInboxFence.Page(rows, maxCount, Item),
            cancellationToken);

    private static WorkInboxItemV1? Item(InventoryCostCancellationVersionRow row)
    {
        if (row.InventoryCostProposalId == Guid.Empty || row.Id == Guid.Empty || row.CancellationId == Guid.Empty
            || row.PreparedBy == Guid.Empty || row.PeriodId == Guid.Empty || row.ApprovedBy is not null)
        {
            return null;
        }

        return new WorkInboxItemV1(ModuleName, KindName, row.InventoryCostProposalId,
            AccountingWorkInboxFence.ShortReference(row.InventoryCostProposalId), row.RecordedAtUtc.ToUniversalTime(),
            ScreenPath, AccountingWorkInboxFence.Actor(row.PreparedBy),
            [new WorkApprovalRuleResultV1(ApprovalRequiredRule, null, null, null, null)],
            [
                new WorkApprovalReferenceV1("inventory-cost-proposal",
                    AccountingWorkInboxFence.ShortReference(row.InventoryCostProposalId), row.InventoryCostProposalId, null),
                new WorkApprovalReferenceV1("invoice-cancellation", AccountingWorkInboxFence.ShortReference(row.CancellationId),
                    row.CancellationId, null),
                new WorkApprovalReferenceV1("period", AccountingWorkInboxFence.ShortReference(row.PeriodId), row.PeriodId, null),
            ]);
    }
}
