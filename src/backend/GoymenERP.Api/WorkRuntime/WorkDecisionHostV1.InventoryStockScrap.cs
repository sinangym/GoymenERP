using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidInventoryStockScrapProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind != InventoryStockScrap) return snapshot.InventoryStockScrapProof is null;
        if (snapshot.InventoryStockScrapProof is not { Scrap: { } scrap } proof
            || proof.CompanyId != snapshot.CompanyId || proof.ScrapId != snapshot.Item.ItemId
            || scrap.LabelId == Guid.Empty || scrap.OwnerId == Guid.Empty || scrap.CustodianId == Guid.Empty
            || scrap.LocationId == Guid.Empty || scrap.ProductId == Guid.Empty
            || scrap.OwnerKind is not ("Company" or "Counterparty") || scrap.CustodianKind is not ("Company" or "Counterparty")
            || scrap.Block is not ("Free" or "Blocked") || !StockScrapLabelVersion(scrap.LabelVersion)
            || !StockScrapKg(scrap.BookKg, false) || !StockScrapKg(scrap.ScrapKg, false) || !StockScrapKg(scrap.RemainingKg, false)
            || scrap.BookKg == "0" || scrap.ScrapKg == "0" || scrap.RecordedBy != snapshot.Item.PreparedBy
            || string.IsNullOrWhiteSpace(scrap.RecordedBy) || scrap.RecordedBy.Length > 128
            || string.IsNullOrWhiteSpace(scrap.Reason) || scrap.Reason.Length > 1000
            || scrap.ScrappedAtUtc == default || scrap.ScrappedAtUtc.Offset != TimeSpan.Zero
            || scrap.RecordedAtUtc == default || scrap.RecordedAtUtc.Offset != TimeSpan.Zero
            || snapshot.Item.WaitingSinceUtc != scrap.RecordedAtUtc
            || scrap.BookPieceCount is < 0 || scrap.ScrapPieceCount is < 0
            || (scrap.BookPieceCount is null) != (scrap.ScrapPieceCount is null)
            || scrap.ScrapPieceCount > scrap.BookPieceCount
            || proof.Status is not ("PendingApproval" or "Approved" or "Rejected")
            || snapshot.Pending != (proof.Status == "PendingApproval") || snapshot.SourceVersion != (snapshot.Pending ? "1" : "2")
            || snapshot.Item.ExcludedDeciders is { Count: > 0 }
            || snapshot.Item.References.Count != 3
            || snapshot.Item.References.Count(row => row.Role == "inventory-label" && row.RecordId == scrap.LabelId && row.Link is null) != 1
            || snapshot.Item.References.Count(row => row.Role == "product" && row.RecordId == scrap.ProductId) != 1
            || snapshot.Item.References.Count(row => row.Role == "location" && row.RecordId == scrap.LocationId) != 1
            || snapshot.Item.Scope is not null)
            return false;
        return true;
    }

    private static bool StockScrapLabelVersion(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
        out var version) && version > 0 && version.ToString(CultureInfo.InvariantCulture) == value;
    private static bool StockScrapKg(string value, bool signed) => decimal.TryParse(value,
        signed ? NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint : NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture, out var kg) && (signed || kg >= 0) && kg.ToString("0.###", CultureInfo.InvariantCulture) == value;

    private async Task<WorkDecisionResultV1> DecideInventoryStockScrapAsync(Guid id, WorkDecisionRequestV1 request,
        string key, WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (stockScraps is null || snapshot.InventoryStockScrapProof is not { } proof) throw new WorkDecisionException(503);
        try
        {
            var answer = await stockScraps.DecideAsync(id,
                new(request.Approve ? InventoryStockScrapDecisionKindV1.Approve : InventoryStockScrapDecisionKindV1.Reject, request.Reason!),
                new(actor, string.Empty, [], correlation.CorrelationId, correlation.CausationId, key, "Web"), token);
            if (answer is not { CommandReceiptProven: true, Scrap: { Decision: { } decision } scrap }
                || scrap.CompanyId != snapshot.CompanyId || scrap.ScrapId != id
                || !snapshot.Pending && !answer.IsReplay
                || !snapshot.Pending && proof.Status != scrap.Status
                || scrap.Status != (request.Approve ? "Approved" : "Rejected")
                || decision.Decision != (request.Approve ? "Approved" : "Rejected")
                || decision.Reason != request.Reason!.Trim() || decision.DecidedAtUtc == default || decision.DecidedAtUtc.Offset != TimeSpan.Zero
                || (request.Approve ? decision.ScrapMovementId is null || decision.ScrapMovementId == Guid.Empty : decision.ScrapMovementId is not null)
                || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId)
                || !answer.IsReplay && answer.OriginalCorrelationId != correlation.CorrelationId
                || new WorkInventoryStockScrapPreviewV1(scrap.LabelId, scrap.LabelVersion, scrap.OwnerKind, scrap.OwnerId,
                    scrap.CustodianKind, scrap.CustodianId, scrap.LocationId, scrap.ProductId, scrap.Block, scrap.BookKg,
                    scrap.ScrapKg, scrap.RemainingKg, scrap.ScrappedAtUtc, scrap.Reason, proof.Scrap.RecordedBy,
                    scrap.RecordedAtUtc, scrap.BookPieceCount, scrap.ScrapPieceCount) != proof.Scrap)
                throw new WorkDecisionException(503);
            return new(snapshot.CompanyId, InventoryStockScrap, id, scrap.Status, answer.IsReplay, answer.OriginalCorrelationId);
        }
        catch (InventoryStockScrapException error) { throw new WorkDecisionException(error.Status, error.Code); }
    }
}
