using System.Globalization;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchasingService : IPurchasingSupplierRevisionServiceV1
{
    public Task<PurchaseOrderResponse> ReviseOrderSupplierAsync(Guid orderId, RevisePurchaseOrderSupplierRequest request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SupplierRevisionCommandAsync(orderId, PurchasingCommands.OrderSupplierReviseV1, request.ExpectedVersion,
            PurchasingRequestHash.Compute(PurchasingCommands.OrderSupplierReviseV1, orderId, request), metadata,
            request, null, Guid.Empty, cancellationToken);
    }

    public Task<PurchaseOrderResponse> SetSupplierRevisionPriceAsync(Guid orderId, Guid lineId, SetPurchaseSupplierRevisionPriceRequest request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SupplierRevisionCommandAsync(orderId, PurchasingCommands.OrderSupplierRevisionPriceV1, request.ExpectedVersion,
            PurchasingRequestHash.Compute(PurchasingCommands.OrderSupplierRevisionPriceV1, orderId, new { lineId, request }), metadata,
            null, request.Price, lineId, cancellationToken);
    }

    private async Task<PurchaseOrderResponse> SupplierRevisionCommandAsync(Guid orderId, string command, string version, string hash,
        PurchasingCommandMetadata metadata, RevisePurchaseOrderSupplierRequest? revision, PurchaseOrderLinePriceRequest? price,
        Guid lineId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var permission = bindings.RevisePermission;
        var scope = await RequireAsync(permission, token);
        _ = await DiscloseAsync(scope, token);
        var expected = ParseVersion(version);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, hash, command, token);
        if (replay is not null) return ReplayedOrder(replay, await FreshDisclosureAsync());
        if (orderId == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        if (store is not IPurchasingSupplierRevisionStoreV1 revisions)
            throw new PurchasingServiceException(503, "service_unavailable");
        async Task Fence(CancellationToken cancellationToken)
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            if (revision is not null)
                await RequireSupplierAsync(scope, permission, revision.SupplierPartyId, revision.CompanyAccountId, cancellationToken);
            await RequireStillAsync(scope, permission, cancellationToken);
        }
        try
        {
            await Fence(token);
            PurchasingOrderStoreResult? result;
            if (revision is not null)
            {
                if (revision.SupplierPartyId == Guid.Empty || revision.CompanyAccountId == Guid.Empty || string.IsNullOrWhiteSpace(revision.Reason)
                    || revision.Reason.Trim().Length > PurchaseOrder.SupplierRevisionReasonMaximumLength)
                    throw new PurchasingServiceException(422, "validation_failed");
                // F-017 §3.4 / STOK F017-b: a future allocation lives only on an APPROVED purchase general line, and the revision
                // returns the order to draft. No decision chooses to release them here, so an active allocation refuses the
                // revision (409 purchasing_line_has_allocations, not stored; the link drop releases it first) — fail-closed, and
                // without the Inventory participant the revision is not decided at all (503). NEEDS-SINAN: release-with-confirm.
                if (allocationLinks is null) throw new PurchasingServiceException(503, "service_unavailable");
                var revisionReason = revision.Reason;
                result = await revisions.ReviseSupplierAsync(orderId, expected, revision.SupplierPartyId, revision.CompanyAccountId,
                    revision.Reason, envelope, hash, Fence, token,
                    (order, transaction, cancellationToken) => GuardFutureAllocationsAsync(transaction, order.Id, GeneralLineIds(order),
                        seen: null, PurchaseFutureAllocationLinkReasonsV1.SourceCancelled, revisionReason, envelope, cancellationToken));
            }
            else
            {
                if (price is null || lineId == Guid.Empty) throw new PurchasingServiceException(400, "validation_failed");
                var current = await GuardedAsync(scope, permission, () => store.FindOrderAsync(orderId, token), token)
                    ?? throw new PurchasingServiceException(404, "not_found");
                var line = current.Order.Lines.SingleOrDefault(item => item.Id == lineId)
                    ?? throw new PurchasingServiceException(404, "not_found");
                var parsed = new PurchaseOrderLinePrice(Read(price.UnitPrice, PurchaseOrderMoney.MaximumPriceScale), price.PriceUnitId ?? line.UnitId,
                    Optional(price.DiscountPercent, PurchaseOrderLinePrice.PercentScale), Optional(price.DiscountAmount, PurchaseOrderMoney.MaximumPriceScale),
                    Optional(price.DeferralPercent, PurchaseOrderLinePrice.PercentScale), Optional(price.DeferralAmount, PurchaseOrderMoney.MaximumPriceScale));
                result = await revisions.SetRevisionPriceAsync(orderId, lineId, expected, parsed, envelope, hash, Fence, token);
            }
            return result is null ? throw new PurchasingServiceException(404, "not_found")
                : OrderResponse(result.Order, result.Outcome == PurchasingWriteOutcome.Replayed, result.OriginalCorrelationId, await FreshDisclosureAsync());
        }
        catch (PurchaseFutureAllocationLinkException exception) { throw LinkRefusal(exception); }
        catch (PurchasingVersionConflictException) { return await Refusal(PurchasingProblemCodesV1.VersionConflict); }
        catch (PurchasingRuleException exception) when (exception.Code is PurchaseOrder.SupplierRevisionBlocked or PurchaseOrder.SupplierRevisionNoChange
            or PurchaseOrder.SupplierRevisionNotDraft or PurchaseOrder.SupplierRevisionPriceRequired)
        { return await Refusal(exception.Code); }
        catch (PurchasingRuleException exception) when (exception.Code is PurchaseOrder.SupplierRevisionInvoiceUnavailable or PurchaseOrder.SupplierRevisionMoneyUnavailable)
        { throw new PurchasingServiceException(503, "service_unavailable"); }
        catch (PurchasingRuleException exception) { throw new PurchasingServiceException(422, "validation_failed", [new("order", exception.Code)]); }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, permission, envelope, hash, command, token);
            return won is null ? throw Store(exception) : ReplayedOrder(won, await FreshDisclosureAsync());
        }
        catch (PurchasingStoreException exception) { throw Store(exception); }
        catch (ArgumentException) { throw new PurchasingServiceException(400, "validation_failed"); }

        Task<PurchaseOrderResponse> Refusal(string code) => TerminalAsync(scope, permission, envelope, hash, command,
            new PurchasingTerminalRefusal(409, code, [new("order", code)]), value => ReplayedOrder(value, PurchasingPriceDisclosureV1.Withheld), token);
        async Task<string> FreshDisclosureAsync()
        {
            var current = await DiscloseAsync(scope, token);
            await RequireStillAsync(scope, permission, token);
            return current;
        }
        static decimal? Optional(string? value, int scale) => value is null ? null : Read(value, scale);
        static decimal Read(string? value, int scale)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64 || value[0] == '.' || value[^1] == '.'
                || value.Count(character => character == '.') > 1 || value.Any(character => character != '.' && character is not (>= '0' and <= '9')))
                throw new PurchasingServiceException(400, "validation_failed");
            return ReadPriceText(value, scale, PurchasingRuleCodes.PriceScaleExceeded, "price");
        }
    }

    public async Task<PurchaseSupplierRevisionHistoryView> GetSupplierRevisionsAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        var stored = await GuardedAsync(scope, bindings.ReadPermission, () => store.FindOrderAsync(orderId, cancellationToken), cancellationToken)
            ?? throw new PurchasingServiceException(404, "not_found");
        return new(scope.CompanyId, stored.Order.Id, stored.Order.Version.ToString(CultureInfo.InvariantCulture), stored.Order.SupplierRevision,
            stored.Order.SupplierRevisions.Select(item => new PurchaseSupplierRevisionView(item.Revision, item.OrderVersionAfter.ToString(CultureInfo.InvariantCulture),
                item.PreviousSupplierPartyId, item.SupplierPartyId, item.PreparedBy, item.PreviousApproval.ApprovedBy, item.PreviousApproval.ApprovedAtUtc,
                item.Reason, item.RecordedAtUtc)).ToArray());
    }
}
