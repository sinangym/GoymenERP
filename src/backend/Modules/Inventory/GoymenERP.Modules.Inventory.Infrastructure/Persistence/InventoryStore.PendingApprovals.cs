using GoymenERP.Modules.Inventory.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

internal sealed partial class InventoryStore : IInventoryPendingApprovalReaderV1
{
    public async Task<IReadOnlyList<InventoryStockCountPendingApprovalItemV1>> ReadStockCountsAsync(
        string excludingActorId,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        RequirePendingRead(excludingActorId, offset, limit);
        var rows = await dbContext.StockCounts.AsNoTracking()
            .Where(row => row.CountedKg != row.BookKg
                && row.RecordedBy != excludingActorId
                && !dbContext.StockCountDecisions.Any(decision => decision.CountId == row.Id))
            .OrderByDescending(row => row.RecordedAtUtc)
            .ThenBy(row => row.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(StockCountPending)];
    }

    public async Task<InventoryStockCountPendingApprovalItemV1?> ReadStockCountAsync(
        Guid countId,
        string excludingActorId,
        CancellationToken cancellationToken)
    {
        RequirePendingIdentity(countId, excludingActorId);
        var row = await dbContext.StockCounts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == countId
                && candidate.CountedKg != candidate.BookKg
                && candidate.RecordedBy != excludingActorId
                && !dbContext.StockCountDecisions.Any(decision => decision.CountId == candidate.Id),
                cancellationToken);
        return row is null ? null : StockCountPending(row);
    }

    public async Task<IReadOnlyList<InventoryStockScrapPendingApprovalItemV1>> ReadStockScrapsAsync(
        string excludingActorId,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        RequirePendingRead(excludingActorId, offset, limit);
        var rows = await dbContext.StockScraps.AsNoTracking()
            .Where(row => row.RecordedBy != excludingActorId
                && !dbContext.StockScrapDecisions.Any(decision => decision.ScrapId == row.Id))
            .OrderByDescending(row => row.RecordedAtUtc)
            .ThenBy(row => row.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(StockScrapPending)];
    }

    public async Task<InventoryStockScrapPendingApprovalItemV1?> ReadStockScrapAsync(
        Guid scrapId,
        string excludingActorId,
        CancellationToken cancellationToken)
    {
        RequirePendingIdentity(scrapId, excludingActorId);
        var row = await dbContext.StockScraps.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == scrapId
                && candidate.RecordedBy != excludingActorId
                && !dbContext.StockScrapDecisions.Any(decision => decision.ScrapId == candidate.Id),
                cancellationToken);
        return row is null ? null : StockScrapPending(row);
    }

    private static InventoryStockCountPendingApprovalItemV1 StockCountPending(StockCountRow row)
    {
        var view = StockCountView(row, decision: null);
        return new(
            view.CountId,
            view.CompanyId,
            view.LabelId,
            view.LabelVersion,
            view.OwnerKind,
            view.OwnerId,
            view.CustodianKind,
            view.CustodianId,
            view.LocationId,
            view.ProductId,
            view.Block,
            view.BookKg,
            view.CountedKg,
            view.DifferenceKg,
            view.Reason,
            row.RecordedBy,
            view.RecordedAtUtc);
    }

    private static InventoryStockScrapPendingApprovalItemV1 StockScrapPending(StockScrapRow row)
    {
        var view = StockScrapView(row, decision: null);
        return new(
            view.ScrapId,
            view.CompanyId,
            view.LabelId,
            view.LabelVersion,
            view.OwnerKind,
            view.OwnerId,
            view.CustodianKind,
            view.CustodianId,
            view.LocationId,
            view.ProductId,
            view.Block,
            view.BookKg,
            view.ScrapKg,
            view.RemainingKg,
            view.Reason,
            row.RecordedBy,
            view.RecordedAtUtc);
    }

    private void RequirePendingRead(string excludingActorId, int offset, int limit)
    {
        RequireResolvedCompany();
        if (string.IsNullOrWhiteSpace(excludingActorId)
            || offset is < 0 or > InventoryPendingApprovalReadServiceV1.MaximumOffset
            || limit is < 1 or > InventoryPendingApprovalReadServiceV1.ReadLimit)
        {
            throw new InvalidOperationException("Inventory pending-approval read arguments are invalid.");
        }
    }

    private void RequirePendingIdentity(Guid id, string excludingActorId)
    {
        RequireResolvedCompany();
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(excludingActorId))
        {
            throw new InvalidOperationException("Inventory pending-approval read identity is invalid.");
        }
    }
}
