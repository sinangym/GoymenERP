using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidInventoryStockCountProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind != InventoryStockCount) return snapshot.InventoryStockCountProof is null;
        if (snapshot.InventoryStockCountProof is not { Count: { } count } proof
            || proof.CompanyId != snapshot.CompanyId || proof.CountId != snapshot.Item.ItemId
            || count.LabelId == Guid.Empty || count.OwnerId == Guid.Empty || count.CustodianId == Guid.Empty
            || count.LocationId == Guid.Empty || count.ProductId == Guid.Empty
            || count.OwnerKind is not ("Company" or "Counterparty") || count.CustodianKind is not ("Company" or "Counterparty")
            || count.Block is not ("Free" or "Blocked") || !StockCountLabelVersion(count.LabelVersion)
            || !StockCountKg(count.BookKg, false) || !StockCountKg(count.CountedKg, false) || !StockCountKg(count.DifferenceKg, true)
            || count.DifferenceKg == "0" || count.RecordedBy != snapshot.Item.PreparedBy
            || string.IsNullOrWhiteSpace(count.RecordedBy) || count.RecordedBy.Length > 128
            || string.IsNullOrWhiteSpace(count.Reason) || count.Reason.Length > 1000
            || count.CountedAtUtc == default || count.CountedAtUtc.Offset != TimeSpan.Zero
            || count.RecordedAtUtc == default || count.RecordedAtUtc.Offset != TimeSpan.Zero
            || snapshot.Item.WaitingSinceUtc != count.RecordedAtUtc
            || count.BookPieceCount is < 0 || count.CountedPieceCount is < 0
            || (count.BookPieceCount is null) != (count.CountedPieceCount is null)
            || proof.Status is not ("PendingApproval" or "Approved" or "Rejected")
            || snapshot.Pending != (proof.Status == "PendingApproval") || snapshot.SourceVersion != (snapshot.Pending ? "1" : "2")
            || snapshot.Item.ExcludedDeciders is { Count: > 0 }
            || snapshot.Item.References.Count != 3
            || snapshot.Item.References.Count(row => row.Role == "inventory-label" && row.RecordId == count.LabelId && row.Link is null) != 1
            || snapshot.Item.References.Count(row => row.Role == "product" && row.RecordId == count.ProductId) != 1
            || snapshot.Item.References.Count(row => row.Role == "location" && row.RecordId == count.LocationId) != 1
            || snapshot.Item.Scope is not null)
            return false;
        return true;
    }

    private static bool StockCountLabelVersion(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
        out var version) && version > 0 && version.ToString(CultureInfo.InvariantCulture) == value;
    private static bool StockCountKg(string value, bool signed) => decimal.TryParse(value,
        signed ? NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint : NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture, out var kg) && (signed || kg >= 0) && kg.ToString("0.###", CultureInfo.InvariantCulture) == value;

    private async Task<WorkDecisionResultV1> DecideInventoryStockCountAsync(Guid id, WorkDecisionRequestV1 request,
        string key, WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (stockCounts is null || snapshot.InventoryStockCountProof is not { } proof) throw new WorkDecisionException(503);
        try
        {
            var answer = await stockCounts.DecideAsync(id,
                new(request.Approve ? InventoryStockCountDecisionKindV1.Approve : InventoryStockCountDecisionKindV1.Reject, request.Reason!),
                new(actor, string.Empty, [], correlation.CorrelationId, correlation.CausationId, key, "Web"), token);
            if (answer is not { CommandReceiptProven: true, Count: { Decision: { } decision } count }
                || count.CompanyId != snapshot.CompanyId || count.CountId != id
                || !snapshot.Pending && !answer.IsReplay
                || !snapshot.Pending && proof.Status != count.Status
                || count.Status != (request.Approve ? "Approved" : "Rejected")
                || decision.Decision != (request.Approve ? "Approved" : "Rejected")
                || decision.Reason != request.Reason!.Trim() || decision.DecidedAtUtc == default || decision.DecidedAtUtc.Offset != TimeSpan.Zero
                || (request.Approve ? decision.AdjustmentMovementId is null || decision.AdjustmentMovementId == Guid.Empty : decision.AdjustmentMovementId is not null)
                || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId)
                || !answer.IsReplay && answer.OriginalCorrelationId != correlation.CorrelationId
                || new WorkInventoryStockCountPreviewV1(count.LabelId, count.LabelVersion, count.OwnerKind, count.OwnerId,
                    count.CustodianKind, count.CustodianId, count.LocationId, count.ProductId, count.Block, count.BookKg,
                    count.CountedKg, count.DifferenceKg, count.CountedAtUtc, count.Reason, proof.Count.RecordedBy,
                    count.RecordedAtUtc, count.BookPieceCount, count.CountedPieceCount) != proof.Count)
                throw new WorkDecisionException(503);
            return new(snapshot.CompanyId, InventoryStockCount, id, count.Status, answer.IsReplay, answer.OriginalCorrelationId);
        }
        catch (InventoryStockCountException error) { throw new WorkDecisionException(error.Status, error.Code); }
    }
}
