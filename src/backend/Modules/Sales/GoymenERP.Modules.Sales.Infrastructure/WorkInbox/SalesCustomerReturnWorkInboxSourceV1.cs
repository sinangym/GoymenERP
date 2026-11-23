using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

/// <summary>
/// WP4: the customer returns waiting for their approval (API-40, SAT-D290 (3)) for the Work inbox. The approval accepts exactly a
/// PendingApproval return under <c>sales.returns.approve</c> AND the price read (every API-40 command needs it), so a caller
/// without both is Denied. D4: the owner refuses the preparer and the submitter. The item waits from its submission; its
/// label is the customer's document number and the original sales invoice is a reference.
/// </summary>
internal sealed class SalesCustomerReturnWorkInboxSourceV1(
    ISalesWorkInboxApprovalStoreV1 store,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
    : SalesWorkInboxSourceBaseV1(companyContext, currentUser, authority)
{
    public const string KindName = "sales.customer-return.v1";
    public const string ApprovalRule = "sales.customer-return-approval-required";

    public override string Kind => KindName;

    public override string ApproverPermission => SalesPermissionManifestV1.ReturnsApprove;

    protected override IReadOnlyList<string> DecisionCoPermissions => [SalesPermissionManifestV1.OrdersPriceRead];

    protected override async Task<(IReadOnlyList<WorkInboxItemV1> Items, bool HasMore)> ReadAsync(Guid companyId, int take,
        bool disclosePrices, CancellationToken cancellationToken) =>
        Project(companyId, await store.ListPendingCustomerReturnsAsync(companyId, take + 1, cancellationToken), take);

    internal static (IReadOnlyList<WorkInboxItemV1> Items, bool HasMore) Project(Guid companyId,
        IReadOnlyList<SalesWorkInboxCustomerReturnV1> returns, int take)
    {
        var items = new List<WorkInboxItemV1>(returns.Count);
        foreach (var item in returns)
        {
            SalesWorkInboxItemsV1.RequireCompany(companyId, item.CompanyId);
            if (item.Id == Guid.Empty || item.OriginalInvoiceId == Guid.Empty)
            {
                throw new InvalidOperationException("Kayıtlı müşteri iadesi kimliği eksik.");
            }

            var label = SalesWorkInboxItemsV1.RequireLabel(item.CustomerDocumentNo);
            var maker = SalesWorkInboxItemsV1.RequireActor(item.CreatedBy);
            var submitter = SalesWorkInboxItemsV1.RequireActor(item.SubmittedBy);
            var link = SalesWorkInboxItemsV1.CustomerReturnLink(item.Id);
            items.Add(new WorkInboxItemV1(
                SalesWorkInboxSourceBaseV1.ModuleName,
                KindName,
                item.Id,
                label,
                SalesWorkInboxItemsV1.RequireTime(item.SubmittedAtUtc),
                link,
                maker,
                [new WorkApprovalRuleResultV1(ApprovalRule, null, null, null, null)],
                SalesWorkInboxItemsV1.References(
                [
                    new WorkApprovalReferenceV1(SalesWorkInboxItemsV1.CustomerReturnRole, label, item.Id, link),
                    SalesWorkInboxItemsV1.Reference(SalesWorkInboxItemsV1.InvoiceRole, item.OriginalDocumentNo,
                        item.OriginalInvoiceId) is { } original
                        ? original with { Link = SalesWorkInboxItemsV1.InvoicesLink }
                        : null,
                ]),
                SalesWorkInboxItemsV1.Others(maker, submitter)));
        }

        return SalesWorkInboxItemsV1.Page(items, take);
    }
}
