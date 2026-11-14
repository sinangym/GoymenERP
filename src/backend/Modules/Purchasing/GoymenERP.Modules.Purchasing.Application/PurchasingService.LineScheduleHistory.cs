using System.Globalization;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>
/// ALS-D94 / ALS-D81 (F-027): a purchase order line's delivery schedule history (read only). A termin change on an approved order
/// asks no re-approval (ALS-D94), so the change has to stay visible: every recorded revision is answered, oldest first, with its
/// recorder and time. The order is read first (the domain rebuild measures every revision again); the history may not run past the
/// order version read, and its last revision must be the line's current schedule — otherwise the read is a 503, never a guess.
/// </summary>
public sealed partial class PurchasingService
{
    public async Task<PurchaseOrderLineScheduleHistoryView> GetOrderLineScheduleHistoryAsync(Guid orderId, Guid lineId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.ReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (orderId == Guid.Empty || lineId == Guid.Empty)
        {
            throw new PurchasingServiceException(404, "not_found");
        }

        StoredPurchaseOrder stored;
        IReadOnlyList<StoredScheduleRevision> revisions;
        try
        {
            stored = await GuardedAsync(scope, permission, () => store.FindOrderAsync(orderId, cancellationToken), cancellationToken)
                ?? throw new PurchasingServiceException(404, "not_found");
            if (stored.Order.Lines.All(candidate => candidate.Id != lineId))
            {
                throw new PurchasingServiceException(404, "not_found");
            }

            revisions = await GuardedAsync(scope, permission,
                () => store.ListLineScheduleRevisionsAsync(orderId, lineId, cancellationToken), cancellationToken);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        var line = stored.Order.Lines.Single(candidate => candidate.Id == lineId);
        var visible = revisions.Where(revision => revision.OrderVersionAfter <= stored.Order.Version).ToArray();
        RequireHistoryConsistent(visible, line);
        return new PurchaseOrderLineScheduleHistoryView(orderId, lineId, stored.Order.Version.ToString(CultureInfo.InvariantCulture),
            [.. visible.Select(revision => new PurchaseOrderLineScheduleRevisionView(
                revision.OrderVersionAfter.ToString(CultureInfo.InvariantCulture),
                revision.RecordedBy,
                revision.RecordedAtUtc,
                [.. revision.Slices.Select(slice => new PurchaseOrderDeliverySliceView(slice.SliceId, slice.SliceNo,
                    Text(Quantity.Of(slice.Quantity, line.Quantity.Unit)), slice.DeliveryDate))]))]);
    }

    /// <summary>
    /// The history read beside the order must agree with it: versions strictly rising, slices numbered 1..n, and the last revision
    /// exactly the line's current schedule (none recorded = no revision). A disagreement is a store fault (503), not a 404.
    /// </summary>
    private static void RequireHistoryConsistent(IReadOnlyList<StoredScheduleRevision> revisions, PurchaseOrderLine line)
    {
        long previous = 0;
        foreach (var revision in revisions)
        {
            if (revision.OrderVersionAfter <= previous || string.IsNullOrWhiteSpace(revision.RecordedBy) || revision.Slices.Count == 0
                || revision.Slices.Select((slice, index) => slice.SliceNo == index + 1).Contains(false))
            {
                throw HistoryUnavailable();
            }

            previous = revision.OrderVersionAfter;
        }

        var current = line.DeliverySchedule;
        if (revisions.Count == 0)
        {
            if (current.Count != 0)
            {
                throw HistoryUnavailable();
            }

            return;
        }

        var last = revisions[^1].Slices;
        if (last.Count != current.Count || last.Zip(current).Any(pair => pair.First.SliceId != pair.Second.SliceId
            || pair.First.SliceNo != pair.Second.SliceNo || pair.First.Quantity != pair.Second.Quantity.Value
            || pair.First.DeliveryDate != pair.Second.DeliveryDate))
        {
            throw HistoryUnavailable();
        }
    }

    private static PurchasingServiceException HistoryUnavailable() => new(503, "service_unavailable");
}
