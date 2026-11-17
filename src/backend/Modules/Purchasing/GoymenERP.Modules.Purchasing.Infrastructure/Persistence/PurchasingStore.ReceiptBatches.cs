using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore
{
    private static void ReplayReceiptAdjustmentBatch(PurchaseOrder order, PurchaseOrderRow header,
        IReadOnlyList<PurchaseOrderLineRow> lines, IReadOnlyList<PurchaseAcceptanceRow> acceptances,
        IReadOnlyList<PurchaseReceiptAdjustmentRow> rows, IReadOnlyDictionary<Guid, PurchaseReceiptAdjustmentBatchRow> batches)
    {
        if (rows[0].BatchId is not { } batchId || batchId == Guid.Empty || !batches.TryGetValue(batchId, out var batch)
            || batch.CompanyId != header.CompanyId || batch.OrderId != header.Id || batch.LineCount != rows.Count
            || batch.OrderVersionAfter != rows[0].OrderVersionAfter)
            throw new InvalidOperationException("Receipt batch does not match its immutable header.");
        var inputs = new List<PurchaseReceiptAdjustmentInput>(rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var line = lines.SingleOrDefault(item => item.LineId == row.LineId);
            var original = acceptances.SingleOrDefault(item => item.AcceptanceId == row.AcceptanceId);
            if (row.BatchId != batchId || row.BatchLineNo != index + 1 || row.CompanyId != header.CompanyId
                || row.OrderId != header.Id || row.OrderVersionAfter != batch.OrderVersionAfter
                || row.RecordedBy != batch.RecordedBy || row.Reason != batch.Reason
                || row.RecordedAtUtc != batch.RecordedAtUtc || row.CorrelationId != batch.CorrelationId
                || line is null || original is null || original.CompanyId != header.CompanyId
                || original.OrderId != header.Id || original.LineId != line.LineId)
                throw new InvalidOperationException("Receipt batch rows are incomplete or differ from their original acceptance.");
            if (row.NewAcceptanceId is { } newId)
            {
                var replacement = acceptances.SingleOrDefault(item => item.AcceptanceId == newId);
                if (replacement is null || replacement.CompanyId != header.CompanyId || replacement.OrderId != header.Id
                    || replacement.LineId != line.LineId || replacement.Quantity != row.NewAcceptedQuantity
                    || replacement.OrderVersionAfter != batch.OrderVersionAfter || replacement.RecordedAtUtc != batch.RecordedAtUtc
                    || replacement.ActorId != batch.RecordedBy || replacement.CorrelationId != batch.CorrelationId)
                    throw new InvalidOperationException("Batch replacement receipt differs from its immutable correction.");
            }
            var unit = UnitOf(line.UnitIsKilogram);
            inputs.Add(new(row.AdjustmentId, row.AcceptanceId, Quantity.Of(row.UndoQuantity, unit), row.IsSupplierReturn,
                row.ReplacementExpected, row.NewAcceptanceId, Quantity.Of(row.NewAcceptedQuantity, unit)));
        }
        var rebuilt = order.AdjustReceipts(order.Version, inputs, batch.RecordedBy, batch.Reason, batch.RecordedAtUtc);
        if (order.Version != batch.OrderVersionAfter || rebuilt.Adjustments.Count != rows.Count)
            throw new InvalidOperationException("Receipt batch version differs from the rebuilt owner.");
        for (var index = 0; index < rows.Count; index++)
        {
            var expected = rows[index]; var actual = rebuilt.Adjustments[index];
            if (actual.AdjustmentId != expected.AdjustmentId || actual.OrderVersionAfter != expected.OrderVersionAfter
                || actual.Revived != expected.Revived || actual.NetOpenedQuantity.Value != expected.NetOpenedQuantity)
                throw new InvalidOperationException("Receipt batch net result differs from its immutable history.");
        }
    }
}
