using System.Globalization;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchasingService
{
    private async Task<PurchaseOrderResponse> ReviseMixedSupplierAsync(Guid orderId, RevisePurchaseOrderSupplierRequest mutable,
        PurchasingCommandMetadata metadata, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(metadata); token.ThrowIfCancellationRequested();
        var request = PurchaseMixedSupplierRevisionIntentV1.Freeze(mutable);
        if (store is not IPurchaseMixedSupplierRevisionStoreV1 revision || !revision.MixedSupplierRevisionEntryIsClean
            || serviceApprovalPreparation is not { } services || allocationLinks is null)
            throw new PurchasingServiceException(503, "service_unavailable");
        var permission = bindings.RevisePermission;
        var scope = await RequireAsync(permission, token); await Fence(token);
        var envelope = Envelope(scope, metadata, permission!);
        var hash = PurchasingRequestHash.Compute(PurchasingCommands.OrderSupplierReviseV1, orderId, request);
        var seen = request.SeenAllocations is null ? null : Seen(request.SeenAllocations);
        if (seen is { Count: > 0 }) await RequireStillAsync(scope, bindings.FutureAllocationReleasePermission, token);
        PurchaseMixedSupplierRevisionAnswerV1 answer;
        try
        {
            answer = await revision.ReviseMixedSupplierAsync(orderId, request, envelope, hash, true, Prepare, OrderDetail, Fence,
                (order, transaction, ct) => GuardFutureAllocationsAsync(transaction, order.Id, GeneralLineIds(order), seen,
                    PurchaseFutureAllocationLinkReasonsV1.LinkDropped, request.Reason.Trim(), envelope, ct), token);
        }
        catch (PurchaseFutureAllocationLinkException failure) { throw LinkRefusal(failure); }
        catch (PurchasingStoreException failure) { throw Store(failure); }
        catch (PurchasingVersionConflictException) { return await Refusal(PurchasingProblemCodesV1.VersionConflict); }
        catch (PurchasingRuleException failure) when (failure.Code is PurchaseOrder.SupplierRevisionBlocked or PurchaseOrder.SupplierRevisionNoChange
            or PurchaseOrder.SupplierRevisionNotDraft or PurchaseOrder.SupplierRevisionPriceRequired) { return await Refusal(failure.Code); }
        catch (PurchasingRuleException failure) when (failure.Code is PurchaseOrder.SupplierRevisionInvoiceUnavailable or PurchaseOrder.SupplierRevisionMoneyUnavailable)
        { throw new PurchasingServiceException(503, "service_unavailable"); }
        catch (PurchasingRuleException failure) { throw new PurchasingServiceException(422, "validation_failed", [new("order", failure.Code)]); }
        catch (ArgumentException) { throw new PurchasingServiceException(400, "validation_failed"); }
        catch (OverflowException) { throw new PurchasingServiceException(422, "validation_failed"); }
        if (answer.Refusal is { } refusal) throw new PurchasingServiceException(refusal.Status, refusal.Code, refusal.Errors,
            answer.IsReplay, answer.OriginalCorrelationId, detail: refusal.Detail);
        var original = answer.Receipt ?? throw new PurchasingServiceException(503, "service_unavailable");
        if (original.CompanyId != scope.CompanyId || original.OrderId != orderId || original.ActorId != scope.ActorId.Trim()
            || original.KeyHash != PurchaseMixedOrderApprovalIntentV1.KeyHash(envelope.IdempotencyKey) || original.PayloadHash != hash
            || original.ExpectedVersion.ToString(CultureInfo.InvariantCulture) != request.ExpectedVersion
            || original.ResultVersion != original.ExpectedVersion + 1
            || PurchasingRequestHash.Compute(PurchasingCommands.OrderSupplierReviseV1, orderId, original.FrozenRequest) != hash)
            throw new PurchasingServiceException(503, "service_unavailable");
        await Fence(token); var disclosure = await DiscloseAsync(scope, token); token.ThrowIfCancellationRequested();
        if (!SameMixedContext(scope)) throw new PurchasingServiceException(403, "forbidden");
        return new(disclosure == PurchasingPriceDisclosureV1.Visible ? original.Visible : original.Withheld, answer.IsReplay, original.OriginalCorrelationId);
        Task<PurchaseOrderResponse> Refusal(string code) => TerminalAsync<PurchaseOrderResponse>(scope, permission, envelope, hash, PurchasingCommands.OrderSupplierReviseV1,
            // A recorded winner of a refusal race is not its CURRENT row. Same-key retry enters the genuine original codec.
            new PurchasingTerminalRefusal(409, code, [new("order", code)]), _ => throw new PurchasingServiceException(503, "service_unavailable"), token);
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); await RequireStillAsync(scope, permission, ct); ct.ThrowIfCancellationRequested();
            if (!SameMixedContext(scope)) throw new PurchasingServiceException(403, "forbidden");
        }
        async Task<IReadOnlyList<PurchaseServiceSupplierRevisionPreparationV1>> Prepare(IReadOnlyList<PurchaseServiceOrderState> current, CancellationToken ct)
        {
            await RequireSupplierAsync(scope, permission, request.SupplierPartyId, request.CompanyAccountId, ct);
            await Fence(ct);
            var result = new List<PurchaseServiceSupplierRevisionPreparationV1>();
            foreach (var child in current)
            {
                await Fence(ct);
                var prepared = await services.ReadSupplierRevisionPreparationAsync(child.OrderId, child.Version.ToString(CultureInfo.InvariantCulture),
                    request.SupplierPartyId, request.CompanyAccountId, request.Reason.Trim(), Fence, ct);
                await Fence(ct);
                if (prepared.CompanyId != scope.CompanyId || prepared.ActorId != scope.ActorId || !PurchaseMixedOrderApprovalFactsV1.SameChild(child, prepared.Original))
                    throw new PurchasingServiceException(503, "service_unavailable");
                result.Add(prepared);
            }
            return result.AsReadOnly();
        }
    }
}
