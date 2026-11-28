using System.Globalization;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchasingService : IPurchaseMixedServicePriceRevisionPreviewV1
{
    public async Task<PurchaseMixedServicePriceRevisionPreviewV1> ReadMixedServicePriceRevisionPreviewAsync(Guid id, string expectedVersion,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var permission = bindings.RevisePermission; var scope = await RequireAsync(permission, token);
        var version = PurchaseMixedOrderMembershipV1.SelectedChildVersion(expectedVersion);
        if (store is not IPurchaseMixedServicePriceRevisionPreviewStoreV1 reader) throw new PurchasingServiceException(503, "service_unavailable");
        var result = await reader.ReadMixedServicePriceRevisionPreviewAsync(id, version, Fence, token)
            ?? throw new PurchasingServiceException(404, "not_found");
        await Fence(token);
        if (result.Parent?.Order is not { } parent || parent.CompanyId != scope.CompanyId || parent.Id != id || parent.Version != version
            || parent.Status != PurchaseOrderStatus.Approved || parent.ServiceMemberships.Count == 0 || result.Services is null
            || result.Services.Count != parent.ServiceMemberships.Count || result.Services.Any(x => x is null)
            || result.Services.Select(x => x.OrderId).Distinct().Count() != result.Services.Count)
            throw new PurchasingServiceException(503, "service_unavailable");
        // As in the native view, price disclosure is the final async measurement. No owner/consumer await follows it.
        var disclosure = await DiscloseAsync(scope, token); token.ThrowIfCancellationRequested();
        if (!SameMixedContext(scope)) throw new PurchasingServiceException(403, "forbidden");
        var services = result.Services.Select(child =>
        {
            PurchaseServiceOrder.Validate(child);
            var membership = parent.ServiceMemberships.SingleOrDefault(x => x.ServiceOrderId == child.OrderId);
            if (membership is null || child.Status != PurchaseServiceOrderStatus.Approved || child.CompanyId != scope.CompanyId
                || child.SupplierPartyId != parent.SupplierPartyId || child.CompanyAccountId != result.Parent.CompanyAccountId
                || child.Money.CurrencyId != parent.Money?.CurrencyId) throw new PurchasingServiceException(503, "service_unavailable");
            return new PurchaseMixedOrderServicePreviewV1(PurchaseServiceOrderServiceV1.ApprovalPreviewView(child,
                disclosure == PurchasingPriceDisclosureV1.Visible), membership.PreparedVersion.ToString(CultureInfo.InvariantCulture),
                Array.AsReadOnly(membership.LineIds.ToArray()), child.PreparedBy, child.CreatedAtUtc, child.UpdatedAtUtc, child.PriceRevision,
                child.PartyVersion, child.AccountVersion, new(child.OrderId, child.Version.ToString(CultureInfo.InvariantCulture),
                    Array.AsReadOnly(child.Lines.Select(x => x.LineId).ToArray())));
        }).ToArray();
        return new(scope.CompanyId, OrderDetail(result.Parent, disclosure), Array.AsReadOnly(services));
        async Task Fence(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); await RequireStillAsync(scope, permission, ct); ct.ThrowIfCancellationRequested();
            if (!SameMixedContext(scope)) throw new PurchasingServiceException(403, "forbidden"); }
    }
}
