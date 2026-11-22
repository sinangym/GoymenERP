using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32): the accounting periods proposed for opening (Ö4), as Accounting lists them for the Work inbox. A
/// period waits while its status is Proposed (<c>AccountingPeriod.Approve</c> accepts only a proposal) since it was prepared;
/// it is approved on the periods screen by a company-wide holder of <c>accounting.periods.approve</c> other than its
/// preparer (<c>accounting_period_self_approval</c>). The approve command names no other excluded user.
/// <para>
/// Closing an open period is a single authorised act (Ö5, <c>accounting.periods.close</c>) with no pending approval, so it
/// is not a Work inbox kind. The approve command accepts only a company-wide grant (D11: no scoped approver).
/// </para>
/// </summary>
internal sealed class AccountingPeriodOpeningWorkInboxSourceV1(
    AccountingDbContext dbContext,
    AccountingWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "accounting";
    public const string KindName = "accounting.period-opening.v1";
    public const string ScreenPath = "/muhasebe/donemler";

    /// <summary>A proposed period opens for posting only with one approval by someone other than its preparer (Ö4).</summary>
    public const string OpeningApprovalRequiredRule = "accounting.period-opening-approval-required";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => AccountingPermissionManifestV1.PeriodsApprove;

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        fence.ListAsync(context, ApproverPermission, maxCount,
            token => dbContext.Periods.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId && row.Status == (int)AccountingPeriodStatus.Proposed)
                .OrderBy(row => row.PreparedAtUtc).ThenBy(row => row.Id)
                .Take(maxCount + 1)
                .ToListAsync(token),
            rows => AccountingWorkInboxFence.Page(rows, maxCount, Item),
            cancellationToken);

    private static WorkInboxItemV1? Item(PeriodRow row)
    {
        if (row.Id == Guid.Empty || row.PreparedByUserId == Guid.Empty || row.EndsOn < row.StartsOn
            || row.ApprovedByUserId is not null || row.ClosedByUserId is not null)
        {
            return null;
        }

        var label = AccountingWorkInboxFence.PeriodLabel(row.StartsOn, row.EndsOn);
        return new WorkInboxItemV1(ModuleName, KindName, row.Id, label, row.PreparedAtUtc.ToUniversalTime(), ScreenPath,
            AccountingWorkInboxFence.Actor(row.PreparedByUserId),
            [new WorkApprovalRuleResultV1(OpeningApprovalRequiredRule, null, null, null, null)],
            [new WorkApprovalReferenceV1("period", label, row.Id, ScreenPath)]);
    }
}
