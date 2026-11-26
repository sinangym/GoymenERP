using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

/// <summary>
/// WP4: the sales invoices waiting for their approval (API-33, SAT-D88, SAT-D204) for the Work inbox. The approval
/// (<c>POST …/invoices/{id}/approval</c>) accepts exactly a PendingApproval invoice, under <c>sales.invoices.approve</c> AND the
/// price read (<see cref="SalesPermissionManifestV1.OrdersPriceRead"/>, the command's own second fence), so a caller without
/// both cannot decide and is Denied. D4: the owner refuses the preparer AND the submitter (<c>RequireNotPreparer</c>): the
/// preparer is the item's maker, the submitter (when another person) its excluded decider. The item waits from its submission.
/// </summary>
internal sealed class SalesInvoiceWorkInboxSourceV1(
    ISalesWorkInboxApprovalStoreV1 store,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
    : SalesWorkInboxSourceBaseV1(companyContext, currentUser, authority), IWorkApprovalDecisionSourceV1
{
    public const string KindName = "sales.invoice.v1";
    public const string ApprovalRule = "sales.invoice-approval-required";

    public override string Kind => KindName;

    public override string ApproverPermission => SalesPermissionManifestV1.InvoicesApprove;

    protected override IReadOnlyList<string> DecisionCoPermissions => [SalesPermissionManifestV1.OrdersPriceRead];

    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken cancellationToken)
    {
        if (itemId == Guid.Empty || !await DecisionAllowedAsync(context, cancellationToken)) return null;
        var row = await store.FindInvoiceDecisionAsync(context.CompanyId, itemId, cancellationToken);
        if (!await DecisionAllowedAsync(context, cancellationToken) || row is null || row.Version < 1) return null;
        var item = Project(context.CompanyId, [row.Invoice], 1).Items.Single();
        return new(context.CompanyId, item, row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), row.Pending);
    }

    protected override async Task<(IReadOnlyList<WorkInboxItemV1> Items, bool HasMore)> ReadAsync(Guid companyId, int take,
        bool disclosePrices, CancellationToken cancellationToken) =>
        Project(companyId, await store.ListPendingInvoicesAsync(companyId, take + 1, cancellationToken), take);

    internal static (IReadOnlyList<WorkInboxItemV1> Items, bool HasMore) Project(Guid companyId,
        IReadOnlyList<SalesWorkInboxInvoiceV1> invoices, int take)
    {
        var items = new List<WorkInboxItemV1>(invoices.Count);
        foreach (var invoice in invoices)
        {
            SalesWorkInboxItemsV1.RequireCompany(companyId, invoice.CompanyId);
            if (invoice.Id == Guid.Empty)
            {
                throw new InvalidOperationException("Kayıtlı satış faturası kimliği eksik.");
            }

            var documentNo = SalesWorkInboxItemsV1.RequireLabel(invoice.DocumentNo);
            var maker = SalesWorkInboxItemsV1.RequireActor(invoice.CreatedBy);
            var submitter = SalesWorkInboxItemsV1.RequireActor(invoice.SubmittedBy);
            items.Add(new WorkInboxItemV1(
                SalesWorkInboxSourceBaseV1.ModuleName,
                KindName,
                invoice.Id,
                documentNo,
                SalesWorkInboxItemsV1.RequireTime(invoice.SubmittedAtUtc),
                $"/onaylarim/{KindName}/{invoice.Id:D}",
                maker,
                [new WorkApprovalRuleResultV1(ApprovalRule, null, null, null, null)],
                [new WorkApprovalReferenceV1(SalesWorkInboxItemsV1.InvoiceRole, documentNo, invoice.Id,
                    SalesWorkInboxItemsV1.InvoicesLink)],
                SalesWorkInboxItemsV1.Others(maker, submitter)));
        }

        return SalesWorkInboxItemsV1.Page(items, take);
    }
}
