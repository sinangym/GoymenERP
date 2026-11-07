using System.Globalization;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>
/// A1-3 (plan API-10; No.161 §2, Sinan 2026-10-27): the cancellation of an APPROVED order's whole not-yet-accepted remainder,
/// under its own name <c>purchasing.orders.cancel</c>, on the same fences as every Purchasing command (#276: before the replay,
/// after every awaited read, right before the single save; none after the commit), ADR-011 replay first, and the terminal
/// refusals (§7) recorded under the key: a stale version, an order that is not Approved (Draft or already Cancelled) and an
/// order with nothing left to cancel. A missing or too long reason is a 422 (not terminal). The impact preview reads the same
/// record under the same name and changes nothing.
/// <para>
/// ALS-D66…D68 (EK10, Sinan 2026-11-03): the name is proposed to the approvers of ALS-D53 (Satın Alma Müdürü, Genel Müdür, Admin)
/// and is registered only — the grant stays a human gate; the cancellation is ONE step with a reason and an audit record, with no
/// second approval and no maker ≠ checker rule (ALS-D67); the cancelled remainder is not given back to the request line's need
/// (ALS-D68: a new request is opened instead).
/// </para>
/// </summary>
public sealed partial class PurchasingService
{
    public async Task<PurchaseOrderResponse> CancelOrderAsync(Guid orderId, CancelPurchaseOrderRequest request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.OrderCancelPermission;
        const string commandName = PurchasingCommands.OrderCancelV1;
        var scope = await RequireAsync(permission, cancellationToken);
        var disclosure = await DiscloseAsync(scope, cancellationToken);
        var expectedVersion = ParseVersion(request.ExpectedVersion);
        var requestHash = PurchasingRequestHash.Compute(commandName, orderId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            return ReplayedOrder(replay, disclosure);
        }

        if (orderId == Guid.Empty)
        {
            throw new PurchasingServiceException(404, "not_found");
        }

        // The last fence that can still prevent the write; none after the commit.
        await RequireStillAsync(scope, permission, cancellationToken);
        var actor = scope.ActorId.Trim();
        PurchasingOrderStoreResult? result;
        try
        {
            result = await store.MutateOrderAsync(envelope, requestHash, commandName, orderId,
                (order, now) => order.Cancel(expectedVersion, actor, request.Reason, now),
                new PurchasingMutationAudit("purchasing.purchase-order.remainder-cancelled.v1", "Cancel",
                    "Satınalma siparişinin kabul edilmemiş kalanı iptal edildi"), cancellationToken);
        }
        catch (PurchasingVersionConflictException)
        {
            return await CancellationTerminalAsync(scope, permission, envelope, requestHash, PurchasingProblemCodesV1.VersionConflict,
                "expectedVersion", "version_conflict", disclosure, cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code == PurchasingRuleCodes.OrderNotCancellable)
        {
            return await CancellationTerminalAsync(scope, permission, envelope, requestHash, PurchasingProblemCodesV1.OrderState,
                "status", exception.Code, disclosure, cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code == PurchasingRuleCodes.OrderNothingToCancel)
        {
            return await CancellationTerminalAsync(scope, permission, envelope, requestHash, PurchasingProblemCodesV1.OrderNothingToCancel,
                "lines", exception.Code, disclosure, cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code is PurchasingRuleCodes.OrderCancellationReasonRequired
                                                             or PurchasingRuleCodes.OrderCancellationReasonTooLong)
        {
            throw new PurchasingServiceException(422, exception.Code, [new("reason", exception.Code)]);
        }
        catch (PurchasingRuleException exception)
        {
            throw new PurchasingServiceException(422, "validation_failed", [new("order", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new PurchasingServiceException(400, "validation_failed");
        }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            return won is null ? throw Store(exception) : ReplayedOrder(won, disclosure);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        return result is null
            ? throw new PurchasingServiceException(404, "not_found")
            : OrderResponse(result.Order, result.Outcome == PurchasingWriteOutcome.Replayed, result.OriginalCorrelationId, disclosure);
    }

    public async Task<PurchaseOrderCancelImpactView> GetOrderCancelImpactAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.OrderCancelPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (orderId == Guid.Empty)
        {
            throw new PurchasingServiceException(404, "not_found");
        }

        StoredPurchaseOrder? stored;
        try
        {
            stored = await GuardedAsync(scope, permission, () => store.FindOrderAsync(orderId, cancellationToken), cancellationToken);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        return stored is null ? throw new PurchasingServiceException(404, "not_found") : CancelImpact(stored.Order);
    }

    /// <summary>
    /// The preview is the domain's own rule read without a write: the blocker is exactly the refusal <see cref="PurchaseOrder.Cancel"/>
    /// would raise for the state (the reason is the caller's), and the remainder to cancel is each line's remaining.
    /// </summary>
    private static PurchaseOrderCancelImpactView CancelImpact(PurchaseOrder order)
    {
        string? blocker = order.Status != PurchaseOrderStatus.Approved
            ? PurchasingRuleCodes.OrderNotCancellable
            : order.Lines.All(line => line.RemainingQuantity.IsZero) ? PurchasingRuleCodes.OrderNothingToCancel : null;
        return new PurchaseOrderCancelImpactView(order.Id, order.Status.ToString(), order.Version.ToString(CultureInfo.InvariantCulture),
            order.SourceRequestId, blocker is null, blocker, order.Lines.Any(line => !line.AcceptedQuantity.IsZero),
            [.. order.Lines.Select((line, index) => new PurchaseOrderCancelImpactLineView(line.Id, index + 1, line.Kind.ToString(), line.ProductId,
                line.UnitId, Text(line.CommittedQuantity), Text(line.AcceptedQuantity), blocker is null ? Text(line.RemainingQuantity) : "0"))]);
    }

    private Task<PurchaseOrderResponse> CancellationTerminalAsync(Scope scope, string? permission, PurchasingWriteEnvelope envelope, string requestHash,
        string code, string field, string fieldCode, string disclosure, CancellationToken cancellationToken) =>
        TerminalAsync(scope, permission, envelope, requestHash, PurchasingCommands.OrderCancelV1,
            new PurchasingTerminalRefusal(409, code, [new(field, fieldCode)]), replayed => ReplayedOrder(replayed, disclosure), cancellationToken);
}
