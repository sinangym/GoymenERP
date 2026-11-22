using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

/// <summary>
/// The invoiced shipment COGS proposals (kind 1). The owner binds <c>accounting.normal-journal.draft.approve</c> to their
/// approval (<c>AccountingInventoryCostServiceV1.Permission</c>).
/// </summary>
internal sealed class AccountingInvoicedShipmentCostWorkInboxSourceV1(
    AccountingDbContext dbContext,
    AccountingWorkInboxFence fence) : AccountingInventoryCostWorkInboxSourceBaseV1(dbContext, fence)
{
    public const string KindName = "accounting.invoiced-shipment-cost.v1";
    public const string ScreenPath = "/muhasebe/sevk-smm-onay";

    /// <summary>A prepared invoiced-shipment COGS posting needs a different person's approval.</summary>
    public const string ApprovalRequiredRule = "accounting.invoiced-shipment-cost-approval-required";

    public override string Kind => KindName;

    public override string ApproverPermission => NormalJournalPermissionNamesV1.DraftApprove;

    protected override AccountingInventoryCostKindV1 CostKind => AccountingInventoryCostKindV1.SalesInvoicedShipment;

    protected override string ScreenLink => ScreenPath;

    protected override string RuleCode => ApprovalRequiredRule;
}
