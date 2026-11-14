using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>
/// A3 API-30 (ALS-D33, ALS-D21 "sıra", master §7.3): a purchase order line's delivery schedule (termin dilimleri) — the quantity
/// slices of a general line with their delivery dates, summing exactly to the line's ordered quantity. Recorded under the revise name
/// <c>purchasing.orders.revise</c> (ALS-D94, Sinan #66 6824883445: a termin change is a revision of the order; the plan's API-12/API-17
/// name, registered, not granted; the create name alone is refused 403) on the same fences as every Purchasing command (#276), ADR-011
/// replay first. On an Approved order a termin-only change keeps the order Approved and asks no re-approval (ALS-D94); a slice total
/// other than the line's quantity is the domain's non-terminal 422 on <c>slices</c>, so the ordered total never changes here. Each POST replaces the line's schedule whole as a new revision
/// at the next order version; the earlier revisions stay (append only). A Draft or an Approved order is scheduled; a Cancelled one is
/// refused (terminal 409), as is a stale version. The schedule rules are the domain's 422; an unknown line is a 404 (the line is a
/// path resource). Delay flagging (API-32) and the schedule screen are not part of this slice.
/// </summary>
public sealed partial class PurchasingService
{
    public async Task<PurchaseOrderResponse> SetOrderLineScheduleAsync(Guid orderId, Guid lineId, SetPurchaseOrderLineScheduleRequest request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.RevisePermission;
        const string commandName = PurchasingCommands.OrderLineScheduleV1;
        var scope = await RequireAsync(permission, cancellationToken);
        var disclosure = await DiscloseAsync(scope, cancellationToken);
        var expectedVersion = ParseVersion(request.ExpectedVersion);
        // The line is a second path identity: it is part of the replay identity beside the order's.
        var requestHash = PurchasingRequestHash.Compute(commandName, orderId, new { lineId, request });
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

        var slices = request.Slices ?? throw new PurchasingServiceException(400, "validation_failed");
        // Ö2 "never rounded": each quantity text is validated lexically before it becomes a value (the line's unit is the order's).
        var values = new decimal[slices.Count];
        for (var index = 0; index < slices.Count; index++)
        {
            var slice = slices[index] ?? throw new PurchasingServiceException(400, "validation_failed");
            try
            {
                values[index] = ReadQuantityText(slice.Quantity, $"slices[{index}]");
            }
            catch (PurchasingServiceException exception) when (exception.Code == PurchasingProblemCodesV1.UnsupportedUnit)
            {
                // A slice carries no unit of its own (the line's is used): the scale refusal names the slice's quantity.
                throw new PurchasingServiceException(exception.Status, exception.Code,
                    [new($"slices[{index}].quantity", PurchaseUnitCodes.QuantityScaleExceeded)]);
            }
        }

        // The last fence that can still prevent the write; none after the commit.
        await RequireStillAsync(scope, permission, cancellationToken);
        var actor = scope.ActorId.Trim();
        PurchasingOrderStoreResult? result;
        try
        {
            result = await store.MutateOrderAsync(envelope, requestHash, commandName, orderId,
                (order, now) =>
                {
                    var line = order.Lines.FirstOrDefault(candidate => candidate.Id == lineId);
                    if (order.Version != expectedVersion || order.Status == PurchaseOrderStatus.Cancelled || line is null)
                    {
                        // The domain's own refusal order answers first (version → cancelled → unknown line): a stale version, a
                        // cancelled order or an unknown line is its 409/404, never the 422 scale refusal below. Called with no slices,
                        // the domain refuses on one of those three before it reads the (empty) set.
                        order.SetLineDeliverySchedule(expectedVersion, lineId, [], actor, now);
                    }

                    var unit = line!.Quantity.Unit;
                    // The order line's own precedent (VerifyUnitAsync): a quantity finer than the unit's scale (a fraction of a
                    // piece) is the 422 scale refusal, named here on the slice's quantity, never a 400.
                    for (var index = 0; index < values.Length; index++)
                    {
                        if (Quantity.SignificantDecimals(values[index]) > Quantity.AllowedScale(unit))
                        {
                            throw new PurchasingServiceException(422, PurchasingProblemCodesV1.UnsupportedUnit,
                                [new($"slices[{index}].quantity", PurchaseUnitCodes.QuantityScaleExceeded)]);
                        }
                    }

                    order.SetLineDeliverySchedule(expectedVersion, lineId,
                        [.. slices.Select((slice, index) => new PurchaseOrderDeliverySliceInput(slice.SliceId, Quantity.Of(values[index], unit),
                            slice.DeliveryDate))], actor, now);
                },
                new PurchasingMutationAudit("purchasing.purchase-order.line-schedule-set.v1", "Schedule",
                    "Satınalma sipariş satırının termin dilimleri kaydedildi"), cancellationToken);
        }
        catch (PurchasingVersionConflictException)
        {
            return await ScheduleTerminalAsync(scope, permission, envelope, requestHash, PurchasingProblemCodesV1.VersionConflict,
                "expectedVersion", "version_conflict", disclosure, cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code == PurchasingRuleCodes.OrderCancelled)
        {
            return await ScheduleTerminalAsync(scope, permission, envelope, requestHash, PurchasingProblemCodesV1.OrderState,
                "status", exception.Code, disclosure, cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code == PurchasingRuleCodes.LineNotFound)
        {
            throw new PurchasingServiceException(404, "not_found");
        }
        catch (PurchasingRuleException exception) when (exception.Code != PurchasingRuleCodes.InternalInvariant)
        {
            throw new PurchasingServiceException(422, "validation_failed", [new("slices", exception.Code)]);
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

    private Task<PurchaseOrderResponse> ScheduleTerminalAsync(Scope scope, string? permission, PurchasingWriteEnvelope envelope, string requestHash,
        string code, string field, string fieldCode, string disclosure, CancellationToken cancellationToken) =>
        TerminalAsync(scope, permission, envelope, requestHash, PurchasingCommands.OrderLineScheduleV1,
            new PurchasingTerminalRefusal(409, code, [new(field, fieldCode)]), replayed => ReplayedOrder(replayed, disclosure), cancellationToken);
}
