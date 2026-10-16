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
        InventoryStockPositionReadQuery request,
        CancellationToken cancellationToken)
    {
        var stockedLabelIds = db.Balances.AsNoTracking()
            .Where(balance => balance.CompanyId == companyId && balance.QuantityKg != 0m)
            .Select(balance => balance.LabelId);
        var query = db.Labels.AsNoTracking()
            .Where(label => label.CompanyId == companyId && stockedLabelIds.Contains(label.Id));
        if (request.Q.Length > 0)
        {
            query = query.Where(label => label.Code.Contains(request.Q));
        }

        if (request.ProductId.HasValue)
        {
            query = query.Where(label => label.ProductId == request.ProductId.Value);
        }
        if (request.Classification.HasValue)
        {
            query = query.Where(label => label.Classification == (int)request.Classification.Value);
        }
        if (request.LocationId.HasValue)
        {
            query = query.Where(label => db.Balances.Any(balance => balance.CompanyId == companyId
                && balance.LabelId == label.Id && balance.QuantityKg != 0m
                && balance.LocationId == request.LocationId.Value));
        }
        if (request.OwnerKind.HasValue && request.OwnerId.HasValue)
        {
            query = query.Where(label => db.Balances.Any(balance => balance.CompanyId == companyId
                && balance.LabelId == label.Id && balance.QuantityKg != 0m
                && balance.OwnerKind == (int)request.OwnerKind.Value
                && balance.OwnerId == request.OwnerId.Value));
        }
        if (request.After is { } after)
        {
            query = query.Where(label => string.Compare(label.Code, after.LabelCode) > 0
                || label.Code == after.LabelCode && label.Id.CompareTo(after.LabelId) > 0);
        }

        var labels = await query.OrderBy(label => label.Code).ThenBy(label => label.Id)
            .Take(request.Limit + 1).ToListAsync(cancellationToken);
        var page = labels.Take(request.Limit).ToArray();
        return new InventoryStockPositionWindow(
            await CaptureAsync(companyId, page, cancellationToken),
            labels.Count > request.Limit,
            page.Length == 0 ? null : new InventoryStockPositionPosition(page[^1].Code, page[^1].Id));
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
        var physicalHolds = await db.PhysicalHolds.AsNoTracking()
            .Where(hold => hold.CompanyId == companyId && labelIds.Contains(hold.LabelId)
                && hold.Status == InventoryStore.PhysicalHoldActive)
            .ToListAsync(cancellationToken);
        var versions = await db.Labels.AsNoTracking()
            .Where(label => label.CompanyId == companyId && labelIds.Contains(label.Id))
            .Select(label => new { label.Id, label.Version })
            .ToDictionaryAsync(label => label.Id, label => label.Version, cancellationToken);
        if (labels.Any(label => !versions.TryGetValue(label.Id, out var version) || version != label.Version))
        {
            throw new InventoryStockPositionException(409, InventoryStockPositionCodesV1.ReadConflict);
        }

        return labels.Select(row => Capture(row, lots, documents, balances, allocations, physicalHolds)).ToArray();
    }

    private static InventoryStockPositionRecord Capture(
        LabelRow row,
        IReadOnlyDictionary<Guid, LotRow> lots,
        IReadOnlyList<LabelDocumentRow> documents,
        IReadOnlyList<BalanceRow> balances,
        IReadOnlyList<AllocationRow> allocations,
        IReadOnlyList<InventoryPhysicalHoldRow> physicalHolds)
    {
        if (!lots.TryGetValue(row.LotId, out var lot))
        {
            throw new InventoryStockPositionException(503, InventoryStockPositionCodesV1.Unavailable);
        }

        var label = MaterialLabel.Restore(
            row.Id,
            row.CompanyId,
            row.Code,
            row.ProductId,
            row.LotId,
            row.ProductVersion,
            JsonSerializer.Deserialize<string[]>(row.RequiredDocumentsJson)
                ?? throw new InventoryStockPositionException(503, InventoryStockPositionCodesV1.Unavailable),
            row.HeatNumber,
            row.CoilNumber,
            [],
            documents.Where(document => document.LabelId == row.Id)
                .Select(document => new LabelDocumentAttachment(document.Kind, document.AttachedAtUtc, document.AttachedBy)),
            row.Classification is null ? null : (LabelClassification)row.Classification.Value);

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

        var activePhysicalHolds = physicalHolds.Where(hold => hold.LabelId == row.Id)
            .Select(hold => new InventoryActivePhysicalHoldRecord(
                hold.OwnerKind,
                hold.SourceKind,
                hold.SourceId,
                ((StockPartyKind)hold.PositionOwnerKind).ToString(),
                hold.PositionOwnerId,
                ((StockPartyKind)hold.CustodianKind).ToString(),
                hold.CustodianId,
                hold.LocationId,
                hold.ProductId,
                hold.QuantityKg))
            .ToArray();

        return new InventoryStockPositionRecord(
            row.Version,
            label,
            new MaterialLot(lot.Id, lot.CompanyId, lot.ProductId, lot.HeatNumber, lot.CoilNumber),
            storedBalances,
            activeAllocations,
            activePhysicalHolds);
    }
}
