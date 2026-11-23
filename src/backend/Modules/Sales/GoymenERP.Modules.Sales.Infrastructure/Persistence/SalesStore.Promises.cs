using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// WP14 (OTIF): the drafts with a promised day in a period — not cancelled, with the order's or a line's requested delivery date
/// in [from, to] — newest first, by the store's company-bound cursor. One untracked read through the company-filtered context
/// (and the company again, explicitly), rebuilt through the domain like every read; it writes nothing.
/// </summary>
public sealed partial class SalesStore
{
    public async Task<SalesDraftPage> ListPromisedDraftsAsync(DateOnly from, DateOnly to, int pageSize, string? cursor,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Sayfa boyutu 1–{MaximumPageSize} olmalıdır.");
        }

        var cancelled = (int)SalesDraftState.Cancelled;
        var query = dbContext.Drafts.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.State != cancelled
                && ((row.RequestedDeliveryDate != null && row.RequestedDeliveryDate >= from && row.RequestedDeliveryDate <= to)
                    || dbContext.DraftLines.Any(line => line.CompanyId == companyId && line.DraftId == row.Id
                        && line.RequestedDeliveryDate != null && line.RequestedDeliveryDate >= from && line.RequestedDeliveryDate <= to)));
        if (cursor is not null)
        {
            var (createdAtUtc, id) = ListCursor.Decode(cursor, companyId);
            query = query.Where(row => row.CreatedAtUtc < createdAtUtc || (row.CreatedAtUtc == createdAtUtc && row.Id.CompareTo(id) < 0));
        }

        var rows = await query
            .OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var page = hasMore ? rows.Take(pageSize).ToList() : rows;
        var drafts = await RestoreAsync(page, cancellationToken);
        var last = page.LastOrDefault();
        return new SalesDraftPage(drafts, hasMore && last is not null ? ListCursor.Encode(companyId, last.CreatedAtUtc, last.Id) : null);
    }
}
