using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>
/// API-11 (ALS-D10/ALS-D12, F-017 §2/§3.6, ALS-D87): the short-delivery closure of an APPROVED order's general line and its
/// reopening, under the registered name <c>purchasing.orders.close</c> (registered, not granted; the grant stays a human gate), on
/// the same fences as every Purchasing command (#276: before the replay, after every awaited read, right before the single save;
/// none after the commit), ADR-011 replay first, and the terminal refusals (§7) recorded under the key: a stale version, an order
/// that is not Approved (Draft or Cancelled), a line already closed (close) or not closed (reopen) and a line with nothing left to
/// close. A missing or too long reason is a 422 (not terminal; ALS-D12: every closure and reopening carries a reason), a technical
/// slice id is a 422 (closure acts on the general line), an unknown line a 404 (path resource).
/// <para>
/// Closing records the line's (and its technical slices') whole not-yet-accepted remainder as <c>closed</c>; the ordered quantity,
/// the accepted shares and the request binding stay. Nothing physical or financial is recorded and the closed remainder is not
/// given back to the request line's need. The store writes one <c>purchasing.order-line.closed.v1</c> outbox message per closure
/// and per reopening (<c>Closed</c> true/false) for the Inventory owner (F-017 §3.6). Releasing Inventory future allocations at
/// closure (§3.6, the F17-4 port and the second permission) is not part of this slice; the ALS-D10 in-tolerance suggestion waits
/// on the tolerance band (OPEN-004 / API-95), so every closure asks the actor's reason.
/// </para>
/// </summary>
public sealed partial class PurchasingService
{
    public async Task<PurchaseOrderResponse> CloseOrderLineAsync(Guid orderId, Guid lineId, ClosePurchaseOrderLineRequest request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        return await LineClosureAsync(orderId, lineId, PurchasingCommands.OrderLineCloseV1, request.ExpectedVersion, request.Reason,
            new { lineId, request }, metadata, cancellationToken);
    }

    public async Task<PurchaseOrderResponse> ReopenOrderLineAsync(Guid orderId, Guid lineId, ReopenPurchaseOrderLineRequest request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        return await LineClosureAsync(orderId, lineId, PurchasingCommands.OrderLineReopenV1, request.ExpectedVersion, request.Reason,
            new { lineId, request }, metadata, cancellationToken);
    }

    private async Task<PurchaseOrderResponse> LineClosureAsync(Guid orderId, Guid lineId, string commandName, string expectedVersionText,
        string reason, object hashedRequest, PurchasingCommandMetadata metadata, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var closing = commandName == PurchasingCommands.OrderLineCloseV1;
        var permission = bindings.OrderClosePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var disclosure = await DiscloseAsync(scope, cancellationToken);
        var expectedVersion = ParseVersion(expectedVersionText);
        // The line is a second path identity: it is part of the replay identity beside the order's.
        var requestHash = PurchasingRequestHash.Compute(commandName, orderId, hashedRequest);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            return ReplayedOrder(replay, disclosure);
        }

        if (orderId == Guid.Empty || lineId == Guid.Empty)
        {
            throw new PurchasingServiceException(404, "not_found");
        }

        // The last fence that can still prevent the write; none after the commit.
        await RequireStillAsync(scope, permission, cancellationToken);
        var actor = scope.ActorId.Trim();
        var audit = closing
            ? new PurchasingMutationAudit("purchasing.purchase-order.line-closed.v1", "CloseLine",
                "Satınalma sipariş satırının teslim alınmamış kalanı kapatıldı")
            : new PurchasingMutationAudit("purchasing.purchase-order.line-reopened.v1", "ReopenLine",
                "Satınalma sipariş satırının kapanışı geri alındı");
        PurchasingOrderStoreResult? result;
        try
        {
            result = await store.MutateOrderAsync(envelope, requestHash, commandName, orderId,
                (order, now) =>
                {
                    if (closing)
                    {
                        order.CloseLine(expectedVersion, lineId, actor, reason, now);
                    }
                    else
                    {
                        order.ReopenLine(expectedVersion, lineId, actor, reason, now);
                    }
                },
                audit, cancellationToken);
        }
        catch (PurchasingVersionConflictException)
        {
            return await LineClosureTerminalAsync(scope, permission, envelope, requestHash, commandName, PurchasingProblemCodesV1.VersionConflict,
                "expectedVersion", "version_conflict", disclosure, cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code is PurchasingRuleCodes.OrderCancelled
                                                             or PurchasingRuleCodes.LineNotClosable)
        {
            return await LineClosureTerminalAsync(scope, permission, envelope, requestHash, commandName, PurchasingProblemCodesV1.OrderState,
                "status", exception.Code, disclosure, cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code is PurchasingRuleCodes.LineAlreadyClosed
                                                             or PurchasingRuleCodes.LineNotClosed)
        {
            return await LineClosureTerminalAsync(scope, permission, envelope, requestHash, commandName, PurchasingProblemCodesV1.LineClosureState,
                "line", exception.Code, disclosure, cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code == PurchasingRuleCodes.LineNothingToClose)
        {
            return await LineClosureTerminalAsync(scope, permission, envelope, requestHash, commandName, PurchasingProblemCodesV1.LineNothingToClose,
                "line", exception.Code, disclosure, cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code == PurchasingRuleCodes.LineNotFound)
        {
            throw new PurchasingServiceException(404, "not_found");
        }
        catch (PurchasingRuleException exception) when (exception.Code == PurchasingRuleCodes.LineClosureOnTechnicalLine)
        {
            throw new PurchasingServiceException(422, exception.Code, [new("lineId", exception.Code)]);
        }
        catch (PurchasingRuleException exception) when (exception.Code is PurchasingRuleCodes.LineClosureReasonRequired
                                                             or PurchasingRuleCodes.LineClosureReasonTooLong
                                                             or PurchasingRuleCodes.LineReopenReasonRequired
                                                             or PurchasingRuleCodes.LineReopenReasonTooLong)
        {
            throw new PurchasingServiceException(422, exception.Code, [new("reason", exception.Code)]);
        }
        catch (PurchasingRuleException exception) when (exception.Code != PurchasingRuleCodes.InternalInvariant)
        {
            throw new PurchasingServiceException(422, "validation_failed", [new("line", exception.Code)]);
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

    private Task<PurchaseOrderResponse> LineClosureTerminalAsync(Scope scope, string? permission, PurchasingWriteEnvelope envelope, string requestHash,
        string commandName, string code, string field, string fieldCode, string disclosure, CancellationToken cancellationToken) =>
        TerminalAsync(scope, permission, envelope, requestHash, commandName,
            new PurchasingTerminalRefusal(409, code, [new(field, fieldCode)]), replayed => ReplayedOrder(replayed, disclosure), cancellationToken);
}
