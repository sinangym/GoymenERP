using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

/// <summary>
/// The purchase invoice cost difference proposals (kind 2). The owner binds <c>accounting.journal.reverse.approve</c> to
/// their approval (<c>AccountingInventoryCostServiceV1.Permission</c>).
/// </summary>
internal sealed class AccountingPurchaseCostDifferenceWorkInboxSourceV1(
    AccountingDbContext dbContext,
    AccountingWorkInboxFence fence) : AccountingInventoryCostWorkInboxSourceBaseV1(dbContext, fence)
{
    public const string KindName = "accounting.purchase-cost-difference.v1";
    public const string ScreenPath = "/muhasebe/alis-maliyet-farki-onay";

    /// <summary>A prepared purchase invoice cost difference posting needs a different person's approval.</summary>
    public const string ApprovalRequiredRule = "accounting.purchase-cost-difference-approval-required";

    public override string Kind => KindName;

    public override string ApproverPermission => AccountingJournalReversalPermissionNamesV1.Approve;

    protected override AccountingInventoryCostKindV1 CostKind => AccountingInventoryCostKindV1.PurchaseInvoiceDifference;

    protected override string ScreenLink => ScreenPath;

    protected override string RuleCode => ApprovalRequiredRule;
}
