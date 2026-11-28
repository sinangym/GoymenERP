using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchasingService : IPurchaseMixedServicePriceRevisionServiceV1
{
    public async Task<PurchaseOrderResponse> ReviseMixedServicePricesAsync(Guid orderId, RevisePurchaseMixedServicePricesRequest mutable,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(metadata);
        var request = PurchaseMixedServicePriceIntentV1.Freeze(mutable);
        if (store is not IPurchaseMixedServicePriceRevisionStoreV1 prices || !prices.MixedServicePriceEntryIsClean)
            throw new PurchasingServiceException(503, "service_unavailable");
        var permission = bindings.RevisePermission; var scope = await RequireAsync(permission, cancellationToken);
        var envelope = Envelope(scope, metadata, permission!);
        var hash = PurchasingRequestHash.Compute(PurchaseMixedServicePriceIntentV1.Command, orderId, request);
        PurchaseMixedServicePriceAnswerV1 answer;
        try
        {
            answer = await prices.ReviseMixedServicePricesAsync(orderId, request, envelope, hash, true, OrderDetail, Fence, cancellationToken);
        }
        catch (PurchasingVersionConflictException) { return await Refusal(PurchasingProblemCodesV1.VersionConflict); }
        catch (PurchaseServiceOrderException failure) { return await Refusal(failure.Code); }
        catch (PurchasingRuleException failure) { return await Refusal(failure.Code); }
        catch (PurchasingStoreException failure) { throw Store(failure); }
        catch (ArgumentException) { throw new PurchasingServiceException(422, "validation_failed"); }
        catch (OverflowException) { throw new PurchasingServiceException(422, "validation_failed"); }
        if (answer.Refusal is { } refusal) throw new PurchasingServiceException(refusal.Status, refusal.Code, refusal.Errors,
            answer.IsReplay, answer.OriginalCorrelationId, detail: refusal.Detail);
        var original = answer.Receipt;
        if (original is null || original.CompanyId != scope.CompanyId || original.OrderId != orderId || original.ActorId != scope.ActorId.Trim()
            || original.KeyHash != PurchaseMixedOrderApprovalIntentV1.KeyHash(envelope.IdempotencyKey) || original.PayloadHash != hash
            || original.FrozenRequest is null || PurchasingRequestHash.Compute(PurchaseMixedServicePriceIntentV1.Command, orderId, original.FrozenRequest) != hash
            || original.ExpectedVersion != PurchaseMixedOrderApprovalIntentV1.Version(request.ExpectedVersion)
            || original.ResultVersion != checked(original.ExpectedVersion + 1) || string.IsNullOrWhiteSpace(original.OriginalCorrelationId))
            throw new PurchasingServiceException(503, "service_unavailable");
        await Fence(cancellationToken);
        var disclosure = await DiscloseAsync(scope, cancellationToken); cancellationToken.ThrowIfCancellationRequested();
        if (!SameMixedContext(scope)) throw new PurchasingServiceException(403, "forbidden");
        return new(disclosure == PurchasingPriceDisclosureV1.Visible ? original.Visible : original.Withheld,
            answer.IsReplay, original.OriginalCorrelationId);
        Task<PurchaseOrderResponse> Refusal(string code) => TerminalAsync<PurchaseOrderResponse>(scope, permission, envelope, hash,
            PurchaseMixedServicePriceIntentV1.Command, new PurchasingTerminalRefusal(409, code, [new("order", code)]),
            _ => throw new PurchasingServiceException(503, "service_unavailable"), cancellationToken);
        async Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); await RequireStillAsync(scope, permission, token); token.ThrowIfCancellationRequested();
            if (!SameMixedContext(scope)) throw new PurchasingServiceException(403, "forbidden");
        }
    }
}
