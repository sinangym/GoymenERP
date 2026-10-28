using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// SAT-D103 (EK13): the shippable-drafts read of the Sales store — the drafts whose lines physical shipping may act on, for
/// the Sales line port's approved-line list. One untracked read through the same company-filtered context (and the company
/// again, explicitly), rebuilt through the domain like every read; it writes nothing.
/// </summary>
public sealed partial class SalesStore
{
    public async Task<SalesDraftPage> ListShippableDraftsAsync(int pageSize, string? cursor, CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Sayfa boyutu 1–{MaximumPageSize} olmalıdır.");
        }

        var open = (int)SalesDraftState.Draft;
        var approved = (int)InternalApprovalState.Approved;
        var query = dbContext.Drafts.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.State == open
                && (row.ApprovalState == approved
                    || dbContext.DraftRevisions.Any(revision => revision.CompanyId == companyId && revision.DraftId == row.Id)));
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
