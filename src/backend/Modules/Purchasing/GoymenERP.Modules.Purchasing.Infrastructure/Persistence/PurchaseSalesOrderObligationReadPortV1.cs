using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>SAT-D478: bounded, no-tracking source obligation read. Domain replay verifies request and PO lifecycle;
/// the existing source-share rule and linked-supply composer answer completion without another quantity engine.</summary>
internal sealed class PurchaseSalesOrderObligationReadPortV1(PurchasingDbContext db, ICompanyContext company)
    : IPurchaseSalesOrderObligationReadPortV1
{
    internal const int MaximumRows = 2048;

    public async Task<PurchaseSalesOrderObligationReadV1> ReadAsync(Guid companyId, Guid salesOrderId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        PurchaseSalesOrderObligationReadV1 Unavailable() => new(companyId, salesOrderId, PurchaseSalesOrderObligationStatusV1.Unavailable);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Current(companyId) || salesOrderId == Guid.Empty || freshConsumerFence is null) return Unavailable();
        try
        {
            await Fence();
            var first = await ReadSnapshot();
            var second = await ReadSnapshot();
            if (JsonSerializer.Serialize(first) != JsonSerializer.Serialize(second)) return Unavailable();
            var status = Project(companyId, salesOrderId, first);
            await Fence();
            return new(companyId, salesOrderId, status);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Unavailable(); }

        async Task Fence()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Current(companyId)) throw new UnauthorizedAccessException();
            await freshConsumerFence(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Current(companyId)) throw new UnauthorizedAccessException();
        }
        async Task<T[]> Rows<T>(IQueryable<T> query)
        {
            await Fence();
            var rows = await query.Take(MaximumRows + 1).ToArrayAsync(cancellationToken);
            await Fence();
            if (rows.Length > MaximumRows) throw new InvalidOperationException("Purchasing obligation history exceeds its bounded read.");
            return rows;
        }
        async Task<Snapshot> ReadSnapshot()
        {
            var linked = await Rows(db.RequestLines.AsNoTracking().Where(x => x.CompanyId == companyId
                && x.SourceKind == (int)PurchaseRequestSourceKind.Sales && x.SourceDocumentId == salesOrderId)
                .OrderBy(x => x.RequestId).ThenBy(x => x.LineNo));
            var requestIds = linked.Select(x => x.RequestId).Distinct().ToArray();
            var lineIds = linked.Select(x => x.LineId).ToArray();
            var requests = await Rows(db.Requests.AsNoTracking().Where(x => x.CompanyId == companyId && requestIds.Contains(x.Id)).OrderBy(x => x.Id));
            var requestLines = await Rows(db.RequestLines.AsNoTracking().Where(x => x.CompanyId == companyId && requestIds.Contains(x.RequestId))
                .OrderBy(x => x.RequestId).ThenBy(x => x.LineNo));
            // Include mismatched/orphan bindings so a bad header cannot silently remove an obligation.
            var linkedOrderIds = await Rows(db.OrderLines.AsNoTracking().Where(x => x.CompanyId == companyId
                && lineIds.Contains(x.SourceRequestLineId ?? Guid.Empty)).Select(x => x.OrderId).Distinct().Order());
            var orders = await Rows(db.Orders.AsNoTracking().Where(x => x.CompanyId == companyId
                && (requestIds.Contains(x.SourceRequestId ?? Guid.Empty) || linkedOrderIds.Contains(x.Id))).OrderBy(x => x.Id));
            var orderIds = orders.Select(x => x.Id).Concat(linkedOrderIds).Distinct().ToArray();
            var lines = await Rows(db.OrderLines.AsNoTracking().Where(x => x.CompanyId == companyId && orderIds.Contains(x.OrderId))
                .OrderBy(x => x.OrderId).ThenBy(x => x.LineNo));
            var acceptances = await Rows(db.Acceptances.AsNoTracking().Where(x => x.CompanyId == companyId && orderIds.Contains(x.OrderId))
                .OrderBy(x => x.OrderId).ThenBy(x => x.OrderVersionAfter).ThenBy(x => x.AcceptanceId));
            var slices = await Rows(db.DeliverySlices.AsNoTracking().Where(x => x.CompanyId == companyId && orderIds.Contains(x.OrderId))
                .OrderBy(x => x.OrderId).ThenBy(x => x.OrderVersionAfter).ThenBy(x => x.LineId).ThenBy(x => x.SliceNo));
            var closures = await Rows(db.LineClosureEvents.AsNoTracking().Where(x => x.CompanyId == companyId && orderIds.Contains(x.OrderId))
                .OrderBy(x => x.OrderId).ThenBy(x => x.OrderVersionAfter));
            var rates = await Rows(db.ExchangeRateChanges.AsNoTracking().Where(x => x.CompanyId == companyId && orderIds.Contains(x.OrderId))
                .OrderBy(x => x.OrderId).ThenBy(x => x.OrderVersionAfter));
            var adjustments = await Rows(db.ReceiptAdjustments.AsNoTracking().Where(x => x.CompanyId == companyId && orderIds.Contains(x.OrderId))
                .OrderBy(x => x.OrderId).ThenBy(x => x.OrderVersionAfter));
            var cancellations = await Rows(db.CancellationHistory.AsNoTracking().Where(x => x.CompanyId == companyId && orderIds.Contains(x.OrderId))
                .OrderBy(x => x.OrderId).ThenBy(x => x.OrderVersionAfter));
            var batches = await Rows(db.ReceiptAdjustmentBatches.AsNoTracking().Where(x => x.CompanyId == companyId && orderIds.Contains(x.OrderId))
                .OrderBy(x => x.OrderId).ThenBy(x => x.OrderVersionAfter));
            return new(requests, requestLines, orders, lines, acceptances, slices, closures, rates, adjustments, cancellations, batches);
        }
    }

    private bool Current(Guid id) => id != Guid.Empty && company.IsResolved && company.CompanyId == id;

    internal sealed record Snapshot(PurchaseRequestRow[] Requests, PurchaseRequestLineRow[] RequestLines,
        PurchaseOrderRow[] Orders, PurchaseOrderLineRow[] Lines, PurchaseAcceptanceRow[] Acceptances,
        PurchaseOrderLineDeliverySliceRow[] Slices, PurchaseOrderLineClosureEventRow[] Closures,
        PurchaseOrderExchangeRateChangeRow[] Rates, PurchaseReceiptAdjustmentRow[] Adjustments,
        PurchaseOrderCancellationHistoryRow[] Cancellations, PurchaseReceiptAdjustmentBatchRow[] Batches);

    internal static PurchaseSalesOrderObligationStatusV1 Project(Guid companyId, Guid salesOrderId, Snapshot snapshot)
    {
        var linked = snapshot.RequestLines.Where(x => x.SourceKind == (int)PurchaseRequestSourceKind.Sales
            && x.SourceDocumentId == salesOrderId).ToArray();
        if (linked.Length == 0) return PurchaseSalesOrderObligationStatusV1.Complete;
        var linkedIds = linked.Select(x => x.LineId).ToHashSet();
        if (linked.Any(x => x.CompanyId != companyId || x.SourceDocumentLineId is null || x.SourceDocumentLineId == Guid.Empty)
            || linkedIds.Count != linked.Length || snapshot.RequestLines.Any(x => x.CompanyId != companyId)) throw Invalid();
        var requests = snapshot.Requests.ToDictionary(x => x.Id, x =>
        {
            if (x.CompanyId != companyId) throw Invalid();
            return PurchasingStore.RestoreRequest(x, snapshot.RequestLines.Where(line => line.RequestId == x.Id).OrderBy(line => line.LineNo).ToArray());
        });
        if (linked.Any(x => !requests.ContainsKey(x.RequestId))) throw Invalid();
        var ownLines = snapshot.Lines.Where(x => linkedIds.Contains(x.SourceRequestLineId ?? Guid.Empty)).ToArray();
        foreach (var slice in snapshot.Lines.Where(x => x.Kind == (int)PurchaseOrderLineKind.Technical))
        {
            var parent = snapshot.Lines.SingleOrDefault(x => x.OrderId == slice.OrderId && x.LineId == slice.ParentLineId);
            if (parent is not null && linkedIds.Contains(parent.SourceRequestLineId ?? Guid.Empty)
                && slice.SourceRequestLineId != parent.SourceRequestLineId) throw Invalid();
        }
        var ownOrderIds = ownLines.Select(x => x.OrderId).ToHashSet();
        var orders = snapshot.Orders.Where(x => ownOrderIds.Contains(x.Id)).ToDictionary(x => x.Id, x =>
        {
            if (x.CompanyId != companyId) throw Invalid();
            return PurchasingStore.RestoreOrder(x, snapshot.Lines.Where(line => line.OrderId == x.Id).OrderBy(line => line.LineNo).ToArray(),
                snapshot.Acceptances.Where(row => row.OrderId == x.Id).ToArray(), snapshot.Slices.Where(row => row.OrderId == x.Id).ToArray(),
                snapshot.Closures.Where(row => row.OrderId == x.Id).ToArray(), snapshot.Rates.Where(row => row.OrderId == x.Id).ToArray(),
                snapshot.Adjustments.Where(row => row.OrderId == x.Id).ToArray(), snapshot.Cancellations.Where(row => row.OrderId == x.Id).ToArray(),
                snapshot.Batches.Where(row => row.OrderId == x.Id).ToArray());
        });
        if (ownLines.Any(x => !orders.ContainsKey(x.OrderId))) throw Invalid();
        var open = false;
        foreach (var source in linked)
        {
            var request = requests[source.RequestId];
            var bound = ownLines.Where(x => x.SourceRequestLineId == source.LineId).ToArray();
            foreach (var line in bound)
            {
                var order = orders[line.OrderId];
                if (line.CompanyId != companyId || order.SourceRequestId != source.RequestId
                    || line.UnitId != source.UnitId || line.UnitIsKilogram != source.UnitIsKilogram
                    || line.Kind == (int)PurchaseOrderLineKind.General && line.ProductId != source.ProductId) throw Invalid();
                if (line.Kind == (int)PurchaseOrderLineKind.Technical)
                {
                    var parent = snapshot.Lines.Single(x => x.OrderId == line.OrderId && x.LineId == line.ParentLineId);
                    if (parent.SourceRequestLineId != source.LineId) throw Invalid();
                }
            }
            var committed = bound.Where(x => orders[x.OrderId].Status is PurchaseOrderStatus.Approved or PurchaseOrderStatus.Cancelled).ToArray();
            if (request.Status is PurchaseRequestStatus.Rejected or PurchaseRequestStatus.Withdrawn)
            {
                if (bound.Any(x => orders[x.OrderId].Status == PurchaseOrderStatus.Approved)) throw Invalid();
                continue; // Drafts cannot be approved after their request was terminated (ALS-D2).
            }
            if (request.Status != PurchaseRequestStatus.Approved)
            {
                if (committed.Length != 0) throw Invalid();
                open = true;
                continue;
            }
            var sourced = PurchasingStore.RequestLineOrderedShare(committed, source.LineId);
            if (sourced > source.Quantity) throw Invalid(); // Same source-use ceiling as the approval save.
            if (sourced < source.Quantity || bound.Any(x => orders[x.OrderId].Status == PurchaseOrderStatus.Draft)) open = true;
            var rows = committed.Select(x => new PurchaseSalesLinkedSupplyRow(companyId, x.OrderId, x.LineId, x.LineNo,
                x.Kind, x.ParentLineId, source.SourceDocumentLineId, x.UnitId, x.UnitIsKilogram,
                x.Ordered, x.Technical, x.Accepted, x.Cancelled, x.Closed, x.DefinitiveReturned)).ToArray();
            if (PurchaseSalesLinkedSupplyComposer.Compose(rows, companyId).Count != 0) open = true;
        }
        return open ? PurchaseSalesOrderObligationStatusV1.Open : PurchaseSalesOrderObligationStatusV1.Complete;
    }

    private static InvalidOperationException Invalid() => new("Purchasing source obligation evidence is inconsistent.");
}
