using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

/// <summary>
/// WP4 (SAT-D475): the over-delivery requests the Logistics shipment gate opened, for the Work inbox. A request waits from the
/// moment the approval-band shipment was attempted, while its order is open; it is decided on the order screen under
/// <c>sales.orders.over-delivery.approve</c>. D4 (SAT-D216): the shipper who attempted the shipment is the item's maker and never
/// decides it. The rule carries the owner's recorded quantities only (no price): threshold = the measured (ordered) quantity,
/// actual = the requested shipped total, deviation = their difference, in the order unit (left empty: Sales stores the unit
/// identity, not its code).
/// </summary>
internal sealed class SalesOverDeliveryRequestWorkInboxSourceV1(
    ISalesWorkInboxApprovalStoreV1 store,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
    : SalesWorkInboxSourceBaseV1(companyContext, currentUser, authority)
{
    public const string KindName = "sales.over-delivery-request.v1";
    public const string RequestedRule = "sales.over-delivery-requested";

    public override string Kind => KindName;

    public override string ApproverPermission => SalesPermissionManifestV1.OrdersOverDeliveryApprove;

    protected override async Task<(IReadOnlyList<WorkInboxItemV1> Items, bool HasMore)> ReadAsync(Guid companyId, int take,
        bool disclosePrices, CancellationToken cancellationToken) =>
        Project(companyId, await store.ListPendingOverDeliveryRequestsAsync(companyId, take + 1, cancellationToken), take);

    internal static (IReadOnlyList<WorkInboxItemV1> Items, bool HasMore) Project(Guid companyId,
        IReadOnlyList<SalesWorkInboxOverDeliveryRequestV1> requests, int take)
    {
        var items = new List<WorkInboxItemV1>(requests.Count);
        foreach (var request in requests)
        {
            SalesWorkInboxItemsV1.RequireCompany(companyId, request.CompanyId);
            if (request.Id == Guid.Empty || request.OrderId == Guid.Empty || request.RequestNo < 1
                || !decimal.TryParse(request.Ordered, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var ordered)
                || !decimal.TryParse(request.RequestedTotal, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var total)
                || total <= ordered)
            {
                throw new InvalidOperationException("Kayıtlı fazla teslim talebi eksik ya da tutarsız.");
            }

            var documentNo = SalesWorkInboxItemsV1.RequireLabel(request.OrderDocumentNo);
            var label = SalesWorkInboxItemsV1.RequireLabel(
                $"{documentNo} #{request.RequestNo.ToString(CultureInfo.InvariantCulture)}");
            items.Add(new WorkInboxItemV1(
                SalesWorkInboxSourceBaseV1.ModuleName,
                KindName,
                request.Id,
                label,
                request.RequestedAtUtc.ToUniversalTime(),
                SalesWorkInboxItemsV1.OrderLink(request.OrderId),
                SalesWorkInboxItemsV1.RequireActor(request.RequestedBy),
                [new WorkApprovalRuleResultV1(RequestedRule, WorkInboxRulesV1.Decimal(ordered), WorkInboxRulesV1.Decimal(total),
                    WorkInboxRulesV1.Decimal(total - ordered), null)],
                [SalesWorkInboxItemsV1.OrderReference(request.OrderId, documentNo)]));
        }

        return SalesWorkInboxItemsV1.Page(items, take);
    }
}
