using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

/// <summary>
/// WP4: the sales requests waiting for their approval (API-21, No 160, 161) for the Work inbox. A request is born Submitted and
/// waits from its creation; the approval (<c>POST …/requests/{id}/approval</c>, <c>sales.requests.approve</c>) accepts exactly a
/// Submitted request. The owner names NO maker ≠ checker rule for a request (SalesRequest: "none is decided for a sales
/// request"), so the item names no maker and no excluded decider (YOL-D23: Work excludes no one the owner does not). The rule
/// carries no numbers.
/// </summary>
internal sealed class SalesRequestWorkInboxSourceV1(
    ISalesWorkInboxApprovalStoreV1 store,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
    : SalesWorkInboxSourceBaseV1(companyContext, currentUser, authority)
{
    public const string KindName = "sales.request.v1";
    public const string ApprovalRule = "sales.request-approval-required";

    public override string Kind => KindName;

    public override string ApproverPermission => SalesPermissionManifestV1.RequestsApprove;

    protected override async Task<(IReadOnlyList<WorkInboxItemV1> Items, bool HasMore)> ReadAsync(Guid companyId, int take,
        bool disclosePrices, CancellationToken cancellationToken) =>
        Project(companyId, await store.ListSubmittedRequestsAsync(companyId, take + 1, cancellationToken), take);

    internal static (IReadOnlyList<WorkInboxItemV1> Items, bool HasMore) Project(Guid companyId,
        IReadOnlyList<SalesWorkInboxRequestV1> requests, int take)
    {
        var items = new List<WorkInboxItemV1>(requests.Count);
        foreach (var request in requests)
        {
            SalesWorkInboxItemsV1.RequireCompany(companyId, request.CompanyId);
            if (request.Id == Guid.Empty)
            {
                throw new InvalidOperationException("Kayıtlı satış talebi kimliği eksik.");
            }

            var documentNo = SalesWorkInboxItemsV1.RequireLabel(request.DocumentNo);
            var link = SalesWorkInboxItemsV1.RequestLink(request.Id);
            items.Add(new WorkInboxItemV1(
                SalesWorkInboxSourceBaseV1.ModuleName,
                KindName,
                request.Id,
                documentNo,
                request.CreatedAtUtc.ToUniversalTime(),
                link,
                PreparedBy: null,
                [new WorkApprovalRuleResultV1(ApprovalRule, null, null, null, null)],
                [new WorkApprovalReferenceV1(SalesWorkInboxItemsV1.RequestRole, documentNo, request.Id, link)]));
        }

        return SalesWorkInboxItemsV1.Page(items, take);
    }
}
