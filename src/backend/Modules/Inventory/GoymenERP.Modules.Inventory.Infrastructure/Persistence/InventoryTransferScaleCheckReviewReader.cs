using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// LOJ-D20: Inventory's own read of the dispatches whose source scale setting was stored as unreadable ("kontrol
/// edilemedi"). It reads only the context company's dispatch and transfer rows (explicit filter on top of the query
/// filter), untracked, newest dispatch first, one row past the limit to answer HasMore, and writes nothing. A dispatch whose
/// transfer later moved on (accepted, dispatch-cancelled…) stays listed with the transfer's current status: the unchecked
/// weighing is a fact of the dispatch, not of the transfer's state.
/// </summary>
internal sealed class InventoryTransferScaleCheckReviewReader(InventoryDbContext dbContext, ICompanyContext companyContext)
    : IInventoryTransferScaleCheckReviewReaderV1
{
    public async Task<InventoryTransferScaleCheckReviewPageV1> ListNotCheckedAsync(int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, InventoryTransferScaleCheckReviewApiV1.MaximumLimit);
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            // Without a server-resolved company there is no tenant filter to read under: refuse, never read unscoped.
            throw new InvalidOperationException("Kantar kontrolü yapılamayan sevkler yalnız çözümlenmiş şirket bağlamında okunur.");
        }

        var companyId = companyContext.CompanyId;
        var unreadable = (int)InventoryTransferSourceScaleSetting.Unreadable;
        var rows = await (
                from dispatch in dbContext.TransferDispatches.AsNoTracking()
                join transfer in dbContext.Transfers.AsNoTracking()
                    on new { dispatch.CompanyId, Id = dispatch.TransferId } equals new { transfer.CompanyId, transfer.Id }
                where dispatch.CompanyId == companyId && dispatch.SourceScaleSettingAtDispatch == unreadable
                orderby dispatch.DispatchedAtUtc descending, dispatch.TransferId
                select new
                {
                    transfer.Id,
                    transfer.Version,
                    transfer.Status,
                    transfer.SourceLocationId,
                    transfer.DestinationLocationId,
                    dispatch.DispatchedAtUtc,
                })
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var items = rows.Take(limit).Select(row => new InventoryTransferScaleCheckReviewItemV1(row.Id,
                row.Version.ToString(CultureInfo.InvariantCulture), ((InventoryTransferStatus)row.Status).ToString(),
                row.SourceLocationId, row.DestinationLocationId, row.DispatchedAtUtc,
                InventoryTransferScaleCheckStatusV1.NotChecked))
            .ToArray();
        return new InventoryTransferScaleCheckReviewPageV1(companyId, items, rows.Count > limit);
    }
}
