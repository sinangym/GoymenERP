using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

/// <summary>
/// W1-A: the pending single-shipment risk exceptions of orders (API-07, SAT-D153…D162) for the Work inbox. One item per
/// exception: the approval (<c>ApproveSalesOrderRiskExceptionV1</c>, route <c>/{orderId}/risk-exceptions/{no}/approval</c>)
/// decides one exception, and one order can hold several pending exceptions (one per shipment, SAT-D154), so the item id is the
/// exception's own id and the order is the first reference and the link. An exception is listed exactly when the approval
/// accepts it: Requested, of an order that is still a draft (not cancelled or closed) and approved or with an approved
/// revision. The exception stores no risk snapshot (limit, exposure): the rule carries no numbers — the order's risk check is
/// recomputed on the order screen by the owner's calculator.
/// </summary>
internal sealed class SalesRiskExceptionWorkInboxSourceV1(
    ISalesWorkInboxStoreV1 store,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
    : SalesWorkInboxSourceBaseV1(companyContext, currentUser, authority)
{
    public const string KindName = "sales.risk-exception.v1";
    public const string RiskExceptionRule = "sales.shipment-risk-exception";

    public override string Kind => KindName;

    public override string ApproverPermission => SalesPermissionManifestV1.OrdersRiskExceptionApprove;

    protected override async Task<(IReadOnlyList<WorkInboxItemV1> Items, bool HasMore)> ReadAsync(Guid companyId, int take,
        bool disclosePrices, CancellationToken cancellationToken)
    {
        var exceptions = await store.ListPendingRiskExceptionsAsync(companyId, take + 1, cancellationToken);
        return Project(companyId, exceptions, take);
    }

    internal static (IReadOnlyList<WorkInboxItemV1> Items, bool HasMore) Project(Guid companyId,
        IReadOnlyList<SalesWorkInboxRiskExceptionV1> exceptions, int take)
    {
        var items = new List<WorkInboxItemV1>(exceptions.Count);
        foreach (var exception in exceptions)
        {
            SalesWorkInboxItemsV1.RequireCompany(companyId, exception.CompanyId);
            if (exception.Id == Guid.Empty || exception.OrderId == Guid.Empty || exception.ExceptionNo < 1)
            {
                throw new InvalidOperationException("Kayıtlı risk istisnası kimliği eksik.");
            }

            var documentNo = SalesWorkInboxItemsV1.RequireLabel(exception.OrderDocumentNo);
            var label = SalesWorkInboxItemsV1.RequireLabel(
                $"{documentNo} #{exception.ExceptionNo.ToString(CultureInfo.InvariantCulture)}");
            items.Add(new WorkInboxItemV1(
                SalesWorkInboxSourceBaseV1.ModuleName,
                KindName,
                exception.Id,
                label,
                exception.RequestedAtUtc.ToUniversalTime(),
                SalesWorkInboxItemsV1.OrderLink(exception.OrderId),
                SalesWorkInboxItemsV1.RequireActor(exception.RequestedBy),
                [new WorkApprovalRuleResultV1(RiskExceptionRule, null, null, null, null)],
                [SalesWorkInboxItemsV1.OrderReference(exception.OrderId, documentNo)]));
        }

        return SalesWorkInboxItemsV1.Page(items, take);
    }
}
