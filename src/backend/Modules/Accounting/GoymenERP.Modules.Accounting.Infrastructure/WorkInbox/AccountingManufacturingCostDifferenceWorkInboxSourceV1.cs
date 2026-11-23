using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32): the manufacturing consumed-cost differences prepared and waiting for approval, as Accounting lists
/// them for the Work inbox. A proposal waits while its stored status is 2 (Prepared: the approve command refuses any other
/// status) since its latest preparation (a re-preparation restamps it); it is approved and posted on the cost difference
/// approval screen by a company-wide holder of <c>accounting.journal.reverse.approve</c> (the permission the owner binds to
/// this approval) other than its preparer (<c>cost_difference_approval_refused</c>); no other user is refused.
/// <para>
/// The one rule carries the stored signed TL difference as the actual value (the owner posts it in TL); no threshold
/// exists. The approve command accepts only a company-wide grant (D11: no scoped approver).
/// </para>
/// </summary>
internal sealed class AccountingManufacturingCostDifferenceWorkInboxSourceV1(
    AccountingDbContext dbContext,
    AccountingWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "accounting";
    public const string KindName = "accounting.manufacturing-cost-difference.v1";
    public const string ScreenPath = "/muhasebe/uretim-maliyet-farki-onay";

    /// <summary>The stored status the approve command accepts (Prepared).</summary>
    internal const int PreparedStatus = 2;

    /// <summary>The unit of the stored signed difference: the owner posts it in Turkish lira.</summary>
    internal const string TlUnit = "TRY";

    /// <summary>A prepared consumed-cost difference is posted only with a different person's approval.</summary>
    public const string ApprovalRequiredRule = "accounting.manufacturing-cost-difference-approval-required";

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => AccountingJournalReversalPermissionNamesV1.Approve;

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        fence.ListAsync(context, ApproverPermission, maxCount,
            token => dbContext.ManufacturingCostDifferences.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId && row.Status == PreparedStatus)
                .OrderBy(row => row.PreparedAtUtc).ThenBy(row => row.Id)
                .Take(maxCount + 1)
                .ToListAsync(token),
            rows => AccountingWorkInboxFence.Page(rows, maxCount, Item),
            cancellationToken);

    private static WorkInboxItemV1? Item(ManufacturingCostDifferenceRow row)
    {
        if (row.Id == Guid.Empty || row.ManufacturingOrderId == Guid.Empty
            || row.PreparedBy is not { } preparedBy || preparedBy == Guid.Empty || row.PreparedAtUtc is not { } preparedAt
            || row.PeriodId is not { } period || period == Guid.Empty
            || row.DebitAccountId is not { } debit || debit == Guid.Empty
            || row.CreditAccountId is not { } credit || credit == Guid.Empty
            || row.ApprovedBy is not null || row.JournalId is not null
            || !AccountingWorkInboxFence.TryAmount(row.SignedTlDifference, out var difference))
        {
            return null;
        }

        return new WorkInboxItemV1(ModuleName, KindName, row.Id, AccountingWorkInboxFence.ShortReference(row.Id),
            preparedAt.ToUniversalTime(), ScreenPath, AccountingWorkInboxFence.Actor(preparedBy),
            [new WorkApprovalRuleResultV1(ApprovalRequiredRule, null, WorkInboxRulesV1.Decimal(difference), null, TlUnit)],
            [
                new WorkApprovalReferenceV1("manufacturing-order",
                    AccountingWorkInboxFence.ShortReference(row.ManufacturingOrderId), row.ManufacturingOrderId, null),
                new WorkApprovalReferenceV1("period", AccountingWorkInboxFence.ShortReference(period), period, null),
                new WorkApprovalReferenceV1("debit-ledger-account", AccountingWorkInboxFence.ShortReference(debit), debit, null),
                new WorkApprovalReferenceV1("credit-ledger-account", AccountingWorkInboxFence.ShortReference(credit), credit, null),
            ]);
    }
}
