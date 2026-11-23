using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

/// <summary>
/// WP4: the subcontract service invoices waiting for their approval (SAT-D380 lifecycle) for the Work inbox. The approval accepts
/// exactly a PendingApproval invoice under <c>sales.invoices.approve</c> AND the price read (the command's own fences), so a caller
/// without both is Denied. D4: the owner refuses the preparer and the submitter (<c>RequireOtherPreparer</c>). The lifecycle panel
/// lives on the order screen, so the link opens the invoice's order, which is also a reference.
/// </summary>
internal sealed class SalesSubcontractInvoiceWorkInboxSourceV1(
    ISalesWorkInboxApprovalStoreV1 store,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
    : SalesWorkInboxSourceBaseV1(companyContext, currentUser, authority)
{
    public const string KindName = "sales.subcontract-invoice.v1";
    public const string ApprovalRule = "sales.subcontract-invoice-approval-required";

    public override string Kind => KindName;

    public override string ApproverPermission => SalesPermissionManifestV1.InvoicesApprove;

    protected override IReadOnlyList<string> DecisionCoPermissions => [SalesPermissionManifestV1.OrdersPriceRead];

    protected override async Task<(IReadOnlyList<WorkInboxItemV1> Items, bool HasMore)> ReadAsync(Guid companyId, int take,
        bool disclosePrices, CancellationToken cancellationToken) =>
        Project(companyId, await store.ListPendingSubcontractInvoicesAsync(companyId, take + 1, cancellationToken), take);

    internal static (IReadOnlyList<WorkInboxItemV1> Items, bool HasMore) Project(Guid companyId,
        IReadOnlyList<SalesWorkInboxInvoiceV1> invoices, int take)
    {
        var items = new List<WorkInboxItemV1>(invoices.Count);
        foreach (var invoice in invoices)
        {
            SalesWorkInboxItemsV1.RequireCompany(companyId, invoice.CompanyId);
            if (invoice.Id == Guid.Empty || invoice.OrderId is not { } orderId || orderId == Guid.Empty)
            {
                throw new InvalidOperationException("Kayıtlı fason hizmet faturası kimliği eksik.");
            }

            var documentNo = SalesWorkInboxItemsV1.RequireLabel(invoice.DocumentNo);
            var orderNo = SalesWorkInboxItemsV1.RequireLabel(invoice.OrderDocumentNo);
            var maker = SalesWorkInboxItemsV1.RequireActor(invoice.CreatedBy);
            var submitter = SalesWorkInboxItemsV1.RequireActor(invoice.SubmittedBy);
            items.Add(new WorkInboxItemV1(
                SalesWorkInboxSourceBaseV1.ModuleName,
                KindName,
                invoice.Id,
                documentNo,
                SalesWorkInboxItemsV1.RequireTime(invoice.SubmittedAtUtc),
                SalesWorkInboxItemsV1.OrderLink(orderId),
                maker,
                [new WorkApprovalRuleResultV1(ApprovalRule, null, null, null, null)],
                [
                    new WorkApprovalReferenceV1(SalesWorkInboxItemsV1.SubcontractInvoiceRole, documentNo, invoice.Id, null),
                    SalesWorkInboxItemsV1.OrderReference(orderId, orderNo),
                ],
                SalesWorkInboxItemsV1.Others(maker, submitter)));
        }

        return SalesWorkInboxItemsV1.Page(items, take);
    }
}
