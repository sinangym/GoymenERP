using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// A tenant-filtered physical-position capture. The label version is read again after balances and
/// allocations so a response is never assembled across a concurrent write. This is an observation,
/// not a lock or availability promise.
/// </summary>
internal sealed class SqlInventoryStockPositionReader(InventoryDbContext db) : IInventoryStockPositionReader
{
    public async Task<InventoryStockPositionWindow> SearchAsync(
        Guid companyId,
        string q,
        int limit,
        CancellationToken cancellationToken)
    {
        var stockedLabelIds = db.Balances.AsNoTracking()
            .Where(balance => balance.CompanyId == companyId && balance.QuantityKg != 0m)
            .Select(balance => balance.LabelId);
        var query = db.Labels.AsNoTracking()
            .Where(label => label.CompanyId == companyId && stockedLabelIds.Contains(label.Id));
        if (q.Length > 0)
        {
            query = query.Where(label => label.Code.Contains(q));
        }

        var labels = await query.OrderBy(label => label.Code).ThenBy(label => label.Id)
            .Take(limit + 1).ToListAsync(cancellationToken);
        return new InventoryStockPositionWindow(
            await CaptureAsync(companyId, labels.Take(limit).ToArray(), cancellationToken),
            labels.Count > limit);
    }

    private async Task<IReadOnlyList<InventoryStockPositionRecord>> CaptureAsync(
        Guid companyId,
        IReadOnlyList<LabelRow> labels,
        CancellationToken cancellationToken)
    {
        if (labels.Count == 0)
        {
            return [];
        }

        var labelIds = labels.Select(label => label.Id).ToArray();
        var lotIds = labels.Select(label => label.LotId).Distinct().ToArray();
        var lots = await db.Lots.AsNoTracking()
            .Where(lot => lot.CompanyId == companyId && lotIds.Contains(lot.Id))
            .ToDictionaryAsync(lot => lot.Id, cancellationToken);
        var documents = await db.LabelDocuments.AsNoTracking()
            .Where(document => document.CompanyId == companyId && labelIds.Contains(document.LabelId))
            .OrderBy(document => document.Sequence)
            .ToListAsync(cancellationToken);
        var balances = await db.Balances.AsNoTracking()
            .Where(balance => balance.CompanyId == companyId && labelIds.Contains(balance.LabelId)
                && balance.QuantityKg != 0m)
            .ToListAsync(cancellationToken);
        var allocations = await db.Allocations.AsNoTracking()
            .Where(allocation => allocation.CompanyId == companyId && labelIds.Contains(allocation.LabelId)
                && allocation.Status == (int)StockAllocationStatus.Active)
            .ToListAsync(cancellationToken);
        var versions = await db.Labels.AsNoTracking()
            .Where(label => label.CompanyId == companyId && labelIds.Contains(label.Id))
            .Select(label => new { label.Id, label.Version })
            .ToDictionaryAsync(label => label.Id, label => label.Version, cancellationToken);
        if (labels.Any(label => !versions.TryGetValue(label.Id, out var version) || version != label.Version))
        {
            throw new InventoryStockPositionException(409, InventoryStockPositionCodesV1.ReadConflict);
        }

        return labels.Select(row => Capture(row, lots, documents, balances, allocations)).ToArray();
    }

    private static InventoryStockPositionRecord Capture(
        LabelRow row,
        IReadOnlyDictionary<Guid, LotRow> lots,
        IReadOnlyList<LabelDocumentRow> documents,
        IReadOnlyList<BalanceRow> balances,
        IReadOnlyList<AllocationRow> allocations)
    {
        if (!lots.TryGetValue(row.LotId, out var lot))
        {
            throw new InventoryStockPositionException(503, InventoryStockPositionCodesV1.Unavailable);
        }

        var label = new MaterialLabel(
            row.Id,
            row.CompanyId,
            row.Code,
            row.ProductId,
            row.LotId,
            JsonSerializer.Deserialize<string[]>(row.RequiredDocumentsJson)
                ?? throw new InventoryStockPositionException(503, InventoryStockPositionCodesV1.Unavailable),
            row.HeatNumber,
            row.CoilNumber);
        foreach (var document in documents.Where(document => document.LabelId == row.Id))
        {
            label.AttachDocument(document.Kind, document.AttachedAtUtc, document.AttachedBy);
        }

        var storedBalances = balances.Where(balance => balance.LabelId == row.Id).Select(balance =>
        {
            if (balance.ProductId != row.ProductId)
            {
                throw new InventoryStockPositionException(503, InventoryStockPositionCodesV1.Unavailable);
            }

            var position = new StockPosition(
                row.CompanyId,
                new StockOwner((StockPartyKind)balance.OwnerKind, balance.OwnerId),
                new StockCustodian((StockPartyKind)balance.CustodianKind, balance.CustodianId),
                balance.LocationId,
                label,
                (BlockState)balance.Block);
            return new StockBalance(position, Kilograms.Of(balance.QuantityKg));
        }).ToArray();

        var activeAllocations = allocations.Where(allocation => allocation.LabelId == row.Id)
            .Select(allocation => new InventoryActiveAllocationRecord(
                ((StockPartyKind)allocation.OwnerKind).ToString(),
                allocation.OwnerId,
                ((StockPartyKind)allocation.CustodianKind).ToString(),
                allocation.CustodianId,
                allocation.LocationId,
                ((BlockState)allocation.Block).ToString(),
                allocation.RemainingKg))
            .ToArray();

        return new InventoryStockPositionRecord(
            row.Version,
            label,
            new MaterialLot(lot.Id, lot.CompanyId, lot.ProductId, lot.HeatNumber, lot.CoilNumber),
            storedBalances,
            activeAllocations);
    }
}
