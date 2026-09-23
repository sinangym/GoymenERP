using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// Read-only, filtered and explicitly bound to the captured company. Every mutation of documents or
/// balances also increments LabelRow.Version atomically. A second version read rejects a torn capture;
/// this is a point-in-time reference observation, not a lock/reservation or future availability promise.
/// </summary>
internal sealed class SqlInventoryParentRollReader(InventoryDbContext db) : IInventoryParentRollReader
{
    public async Task<InventoryParentRollWindow> SearchAsync(Guid companyId, string q, int limit, CancellationToken ct)
    {
        var query = db.Labels.AsNoTracking().Where(x => x.CompanyId == companyId);
        if (q.Length > 0) query = query.Where(x => x.Code.Contains(q));
        var rows = await query.OrderBy(x => x.Code).ThenBy(x => x.Id).Take(limit + 1).ToListAsync(ct);
        return new InventoryParentRollWindow(
            await CaptureAsync(companyId, rows.Take(limit).ToArray(), ct),
            rows.Count > limit);
    }

    public async Task<InventoryParentRollRecord?> ReadAsync(Guid companyId, Guid labelId, CancellationToken ct)
    {
        var row = await db.Labels.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == labelId, ct);
        return row is null ? null : (await CaptureAsync(companyId, [row], ct)).Single();
    }

    private async Task<IReadOnlyList<InventoryParentRollRecord>> CaptureAsync(Guid companyId,
        IReadOnlyList<LabelRow> labels, CancellationToken ct)
    {
        if (labels.Count == 0) return [];
        var ids = labels.Select(x => x.Id).ToArray();
        var lotIds = labels.Select(x => x.LotId).Distinct().ToArray();
        var lots = await db.Lots.AsNoTracking().Where(x => x.CompanyId == companyId && lotIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var documents = await db.LabelDocuments.AsNoTracking().Where(x => x.CompanyId == companyId && ids.Contains(x.LabelId))
            .OrderBy(x => x.Sequence).ToListAsync(ct);
        var balances = await db.Balances.AsNoTracking().Where(x => x.CompanyId == companyId && ids.Contains(x.LabelId)
            && x.QuantityKg != 0m).ToListAsync(ct);
        var versions = await db.Labels.AsNoTracking().Where(x => x.CompanyId == companyId && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Version }).ToDictionaryAsync(x => x.Id, x => x.Version, ct);
        if (labels.Any(x => !versions.TryGetValue(x.Id, out var current) || current != x.Version))
            throw new InventoryParentRollException(409, InventoryParentRollCodesV1.ReadConflict);

        return labels.Select(row =>
        {
            if (!lots.TryGetValue(row.LotId, out var lot))
                throw new InventoryParentRollException(503, InventoryParentRollCodesV1.Unavailable);
            var label = new MaterialLabel(row.Id, row.CompanyId, row.Code, row.ProductId, row.LotId,
                JsonSerializer.Deserialize<string[]>(row.RequiredDocumentsJson)
                    ?? throw new InventoryParentRollException(503, InventoryParentRollCodesV1.Unavailable),
                row.HeatNumber, row.CoilNumber);
            foreach (var document in documents.Where(x => x.LabelId == row.Id))
                label.AttachDocument(document.Kind, document.AttachedAtUtc, document.AttachedBy);
            var positions = balances.Where(x => x.LabelId == row.Id).Select(x =>
            {
                if (x.ProductId != row.ProductId)
                    throw new InventoryParentRollException(503, InventoryParentRollCodesV1.Unavailable);
                return new StockBalance(new StockPosition(companyId,
                    new StockOwner((StockPartyKind)x.OwnerKind, x.OwnerId),
                    new StockCustodian((StockPartyKind)x.CustodianKind, x.CustodianId),
                    x.LocationId, label, (BlockState)x.Block),
                    Kilograms.Of(x.QuantityKg));
            }).ToArray();
            return new InventoryParentRollRecord(row.Version, label,
                new MaterialLot(lot.Id, lot.CompanyId, lot.ProductId, lot.HeatNumber, lot.CoilNumber), positions);
        }).ToArray();
    }
}
