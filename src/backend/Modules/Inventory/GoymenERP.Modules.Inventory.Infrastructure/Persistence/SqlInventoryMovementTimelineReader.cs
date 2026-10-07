using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>Tenant-filtered, newest-first projection of immutable physical movements.</summary>
internal sealed class SqlInventoryMovementTimelineReader(InventoryDbContext db) : IInventoryMovementTimelineReader
{
    public async Task<InventoryMovementTimelineWindow> SearchAsync(
        Guid companyId,
        InventoryMovementTimelineReadQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Movements.AsNoTracking().Where(movement => movement.CompanyId == companyId);
        if (request.After is { } after)
        {
            query = query.Where(movement => movement.OccurredAtUtc < after.OccurredAtUtc
                || movement.OccurredAtUtc == after.OccurredAtUtc
                    && movement.Id.CompareTo(after.MovementId) < 0);
        }

        var rows = await query.OrderByDescending(movement => movement.OccurredAtUtc)
            .ThenByDescending(movement => movement.Id)
            .Take(request.Limit + 1)
            .ToListAsync(cancellationToken);
        var page = rows.Take(request.Limit).ToArray();
        if (page.Length == 0)
        {
            return new InventoryMovementTimelineWindow([], false, null);
        }

        var movementIds = page.Select(movement => movement.Id).ToArray();
        var lines = await db.MovementLines.AsNoTracking()
            .Where(line => line.CompanyId == companyId && movementIds.Contains(line.MovementId))
            .OrderBy(line => line.MovementId)
            .ThenBy(line => line.LineNumber)
            .ToListAsync(cancellationToken);
        var labelIds = lines.Select(line => line.LabelId).Distinct().ToArray();
        var labels = await db.Labels.AsNoTracking()
            .Where(label => label.CompanyId == companyId && labelIds.Contains(label.Id))
            .Select(label => new { label.Id, label.Code, label.ProductId })
            .ToDictionaryAsync(label => label.Id, cancellationToken);

        var linesByMovement = lines.ToLookup(line => line.MovementId);
        var items = page.Select(movement => new InventoryMovementV1(
            movement.Id,
            EnumName<StockMovementKind>(movement.Kind),
            movement.OccurredAtUtc,
            movement.SourceKind,
            movement.SourceId,
            movement.SourceLineId,
            EnumName<StockSourceGrain>(movement.SourceGrain),
            linesByMovement[movement.Id].Select(line =>
            {
                if (!labels.TryGetValue(line.LabelId, out var label) || label.ProductId != line.ProductId)
                {
                    throw Unavailable();
                }

                return new InventoryMovementLineV1(
                    line.LineNumber,
                    EnumName<StockDirection>(line.Direction),
                    line.LabelId,
                    label.Code,
                    EnumName<StockPartyKind>(line.OwnerKind),
                    line.OwnerId,
                    EnumName<StockPartyKind>(line.CustodianKind),
                    line.CustodianId,
                    line.LocationId,
                    line.ProductId,
                    EnumName<BlockState>(line.Block),
                    Kilograms.Positive(line.QuantityKg).ToString());
            }).ToArray())).ToArray();

        var last = page[^1];
        return new InventoryMovementTimelineWindow(
            items,
            rows.Count > request.Limit,
            rows.Count > request.Limit
                ? new InventoryMovementTimelinePosition(last.OccurredAtUtc, last.Id)
                : null);
    }

    private static string EnumName<T>(int value) where T : struct, Enum =>
        Enum.IsDefined(typeof(T), value)
            ? Enum.GetName(typeof(T), value)!
            : throw Unavailable();

    private static InventoryMovementTimelineException Unavailable() =>
        new(503, InventoryMovementTimelineCodesV1.Unavailable);
}
