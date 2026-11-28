using System.Globalization;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchasingService : IPurchaseMixedOrderMembershipServiceV1
{
    public async Task<PurchaseOrderResponse> BindServicesAsync(Guid orderId, BindPurchaseOrderServicesRequest mutableRequest,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata); cancellationToken.ThrowIfCancellationRequested();
        var request = PurchaseMixedOrderMembershipV1.Freeze(mutableRequest);
        if (orderId == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        if (store is not IPurchaseMixedOrderMembershipStoreV1 memberships || !memberships.MembershipEntryIsClean)
            throw new PurchasingServiceException(503, "service_unavailable");
        // Same native Draft edit authority as SetOrderLineSchedule / supplier revision. No Read/Create/Approve co-grant.
        var permission = bindings.RevisePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var envelope = Envelope(scope, metadata, permission!);
        var hash = PurchasingRequestHash.Compute(PurchaseMixedOrderMembershipV1.Command, orderId, request);
        PurchaseMixedOrderMembershipAnswerV1 result;
        try
        {
            result = await memberships.BindServicesAsync(orderId, request, envelope, hash, true, OrderDetail, Fence, cancellationToken);
        }
        catch (PurchasingVersionConflictException) { throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict); }
        catch (PurchasingRuleException failure) { throw new PurchasingServiceException(409, failure.Code); }
        catch (PurchasingStoreException failure) { throw Store(failure); }
        catch (ArgumentException) { throw new PurchasingServiceException(422, "validation_failed"); }
        var original = result.Receipt;
        if (original is null || original.CompanyId != scope.CompanyId || original.OrderId != orderId
            || original.ActorId != scope.ActorId.Trim() || original.KeyHash != PurchaseMixedOrderApprovalIntentV1.KeyHash(envelope.IdempotencyKey)
            || original.PayloadHash != hash || original.FrozenRequest is null
            || PurchasingRequestHash.Compute(PurchaseMixedOrderMembershipV1.Command, orderId, original.FrozenRequest) != hash
            || original.FrozenRequest.ExpectedVersion != request.ExpectedVersion || string.IsNullOrWhiteSpace(original.OriginalCorrelationId)
            || original.Visible?.Order is not { } header || header.Id != orderId || header.CompanyId != scope.CompanyId
            || original.Withheld?.Order is not { } hidden || hidden.Id != orderId || hidden.CompanyId != scope.CompanyId
            || header.Version != (PurchaseMixedOrderApprovalIntentV1.Version(request.ExpectedVersion) + 1).ToString(CultureInfo.InvariantCulture))
            throw new PurchasingServiceException(503, "service_unavailable");
        await Fence(cancellationToken);
        // Every other asynchronous fence precedes the native final disclosure. Historical result is never replaced by a current row.
        var disclosure = await DiscloseAsync(scope, cancellationToken); cancellationToken.ThrowIfCancellationRequested();
        if (!SameMixedContext(scope)) throw new PurchasingServiceException(403, "forbidden");
        return new(disclosure == PurchasingPriceDisclosureV1.Visible ? original.Visible : original.Withheld,
            result.IsReplay, original.OriginalCorrelationId);

        async Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); await RequireStillAsync(scope, permission, token); token.ThrowIfCancellationRequested();
            if (!SameMixedContext(scope)) throw new PurchasingServiceException(403, "forbidden");
        }
    }
}
