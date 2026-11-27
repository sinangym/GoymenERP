using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Modules.Inventory.Infrastructure.WorkInbox;

/// <summary>The native count is immutable evidence. Its one append-only decision advances logical state 1 to 2.
/// Live label state is deliberately not read: the native command owns the first decision's label-version check,
/// while an old key must still reach its immutable receipt after a later movement/count.</summary>
internal sealed class InventoryStockCountWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public const string KindName = "inventory.stock-count.v1";
    public const string DifferenceRule = "inventory.stock-count-difference";
    private readonly InventoryWorkInboxRows rows;
    private readonly InventoryWorkInboxFence fence;
    public InventoryStockCountWorkInboxSourceV1(InventoryDbContext db, InventoryWorkInboxFence fence)
        : this(new InventoryWorkInboxDbRows(db), fence) { }
    internal InventoryStockCountWorkInboxSourceV1(InventoryWorkInboxRows rows, InventoryWorkInboxFence fence)
    { this.rows = rows; this.fence = fence; }
    public string Module => "inventory";
    public string Kind => KindName;
    public string ApproverPermission => InventoryPermissionManifestV1.StockCountApprove;

    public async Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        if (context.GrantScope is not null) return WorkInboxSourceResultV1.Denied();
        if (await fence.CheckAsync(context, ApproverPermission, token) is { } denied) return denied;
        List<StockCountRow> pending;
        try
        {
            pending = await rows.ListAsync(rows.StockCounts.Where(row => row.CompanyId == context.CompanyId
                    && row.CountedKg != row.BookKg && row.RecordedBy != context.UserId
                    && !rows.StockCountDecisions.Any(decision => decision.CompanyId == context.CompanyId && decision.CountId == row.Id))
                .OrderBy(row => row.RecordedAtUtc).ThenBy(row => row.Id).Take(maxCount + 1), token);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        { return await fence.UnavailableAsync(context, ApproverPermission, token); }
        if (await fence.CheckAsync(context, ApproverPermission, token) is { } changed) return changed;
        Dictionary<Guid, string> labels;
        try { labels = await LabelCodesAsync(context.CompanyId, pending.Take(maxCount).Select(row => row.LabelId).Distinct().ToArray(), token); }
        catch (Exception) when (!token.IsCancellationRequested)
        { return await fence.UnavailableAsync(context, ApproverPermission, token); }
        if (await fence.CheckAsync(context, ApproverPermission, token) is { } changedLabels) return changedLabels;
        var items = new List<WorkInboxItemV1>();
        foreach (var row in pending.Take(maxCount))
        {
            if (!TrySnapshot(context.CompanyId, row, null, labels.GetValueOrDefault(row.LabelId), out var snapshot)) return WorkInboxSourceResultV1.Unavailable();
            items.Add(snapshot.Item);
        }
        return WorkInboxSourceResultV1.Found(context.CompanyId, items, pending.Count > maxCount);
    }

    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        if (context.GrantScope is not null) return null;
        if (await fence.CheckAsync(context, ApproverPermission, token) is not null) return null;
        var counts = await rows.ListAsync(rows.StockCounts.Where(row => row.CompanyId == context.CompanyId && row.Id == itemId), token);
        if (await fence.CheckAsync(context, ApproverPermission, token) is not null) return null;
        var count = counts.SingleOrDefault();
        if (count is null || count.BookKg == count.CountedKg) return null;
        var decisions = await rows.ListAsync(rows.StockCountDecisions.Where(row => row.CompanyId == context.CompanyId && row.CountId == itemId), token);
        if (await fence.CheckAsync(context, ApproverPermission, token) is not null) return null;
        var labels = await LabelCodesAsync(context.CompanyId, [count.LabelId], token);
        if (await fence.CheckAsync(context, ApproverPermission, token) is not null) return null;
        return TrySnapshot(context.CompanyId, count, decisions.SingleOrDefault(), labels.GetValueOrDefault(count.LabelId), out var snapshot)
            ? snapshot : throw new InvalidOperationException("Invalid retained stock count evidence.");
    }

    private async Task<Dictionary<Guid, string>> LabelCodesAsync(Guid company, Guid[] ids, CancellationToken token)
    {
        if (ids.Length == 0) return [];
        // The existing native approval read discloses label code under StockCountApprove. No live physical
        // label/version is projected or compared; the old receipt remains bound to its immutable count.
        var labels = await rows.ListAsync(rows.Labels.Where(row => row.CompanyId == company && ids.Contains(row.Id))
            .Select(row => new { row.Id, row.Code }), token);
        return labels.ToDictionary(row => row.Id, row => row.Code);
    }

    private static bool TrySnapshot(Guid company, StockCountRow row, StockCountDecisionRow? decision, string? labelCode,
        out WorkApprovalDecisionSnapshotV1 snapshot)
    {
        snapshot = null!;
        if (row.CompanyId != company || row.Id == Guid.Empty || row.LabelId == Guid.Empty || row.LabelVersion < 1
            || row.OwnerId == Guid.Empty || row.CustodianId == Guid.Empty || row.LocationId == Guid.Empty || row.ProductId == Guid.Empty
            || row.OwnerKind is not (1 or 2) || row.CustodianKind is not (1 or 2) || row.Block is not (1 or 2)
            || row.BookKg < 0 || row.CountedKg < 0 || row.BookKg == row.CountedKg
            || string.IsNullOrWhiteSpace(row.RecordedBy) || row.RecordedBy.Length > 128
            || string.IsNullOrWhiteSpace(row.Reason) || row.Reason.Length > 1000
            || row.CountedAtUtc == default || row.CountedAtUtc.Offset != TimeSpan.Zero
            || row.RecordedAtUtc == default || row.RecordedAtUtc.Offset != TimeSpan.Zero
            || labelCode is not null && !InventoryPendingApprovalReadContractV1.IsCanonicalLabelCode(labelCode)
            || row.BookPieceCount is < 0 || row.CountedPieceCount is < 0
            || (row.BookPieceCount is null) != (row.CountedPieceCount is null)
            || decision is not null && (decision.CompanyId != company || decision.CountId != row.Id
                || decision.Decision is not (1 or 2) || string.IsNullOrWhiteSpace(decision.DecidedBy)
                || decision.DecidedBy == row.RecordedBy || string.IsNullOrWhiteSpace(decision.Reason)
                || decision.Reason.Length > 1000 || decision.DecidedAtUtc == default || decision.DecidedAtUtc.Offset != TimeSpan.Zero
                || (decision.Decision == 1 ? decision.MovementId is null || decision.MovementId == Guid.Empty : decision.MovementId is not null)))
            return false;
        var view = InventoryStore.StockCountView(row, decision);
        var count = new WorkInventoryStockCountPreviewV1(view.LabelId, view.LabelVersion, view.OwnerKind, view.OwnerId,
            view.CustodianKind, view.CustodianId, view.LocationId, view.ProductId, view.Block, view.BookKg, view.CountedKg,
            view.DifferenceKg, view.CountedAtUtc, view.Reason, row.RecordedBy, view.RecordedAtUtc, view.BookPieceCount, view.CountedPieceCount);
        var item = new WorkInboxItemV1("inventory", KindName, row.Id, labelCode ?? Short(row.LabelId), row.RecordedAtUtc,
            "/onaylarim/" + KindName + "/" + row.Id.ToString("D"), row.RecordedBy,
            [new(DifferenceRule, null, view.CountedKg, view.DifferenceKg, "kg")],
            [new("inventory-label", labelCode ?? Short(row.LabelId), row.LabelId, null),
             new("product", Short(row.ProductId), row.ProductId, null), new("location", Short(row.LocationId), row.LocationId, null)],
            null);
        snapshot = new(company, item, decision is null ? "1" : "2", decision is null,
            InventoryStockCountProof: new(company, row.Id, count, view.Status));
        return true;
    }

    private static string Short(Guid value) => "…" + value.ToString("D", CultureInfo.InvariantCulture)[^12..];
}
