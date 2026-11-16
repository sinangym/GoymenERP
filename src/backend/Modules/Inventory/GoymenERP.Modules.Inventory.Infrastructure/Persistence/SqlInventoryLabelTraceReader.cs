using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// STK-S33: the company-filtered reads behind the label trace. Every query names the resolved company explicitly on top of the
/// tenant query filter; the reads are no-tracking and create nothing.
/// </summary>
internal sealed class SqlInventoryLabelTraceReader(InventoryDbContext db) : IInventoryLabelTraceReader
{
    public async Task<IReadOnlyList<InventoryLabelTraceMovementLine>> ReadMovementLinesAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> labelIds,
        IReadOnlyCollection<string> sourceKinds,
        int take,
        CancellationToken cancellationToken)
    {
        if (labelIds.Count == 0 || sourceKinds.Count == 0 || take <= 0)
        {
            return [];
        }

        var labels = labelIds.Distinct().ToArray();
        var kinds = sourceKinds.Distinct(StringComparer.Ordinal).ToArray();
        return await (
                from line in db.MovementLines.AsNoTracking()
                join movement in db.Movements.AsNoTracking()
                    on new { line.CompanyId, line.MovementId } equals new { movement.CompanyId, MovementId = movement.Id }
                where line.CompanyId == companyId && movement.CompanyId == companyId
                    && labels.Contains(line.LabelId) && kinds.Contains(movement.SourceKind)
                orderby movement.OccurredAtUtc, movement.Id, line.LineNumber
                select new InventoryLabelTraceMovementLine(
                    movement.Id,
                    line.LineNumber,
                    line.LabelId,
                    movement.Kind,
                    movement.OccurredAtUtc,
                    movement.SourceKind,
                    movement.SourceId,
                    movement.SourceLineId,
                    line.Direction,
                    line.QuantityKg))
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryLabelTraceLabel>> SearchLabelsAsync(
        Guid companyId,
        string prefix,
        int take,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(prefix) || take <= 0)
        {
            return [];
        }

        return await db.Labels.AsNoTracking()
            .Where(label => label.CompanyId == companyId && label.Code.StartsWith(prefix))
            .OrderBy(label => label.Code)
            .ThenBy(label => label.Id)
            .Take(take)
            .Select(label => new InventoryLabelTraceLabel(label.Id, label.Code))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryLabelTraceLabel>> FindLabelsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> labelIds,
        CancellationToken cancellationToken)
    {
        if (labelIds.Count == 0)
        {
            return [];
        }

        var ids = labelIds.Distinct().ToArray();
        return await db.Labels.AsNoTracking()
            .Where(label => label.CompanyId == companyId && ids.Contains(label.Id))
            .OrderBy(label => label.Code)
            .Select(label => new InventoryLabelTraceLabel(label.Id, label.Code))
            .ToListAsync(cancellationToken);
    }
}
