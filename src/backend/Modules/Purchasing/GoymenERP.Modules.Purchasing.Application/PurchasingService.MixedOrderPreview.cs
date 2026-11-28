using System.Globalization;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchasingService : IPurchaseMixedOrderApprovalPreviewV1
{
    public async Task<PurchaseMixedOrderApprovalPreviewV1> ReadMixedOrderApprovalPreviewAsync(Guid orderId, string expectedVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ApprovePermission, cancellationToken);
        var version = ParseVersion(expectedVersion);
        var servicePreparation = serviceApprovalPreparation;
        if (store is not IPurchaseMixedOrderApprovalPreviewStoreV1 reader || servicePreparation is null)
            throw new PurchasingServiceException(503, "service_unavailable");
        var result = await reader.ReadMixedOrderApprovalPreviewAsync(orderId, version, Prepare, Fence, cancellationToken)
            ?? throw new PurchasingServiceException(404, "not_found");
        await Fence(cancellationToken);
        if (result.Parent?.Order is not { } actual || actual.CompanyId != scope.CompanyId || actual.Id != orderId
            || actual.Version != version || result.Services is null)
            throw new PurchasingServiceException(503, "service_unavailable");
        // The existing native price gate is the final asynchronous release measurement; only synchronous projection follows.
        var disclosure = await DiscloseAsync(scope, cancellationToken); cancellationToken.ThrowIfCancellationRequested();
        if (!SameMixedContext(scope)) throw new PurchasingServiceException(403, "forbidden");
        return ProjectMixedOrderPreview(result, disclosure);

        async Task Fence(CancellationToken token)
        { token.ThrowIfCancellationRequested(); await RequireStillAsync(scope, bindings.ApprovePermission, token); token.ThrowIfCancellationRequested(); }
        async Task<PurchaseMixedOrderPreparedPreviewV1> Prepare(StoredPurchaseOrder parent,
            IReadOnlyList<PurchaseServiceOrderState> current, CancellationToken token)
        {
            if (parent.Order.CompanyId != scope.CompanyId || parent.Order.Id != orderId || parent.Order.Version != version
                || current.Any(x => x is null || x.CompanyId != scope.CompanyId))
                throw new PurchasingServiceException(503, "service_unavailable");
            var prepared = new List<PurchaseServiceOrderState>();
            foreach (var child in current)
            {
                await Fence(token);
                var answer = await (child.Status == PurchaseServiceOrderStatus.Approved && parent.Order.ServicePriceRevisions.Count > 0
                    ? servicePreparation.ReadRetainedApprovalPreparationAsync(child.OrderId, child.Version.ToString(CultureInfo.InvariantCulture), Fence, token)
                    : servicePreparation.ReadApprovalPreparationAsync(child.OrderId, child.Version.ToString(CultureInfo.InvariantCulture), Fence, token));
                await Fence(token);
                if (answer.CompanyId != scope.CompanyId || answer.ActorId != scope.ActorId || answer.Prepared is null
                    || !PurchaseMixedOrderApprovalFactsV1.SameChild(child, answer.Prepared, allowSupplierRefresh: true))
                    throw new PurchasingServiceException(503, "service_unavailable");
                prepared.Add(answer.Prepared);
            }
            return new(parent, prepared.AsReadOnly());
        }
    }

    private bool SameMixedContext(Scope scope) => companyContext.IsResolved && companyContext.CompanyId == scope.CompanyId
        && currentUser.IsAuthenticated && currentUser.UserId == scope.ActorId && currentUser.CanAccessCompany(scope.CompanyId);

    internal static PurchaseMixedOrderApprovalPreviewV1 ProjectMixedOrderPreview(PurchaseMixedOrderPreparedPreviewV1 result, string disclosure)
    {
        var parent = result.Parent; var order = parent.Order;
        if (order.Status != PurchaseOrderStatus.Draft || order.ServiceMemberships.Count == 0
            || result.Services.Count != order.ServiceMemberships.Count
            || result.Services.Select(x => x.OrderId).Distinct().Count() != result.Services.Count)
            throw new PurchasingServiceException(503, "service_unavailable");
        var services = result.Services.Select(child =>
        {
            PurchaseServiceOrder.Validate(child);
            var membership = order.ServiceMemberships.SingleOrDefault(x => x.ServiceOrderId == child.OrderId);
            if (membership is null || child.CompanyId != order.CompanyId || child.SupplierPartyId != order.SupplierPartyId
                || child.CompanyAccountId != parent.CompanyAccountId || child.Money.CurrencyId != order.Money?.CurrencyId
                || (child.Status != PurchaseServiceOrderStatus.Draft && (order.ServicePriceRevisions.Count == 0 || child.Status != PurchaseServiceOrderStatus.Approved)))
                throw new PurchasingServiceException(409, "version_conflict");
            return new PurchaseMixedOrderServicePreviewV1(PurchaseServiceOrderServiceV1.ApprovalPreviewView(child,
                disclosure == PurchasingPriceDisclosureV1.Visible), membership.PreparedVersion.ToString(CultureInfo.InvariantCulture),
                Array.AsReadOnly(membership.LineIds.ToArray()), child.PreparedBy, child.CreatedAtUtc, child.UpdatedAtUtc,
                child.PriceRevision, child.PartyVersion, child.AccountVersion,
                new(child.OrderId, child.Version.ToString(CultureInfo.InvariantCulture), Array.AsReadOnly(child.Lines.Select(x => x.LineId).ToArray())));
        }).ToArray();
        return new(order.CompanyId, OrderDetail(parent, disclosure), Array.AsReadOnly(services));
    }
}
