using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventoryStore
{
    /// <summary>Called after original replay, in the same physical owner context/transaction, before any stock write.</summary>
    private async Task RequireReceiptDocumentProofAsync(Guid transferId, Guid transferLineId,
        InventoryTransferDocumentReference document, string? expectedDocumentVersion, DateTimeOffset receivedAt,
        decimal receivedKg, string? overageReason, Func<CancellationToken, Task> freshAuthorityFence, CancellationToken token)
    {
        if (document.Kind != InventoryTransferReceiptDocumentV1.Kind || document.LineId is not { } documentLineId
            || !InventoryTransferReceiptDocuments.Version(expectedDocumentVersion, out var expected) || expected != 2)
            throw TransferError(409, InventoryTransferReceiptDocumentV1.NotEffective);
        var history = await dbContext.TransferReceiptDocumentVersions.AsNoTracking()
            .Where(x => x.DocumentId == document.Id).OrderBy(x => x.Version).Take(3).ToListAsync(token);
        await RequireFreshAuthorityAsync(freshAuthorityFence, token);
        if (history.Count == 0) throw TransferError(409, InventoryTransferReceiptDocumentV1.NotEffective);
        var proof = InventoryTransferReceiptDocuments.Project(history);
        if (proof.Status != "Effective") throw TransferError(409, InventoryTransferReceiptDocumentV1.NotEffective);
        if (proof.Version != expectedDocumentVersion)
            throw TransferError(409, InventoryTransferReceiptDocumentV1.VersionConflict);
        var line = proof.Lines.SingleOrDefault(x => x.DocumentLineId == documentLineId && x.TransferLineId == transferLineId);
        if (proof.CompanyId != companyContext.CompanyId || proof.TransferId != transferId || proof.ReceivedAtUtc != receivedAt
            || line is null || !InventoryTransferReceiptDocuments.Quantity(line.ReceivedKg, out var recorded) || recorded != receivedKg
            || line.OverageReason != overageReason)
            throw TransferError(409, InventoryTransferReceiptDocumentV1.BindingConflict);
        var actual = await dbContext.TransferLines.AsNoTracking().SingleOrDefaultAsync(x => x.TransferId == transferId && x.LineId == transferLineId, token);
        var dispatch = await dbContext.TransferDispatchLines.AsNoTracking().SingleOrDefaultAsync(x => x.TransferId == transferId && x.LineId == transferLineId, token);
        await RequireFreshAuthorityAsync(freshAuthorityFence, token);
        if (actual is null || dispatch is null || actual.LabelId != line.LabelId
            || !InventoryTransferReceiptDocuments.Quantity(line.DispatchedKg, out var dispatched) || dispatched != dispatch.ActualKg)
            throw TransferError(409, InventoryTransferReceiptDocumentV1.BindingConflict);
    }
}
