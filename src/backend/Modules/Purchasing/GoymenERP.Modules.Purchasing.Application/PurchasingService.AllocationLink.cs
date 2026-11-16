using System.Data;
using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>
/// F-017 §3.5 F17-4 (ALS-D83/ALS-D85, Sinan 2026-11-12 EK17): the purchasing side of the future allocation link.
/// <list type="bullet">
/// <item>The list (GET …/future-allocations) reads the order's active allocations from the Inventory owner under
/// <c>purchasing.future-allocation.release</c>; nothing changes.</item>
/// <item>The link drop (ALS-D83) releases ONE allocation's whole remainder with <c>purchasing-link-dropped</c>, under the order's line
/// locks, with idempotency, an audit row and a required reason; the order version and the purchase quantities do not change.</item>
/// <item>The drop-and-cancel / drop-and-close (ALS-D85, §3.6) compare the confirmed set exactly, then release each with
/// <c>source-cancelled</c> / <c>purchase-line-closed</c> and cancel/close in the SAME transaction, under both names.</item>
/// </list>
/// Purchasing never writes the owner's tables: every write goes through <see cref="IPurchaseFutureAllocationLinkParticipantV1"/>.
/// When the host did not wire it, the plain cancel/closure stay unguarded (as before) and the F17-4 routes answer 503.
/// </summary>
public sealed partial class PurchasingService
{
    public async Task<PurchaseOrderFutureAllocationLinksView> GetOrderFutureAllocationsAsync(Guid orderId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.FutureAllocationReleasePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (orderId == Guid.Empty)
        {
            throw new PurchasingServiceException(404, "not_found");
        }

        var links = allocationLinks ?? throw new PurchasingServiceException(503, "service_unavailable");
        PurchasingOrderLinkRead<IReadOnlyList<PurchaseFutureAllocationLinkV1>>? read;
        try
        {
            read = await GuardedAsync(scope, permission, () => store.ReadOrderFutureAllocationsAsync(orderId,
                (order, transaction, token) => links.ListActiveAsync(transaction, order.Id, GeneralLineIds(order), token),
                cancellationToken), cancellationToken);
        }
        catch (PurchaseFutureAllocationLinkException exception)
        {
            throw LinkRefusal(exception);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        if (read is null)
        {
            throw new PurchasingServiceException(404, "not_found");
        }

        var found = read.Order.Order;
        var lineOrder = found.Lines.Select((line, index) => (line.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index);
        return new PurchaseOrderFutureAllocationLinksView(found.Id, found.Version.ToString(CultureInfo.InvariantCulture),
            [.. read.Value
                .Where(link => link.PurchaseOrderId == found.Id && lineOrder.ContainsKey(link.PurchaseOrderLineId))
                .OrderBy(link => lineOrder[link.PurchaseOrderLineId]).ThenBy(link => link.CreatedAtUtc).ThenBy(link => link.AllocationId)
                .Select(LinkView)]);
    }

    public async Task<PurchaseFutureAllocationReleaseResponse> ReleaseOrderFutureAllocationAsync(Guid orderId, Guid allocationId,
        ReleasePurchaseFutureAllocationRequest request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.FutureAllocationReleasePermission;
        const string commandName = PurchasingCommands.OrderFutureAllocationReleaseV1;
        var scope = await RequireAsync(permission, cancellationToken);
        var expectedAllocationVersion = ParseAllocationVersion(request.ExpectedAllocationVersion);
        // The allocation is a second path identity: it is part of the replay identity beside the order's.
        var requestHash = PurchasingRequestHash.Compute(commandName, orderId, new { allocationId, request });
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
        if (replay is not null)
        {
            return ReplayedRelease(replay, allocationId);
        }

        if (orderId == Guid.Empty || allocationId == Guid.Empty)
        {
            throw new PurchasingServiceException(404, "not_found");
        }

        var reasonText = RequireReleaseReason(request.ReasonText);
        var links = allocationLinks ?? throw new PurchasingServiceException(503, "service_unavailable");

        // The last fence that can still prevent the write; none after the commit.
        await RequireStillAsync(scope, permission, cancellationToken);
        PurchasingOrderStoreResult? result;
        try
        {
            result = await store.ReleaseOrderFutureAllocationAsync(envelope, requestHash, orderId,
                async (order, transaction, token) =>
                {
                    var active = await links.ListActiveAsync(transaction, order.Id, GeneralLineIds(order), token);
                    var link = active.FirstOrDefault(candidate => candidate.AllocationId == allocationId && candidate.PurchaseOrderId == order.Id)
                        ?? throw new PurchaseFutureAllocationLinkException(404, "not_found", terminal: false);
                    var released = await links.ReleaseAsync(transaction, new PurchaseFutureAllocationLinkReleaseV1(order.Id, allocationId,
                        expectedAllocationVersion, PurchaseFutureAllocationLinkReasonsV1.LinkDropped, reasonText, envelope), token);
                    return new PurchasingLinkAuditSnapshots(Snapshot(link), Snapshot(released));
                },
                new PurchasingMutationAudit("purchasing.purchase-order.future-allocation-released.v1", "ReleaseFutureAllocation",
                    "Satınalma sipariş satırındaki gelecek tedarik ayırma bağı düşürüldü"), cancellationToken);
        }
        catch (PurchaseFutureAllocationLinkException exception) when (exception.Terminal)
        {
            return await TerminalAsync(scope, permission, envelope, requestHash, commandName,
                new PurchasingTerminalRefusal(409, exception.Code, [new("expectedAllocationVersion", exception.Code)]),
                replayed => ReplayedRelease(replayed, allocationId), cancellationToken);
        }
        catch (PurchaseFutureAllocationLinkException exception)
        {
            throw LinkRefusal(exception);
        }
        catch (ArgumentException)
        {
            throw new PurchasingServiceException(400, "validation_failed");
        }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            return won is null ? throw Store(exception) : ReplayedRelease(won, allocationId);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        return result is null
            ? throw new PurchasingServiceException(404, "not_found")
            : ReleaseResponse(result.Order.Order, allocationId, result.Outcome == PurchasingWriteOutcome.Replayed, result.OriginalCorrelationId);
    }

    public async Task<PurchaseOrderResponse> CancelOrderWithAllocationReleaseAsync(Guid orderId,
        CancelPurchaseOrderWithAllocationReleaseRequest request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        return await CancelOrderCoreAsync(orderId, PurchasingCommands.OrderCancelWithAllocationReleaseV1, request.ExpectedVersion, request.Reason,
            request, Seen(request.SeenAllocations), metadata, cancellationToken);
    }

    public async Task<PurchaseOrderResponse> CloseOrderLineWithAllocationReleaseAsync(Guid orderId, Guid lineId,
        ClosePurchaseOrderLineWithAllocationReleaseRequest request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        return await LineClosureAsync(orderId, lineId, PurchasingCommands.OrderLineCloseWithAllocationReleaseV1, request.ExpectedVersion,
            request.Reason, new { lineId, request }, metadata, cancellationToken, Seen(request.SeenAllocations));
    }

    /// <summary>
    /// ALS-D85, inside the store's transaction after every general line lock of the order was taken: with no confirmed set an active
    /// allocation refuses the write (409 <c>purchasing_line_has_allocations</c>); with one, the set (ids and versions) must be exactly
    /// the active set (409 <c>purchasing_future_allocation_set_changed</c>), then each is released in id order.
    /// </summary>
    private async Task GuardFutureAllocationsAsync(IDbTransaction transaction, Guid orderId, IReadOnlyList<Guid> lineIds,
        IReadOnlyList<PurchaseFutureAllocationSeenV1>? seen, string reasonCode, string reasonText, PurchasingWriteEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var links = allocationLinks ?? throw new PurchaseFutureAllocationLinkException(503, "service_unavailable", terminal: false);
        var active = await links.ListActiveAsync(transaction, orderId, lineIds, cancellationToken);
        if (seen is null)
        {
            if (active.Count > 0)
            {
                throw new PurchaseFutureAllocationLinkException(409, PurchasingProblemCodesV1.LineHasAllocations, terminal: false);
            }

            return;
        }

        if (!SameSet(active, seen))
        {
            throw new PurchaseFutureAllocationLinkException(409, PurchasingProblemCodesV1.FutureAllocationSetChanged, terminal: false);
        }

        foreach (var link in active.OrderBy(candidate => candidate.AllocationId))
        {
            await links.ReleaseAsync(transaction, new PurchaseFutureAllocationLinkReleaseV1(orderId, link.AllocationId, link.Version,
                reasonCode, reasonText, envelope), cancellationToken);
        }
    }

    private static bool SameSet(IReadOnlyList<PurchaseFutureAllocationLinkV1> active, IReadOnlyList<PurchaseFutureAllocationSeenV1> seen)
    {
        if (active.Count != seen.Count)
        {
            return false;
        }

        var expected = seen.ToDictionary(item => item.AllocationId, item => item.ExpectedAllocationVersion);
        return active.All(link => expected.TryGetValue(link.AllocationId, out var version) && version == link.Version);
    }

    private static IReadOnlyList<Guid> GeneralLineIds(PurchaseOrder order) =>
        [.. order.Lines.Where(line => line.Kind == PurchaseOrderLineKind.General).Select(line => line.Id)];

    private static IReadOnlyList<PurchaseFutureAllocationSeenV1> Seen(IReadOnlyList<PurchaseFutureAllocationSeenRequest>? seen)
    {
        if (seen is null)
        {
            throw new PurchasingServiceException(400, "validation_failed", [new("seenAllocations", "validation_failed")]);
        }

        var ids = new HashSet<Guid>();
        var result = new List<PurchaseFutureAllocationSeenV1>(seen.Count);
        foreach (var item in seen)
        {
            if (item is null || item.AllocationId == Guid.Empty || !ids.Add(item.AllocationId))
            {
                throw new PurchasingServiceException(400, "validation_failed", [new("seenAllocations", "validation_failed")]);
            }

            result.Add(new PurchaseFutureAllocationSeenV1(item.AllocationId, ParseAllocationVersion(item.ExpectedAllocationVersion)));
        }

        return result;
    }

    private static long ParseAllocationVersion(string? text)
    {
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
        {
            throw new PurchasingServiceException(400, "validation_failed", [new("expectedAllocationVersion", "validation_failed")]);
        }

        return version;
    }

    /// <summary>ALS-D83: the reason is required (not blank) and at most the order reason bound; it is never cut.</summary>
    private static string RequireReleaseReason(string? reasonText)
    {
        if (string.IsNullOrWhiteSpace(reasonText))
        {
            throw new PurchasingServiceException(422, PurchasingProblemCodesV1.FutureAllocationReleaseReasonRequired, [new("reasonText", PurchasingProblemCodesV1.FutureAllocationReleaseReasonRequired)]);
        }

        if (reasonText.Length > PurchaseOrder.MaximumReasonLength)
        {
            throw new PurchasingServiceException(422, PurchasingProblemCodesV1.FutureAllocationReleaseReasonTooLong, [new("reasonText", PurchasingProblemCodesV1.FutureAllocationReleaseReasonTooLong)]);
        }

        return reasonText;
    }

    private static PurchasingServiceException LinkRefusal(PurchaseFutureAllocationLinkException exception) => exception.Status switch
    {
        404 => new PurchasingServiceException(404, "not_found"),
        409 => new PurchasingServiceException(409, exception.Code, [new("futureAllocations", exception.Code)]),
        _ => new PurchasingServiceException(503, "service_unavailable"),
    };

    private static PurchaseOrderFutureAllocationLinkView LinkView(PurchaseFutureAllocationLinkV1 link) =>
        new(link.AllocationId, link.Version.ToString(CultureInfo.InvariantCulture), link.PurchaseOrderLineId, link.SalesOrderId,
            link.SalesOrderLineId, link.UnitId, link.AllocatedQuantity, link.RemainingQuantity, link.CreatedAtUtc);

    private static string Snapshot(PurchaseFutureAllocationLinkV1 link) => JsonSerializer.Serialize(LinkView(link));

    private static PurchaseFutureAllocationReleaseResponse ReleaseResponse(PurchaseOrder order, Guid allocationId, bool isReplay,
        string originalCorrelationId) =>
        new(order.Id, order.Version.ToString(CultureInfo.InvariantCulture), allocationId, isReplay, originalCorrelationId);

    private static PurchaseFutureAllocationReleaseResponse ReplayedRelease(PurchasingReplay replay, Guid allocationId)
    {
        ThrowIfRefusal(replay);
        return replay.Order is { } stored
            ? ReleaseResponse(stored.Order, allocationId, isReplay: true, replay.OriginalCorrelationId)
            : throw new PurchasingServiceException(503, "service_unavailable");
    }
}
