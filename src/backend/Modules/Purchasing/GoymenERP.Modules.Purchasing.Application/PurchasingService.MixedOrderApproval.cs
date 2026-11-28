using System.Globalization;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchasingService
{
    private async Task<PurchaseOrderResponse> ApproveMixedOrderAsync(Guid orderId, ApprovePurchaseOrderRequest mutableRequest,
        PurchasingCommandMetadata metadata, CancellationToken token)
    {
        // Snapshot public intent and caller cleanliness before the first authority await.
        var request = PurchaseMixedOrderApprovalIntentV1.Freeze(mutableRequest);
        if (store is not IPurchaseMixedOrderApprovalStoreV1 mixed || serviceApprovalPreparation is not { } services)
            throw new PurchasingServiceException(503, "service_unavailable");
        var clean = mixed.MixedApprovalEntryIsClean;
        if (!clean) throw new PurchasingServiceException(503, "service_unavailable");
        var scope = await RequireAsync(bindings.ApprovePermission, token); token.ThrowIfCancellationRequested();
        var envelope = Envelope(scope, metadata, bindings.ApprovePermission!);
        var hash = PurchasingRequestHash.Compute(PurchasingCommands.OrderApproveV1, orderId, request);
        PurchaseMixedOrderApprovalStoreAnswerV1 answer;
        try
        {
            answer = await mixed.ApproveMixedOrderAsync(orderId, request, envelope, hash, clean, Prepare, OrderDetail, Fence, token);
        }
        catch (PurchasingVersionConflictException)
        { return await Terminal(new(409, PurchasingProblemCodesV1.VersionConflict, [new("expectedVersion", "version_conflict")])); }
        catch (PurchasingRuleException failure) when (failure.Code is PurchasingRuleCodes.OrderNotDraft or PurchasingRuleCodes.OrderExchangeRateRequired
            or PurchasingRuleCodes.OrderSameActor or PurchasingRuleCodes.OrderMakerUnknown)
        {
            var code = failure.Code switch
            {
                PurchasingRuleCodes.OrderNotDraft => PurchasingProblemCodesV1.OrderState,
                PurchasingRuleCodes.OrderExchangeRateRequired => PurchasingProblemCodesV1.OrderExchangeRateRequired,
                PurchasingRuleCodes.OrderSameActor => PurchasingProblemCodesV1.OrderSameActor,
                _ => PurchasingProblemCodesV1.OrderMakerUnknown,
            };
            return await Terminal(new(409, code, [new("order", failure.Code)]));
        }
        catch (PurchasingRuleException failure) when (failure.Code == PurchasingRuleCodes.TheoreticalKilogramsMissing)
        { throw new PurchasingServiceException(422, PurchasingProblemCodesV1.TheoreticalKilogramsMissing, [new("lines", failure.Code)]); }
        catch (PurchasingRuleException failure)
        { throw new PurchasingServiceException(422, "validation_failed", [new("order", failure.Code)]); }
        catch (PurchasingStoreException failure) when (failure.Code is PurchasingStoreCodes.SourceRequestNotApproved or PurchasingStoreCodes.RequestLineOverSourced)
        {
            return await Terminal(new(409, failure.Code == PurchasingStoreCodes.SourceRequestNotApproved
                ? PurchasingProblemCodesV1.RequestState : PurchasingProblemCodesV1.RequestLineOverSourced,
                failure.Errors.Count > 0 ? failure.Errors : [new("order", failure.Code)]));
        }
        catch (PurchasingStoreException failure) { throw Store(failure); }
        catch (PurchasingServiceException failure) when (failure.Status == 409)
        { return await Terminal(new(failure.Status, failure.Code, failure.Errors)); }
        catch (ArgumentException) { throw new PurchasingServiceException(400, "validation_failed"); }
        if (answer.Refusal is { } refusal)
            throw new PurchasingServiceException(refusal.Status, refusal.Code, refusal.Errors, isReplay: answer.IsReplay,
                originalCorrelationId: answer.OriginalCorrelationId, detail: refusal.Detail);
        var original = answer.Receipt ?? throw new PurchasingServiceException(503, "service_unavailable");
        if (original.CompanyId != scope.CompanyId || original.OrderId != orderId || original.ActorId != scope.ActorId.Trim()
            || original.PayloadHash != hash || original.IdempotencyKeyHash != PurchaseMixedOrderApprovalIntentV1.KeyHash(envelope.IdempotencyKey)
            || original.OriginalCorrelationId != answer.OriginalCorrelationId || original.Original is null
            || original.ExpectedVersion.ToString(CultureInfo.InvariantCulture) != request.ExpectedVersion
            || original.ResultVersion != original.ExpectedVersion + 1)
            throw new PurchasingServiceException(503, "service_unavailable");
        await Fence(token);
        // Final native price disclosure follows every other async owner/consumer fence. Historical facts stay historical.
        var disclosure = await DiscloseAsync(scope, token); token.ThrowIfCancellationRequested();
        if (!SameMixedContext(scope)) throw new PurchasingServiceException(403, "forbidden");
        return new(disclosure == PurchasingPriceDisclosureV1.Visible ? original.Original.Visible : original.Original.Withheld,
            answer.IsReplay, answer.OriginalCorrelationId);

        async Task Fence(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); await RequireStillAsync(scope, bindings.ApprovePermission, ct); ct.ThrowIfCancellationRequested(); if (!SameMixedContext(scope)) throw new PurchasingServiceException(403, "forbidden"); }
        async Task<PurchaseMixedOrderApprovalPreparationV1> Prepare(StoredPurchaseOrder parent,
            IReadOnlyList<PurchaseServiceOrderState> current, CancellationToken ct)
        {
            var prepared = new List<PurchaseServiceOrderState>();
            foreach (var child in current)
            {
                await Fence(ct);
                var result = await (child.Status == PurchaseServiceOrderStatus.Approved && parent.Order.ServicePriceRevisions.Count > 0
                    ? services.ReadRetainedApprovalPreparationAsync(child.OrderId, child.Version.ToString(CultureInfo.InvariantCulture), Fence, ct)
                    : services.ReadApprovalPreparationAsync(child.OrderId, child.Version.ToString(CultureInfo.InvariantCulture), Fence, ct));
                await Fence(ct);
                if (result.CompanyId != scope.CompanyId || result.ActorId != scope.ActorId || result.Prepared is null
                    || !PurchaseMixedOrderApprovalFactsV1.SameChild(child, result.Prepared, true)) throw new PurchasingServiceException(503, "service_unavailable");
                prepared.Add(result.Prepared);
            }
            var kg = await ReadMixedTheoreticalKilogramsAsync(scope, parent, ct); await Fence(ct);
            return new(prepared.AsReadOnly(), kg);
        }
        Task<PurchaseOrderResponse> Terminal(PurchasingTerminalRefusal refusal) => TerminalAsync<PurchaseOrderResponse>(scope, bindings.ApprovePermission,
            envelope, hash, PurchasingCommands.OrderApproveV1, refusal,
            _ => throw new PurchasingServiceException(503, "service_unavailable"), token); // A racing original success is reconciled only by its immutable mixed receipt.
    }
}
