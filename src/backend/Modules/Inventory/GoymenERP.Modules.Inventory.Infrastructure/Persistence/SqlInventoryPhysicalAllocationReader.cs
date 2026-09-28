using System.Globalization;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// Captures one deterministic company/order/line allocation window. Allocation and label versions
/// are re-read before the result is released so a mixed-time response fails closed.
/// </summary>
internal sealed class SqlInventoryPhysicalAllocationReader(InventoryDbContext db)
    : IInventoryPhysicalAllocationReader
{
    public async Task<InventoryPhysicalAllocationReadWindow> SearchAsync(
        Guid companyId,
        Guid salesOrderId,
        Guid salesOrderLineId,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await Query(companyId, salesOrderId, salesOrderLineId)
            .OrderBy(row => row.CreatedAtUtc)
            .ThenBy(row => row.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var captured = rows.Take(limit).ToArray();
        if (captured.Length == 0)
        {
            return new InventoryPhysicalAllocationReadWindow([], false);
        }

        var labelIds = captured.Select(row => row.LabelId).Distinct().ToArray();
        var labels = await db.Labels.AsNoTracking()
            .Where(label => label.CompanyId == companyId && labelIds.Contains(label.Id))
            .Select(label => new LabelCapture(label.Id, label.ProductId, label.Version))
            .ToDictionaryAsync(label => label.Id, cancellationToken);
        if (labels.Count != labelIds.Length)
        {
            throw Error(503, InventoryPhysicalAllocationReadCodesV1.Unavailable);
        }

        var verification = await Query(companyId, salesOrderId, salesOrderLineId)
            .OrderBy(row => row.CreatedAtUtc)
            .ThenBy(row => row.Id)
            .Take(limit + 1)
            .Select(row => new AllocationCapture(row.Id, row.Version))
            .ToListAsync(cancellationToken);
        var labelVerification = await db.Labels.AsNoTracking()
            .Where(label => label.CompanyId == companyId && labelIds.Contains(label.Id))
            .Select(label => new LabelCapture(label.Id, label.ProductId, label.Version))
            .ToDictionaryAsync(label => label.Id, cancellationToken);

        if (!SameWindow(rows, verification)
            || labelVerification.Count != labelIds.Length
            || labels.Any(pair => !labelVerification.TryGetValue(pair.Key, out var current) || current != pair.Value))
        {
            throw Error(409, InventoryPhysicalAllocationReadCodesV1.ReadConflict);
        }

        return new InventoryPhysicalAllocationReadWindow(
            [.. captured.Select(row => Capture(row, labels))],
            rows.Count > limit);
    }

    /// <summary>
    /// INV-AL-1 change: one label's newest sales allocations with their change history. The label version and every
    /// captured allocation version are re-read after the capture; a change in between is a read conflict (409).
    /// </summary>
    public async Task<InventoryLabelPhysicalAllocationsV1?> ReadLabelAsync(
        Guid companyId,
        Guid labelId,
        int limit,
        CancellationToken cancellationToken)
    {
        var label = await LabelVersionAsync(companyId, labelId, cancellationToken);
        if (label is null)
        {
            return null;
        }

        var rows = await LabelQuery(companyId, labelId)
            .OrderByDescending(row => row.CreatedAtUtc)
            .ThenByDescending(row => row.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var captured = rows.Take(limit).ToArray();
        var ids = captured.Select(row => row.Id).ToArray();
        List<AllocationChangeRow> changes = ids.Length == 0
            ? []
            : await db.AllocationChanges.AsNoTracking()
                .Where(change => change.CompanyId == companyId
                    && (ids.Contains(change.SourceAllocationId) || ids.Contains(change.TargetAllocationId)))
                .OrderBy(change => change.ChangedAtUtc)
                .ThenBy(change => change.SourceVersion)
                .ThenBy(change => change.Id)
                .ToListAsync(cancellationToken);

        var verification = await LabelQuery(companyId, labelId)
            .OrderByDescending(row => row.CreatedAtUtc)
            .ThenByDescending(row => row.Id)
            .Take(limit + 1)
            .Select(row => new AllocationCapture(row.Id, row.Version))
            .ToListAsync(cancellationToken);
        var labelAfter = await LabelVersionAsync(companyId, labelId, cancellationToken);
        if (!SameWindow(rows, verification) || labelAfter != label)
        {
            throw Error(409, InventoryPhysicalAllocationReadCodesV1.ReadConflict);
        }

        var labels = new Dictionary<Guid, LabelCapture> { [label.Id] = label };
        return new InventoryLabelPhysicalAllocationsV1(
            labelId,
            label.Version.ToString(CultureInfo.InvariantCulture),
            [.. captured.Select(row => new InventoryLabelPhysicalAllocationV1(
                Capture(row, labels).Allocation,
                row.ReleasedAtUtc,
                row.ReleaseReason,
                [.. changes
                    .Where(change => change.SourceAllocationId == row.Id || change.TargetAllocationId == row.Id)
                    .Select(ChangeEntry)]))],
            rows.Count > limit);
    }

    private static InventoryPhysicalAllocationChangeEntryV1 ChangeEntry(AllocationChangeRow change)
    {
        var kind = change.Kind switch
        {
            1 => InventoryAllocationChangeKindsV1.Resize,
            2 => InventoryAllocationChangeKindsV1.Move,
            _ => throw Error(503, InventoryPhysicalAllocationReadCodesV1.Unavailable),
        };
        if (change.SourceVersion <= 0 || change.SourceRemainingKg <= 0m || change.TargetRemainingKg <= 0m
            || string.IsNullOrWhiteSpace(change.Reason) || change.ChangedAtUtc.Offset != TimeSpan.Zero)
        {
            throw Error(503, InventoryPhysicalAllocationReadCodesV1.Unavailable);
        }

        return new InventoryPhysicalAllocationChangeEntryV1(
            change.Id,
            kind,
            change.SourceAllocationId,
            change.SourceVersion.ToString(CultureInfo.InvariantCulture),
            change.SourceLabelId,
            Kilograms.Of(change.SourceRemainingKg).ToString(),
            change.TargetAllocationId,
            change.TargetLabelId,
            Kilograms.Of(change.TargetRemainingKg).ToString(),
            change.Reason,
            change.ChangedAtUtc);
    }

    private async Task<LabelCapture?> LabelVersionAsync(Guid companyId, Guid labelId, CancellationToken cancellationToken) =>
        await db.Labels.AsNoTracking()
            .Where(label => label.CompanyId == companyId && label.Id == labelId)
            .Select(label => new LabelCapture(label.Id, label.ProductId, label.Version))
            .SingleOrDefaultAsync(cancellationToken);

    private IQueryable<AllocationRow> LabelQuery(Guid companyId, Guid labelId) =>
        db.Allocations.AsNoTracking().Where(row =>
            row.CompanyId == companyId
            && row.LabelId == labelId
            && row.DemandKind == InventoryAllocationDemandKindsV1.SalesOrderLine);

    private IQueryable<AllocationRow> Query(Guid companyId, Guid salesOrderId, Guid salesOrderLineId) =>
        db.Allocations.AsNoTracking().Where(row =>
            row.CompanyId == companyId
            && row.DemandKind == InventoryAllocationDemandKindsV1.SalesOrderLine
            && row.DemandId == salesOrderId
            && row.DemandLineId == salesOrderLineId);

    private static bool SameWindow(
        IReadOnlyList<AllocationRow> rows,
        IReadOnlyList<AllocationCapture> verification) =>
        rows.Count == verification.Count
        && rows.Select(row => new AllocationCapture(row.Id, row.Version)).SequenceEqual(verification);

    private static InventoryPhysicalAllocationReadRecord Capture(
        AllocationRow row,
        IReadOnlyDictionary<Guid, LabelCapture> labels)
    {
        if (row.Id == Guid.Empty
            || row.CompanyId == Guid.Empty
            || row.LabelId == Guid.Empty
            || row.DemandId is not { } demandId || demandId == Guid.Empty
            || row.DemandLineId is not { } demandLineId || demandLineId == Guid.Empty
            || row.DemandProductId is not { } productId || productId == Guid.Empty
            || !string.Equals(row.DemandKind, InventoryAllocationDemandKindsV1.SalesOrderLine, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(row.DemandProductVersion)
            || string.IsNullOrWhiteSpace(row.Reason)
            || row.Version <= 0
            || row.AllocatedKg <= 0m
            || row.RemainingKg < 0m
            || row.RemainingKg > row.AllocatedKg
            || !Enum.IsDefined((StockAllocationStatus)row.Status)
            || !labels.TryGetValue(row.LabelId, out var label)
            || label.Version <= 0
            || label.ProductId != productId)
        {
            throw Error(503, InventoryPhysicalAllocationReadCodesV1.Unavailable);
        }

        var allocation = new InventoryPhysicalAllocationViewV1(
            row.Id,
            row.CompanyId,
            row.Version.ToString(CultureInfo.InvariantCulture),
            ((StockAllocationStatus)row.Status).ToString(),
            row.LabelId,
            productId,
                row.DemandKind!,
            demandId,
            demandLineId,
                row.DemandProductVersion!,
            Kilograms.Of(row.AllocatedKg).ToString(),
            Kilograms.Of(row.RemainingKg).ToString(),
                row.Reason!,
            row.ReservedUntilUtc,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);
        return new InventoryPhysicalAllocationReadRecord(allocation, label.Version);
    }

    private static InventoryPhysicalAllocationReadException Error(int status, string code) =>
        new InventoryPhysicalAllocationReadException(status, code);

    private sealed record AllocationCapture(Guid Id, long Version);
    private sealed record LabelCapture(Guid Id, Guid ProductId, long Version);
}
