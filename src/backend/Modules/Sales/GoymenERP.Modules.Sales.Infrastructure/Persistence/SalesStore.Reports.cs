using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quotes;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// API-41 / API-42: the read-only report queries of the Sales store. Untracked reads through the same company-filtered context (and
/// the company again, explicitly); orders and quotes are rebuilt through the domain like every read; nothing is written. API-41 is
/// answered by keyset positions the service keeps in its own cursor; the lost-quote cursor is opaque, bound to the company, the
/// ordering AND the filter (a cursor of another filter is refused, never read as a position).
/// </summary>
public sealed partial class SalesStore : ISalesReportReadStore
{
    public async Task<IReadOnlyList<SalesOpenOrderGroupScope>> ListOpenOrderGroupsAsync(SalesOpenOrderReportFilter filter,
        SalesOpenOrderGroupKey? after, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var companyId = RequireResolvedCompany();
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        var rows = OpenLines(companyId, filter);
        if (after is { } a)
        {
            rows = rows.Where(row => row.CompanyAccountId.CompareTo(a.CompanyAccountId) > 0
                || row.CompanyAccountId == a.CompanyAccountId && (row.PartyId.CompareTo(a.PartyId) > 0
                    || row.PartyId == a.PartyId && (row.ProductId.CompareTo(a.ProductId) > 0
                        || row.ProductId == a.ProductId && row.OrderUnitId.CompareTo(a.UnitId) > 0)));
        }

        var groups = await rows
            .GroupBy(row => new { row.CompanyAccountId, row.PartyId, row.ProductId, row.OrderUnitId })
            .Select(group => new
            {
                group.Key.CompanyAccountId,
                group.Key.PartyId,
                group.Key.ProductId,
                group.Key.OrderUnitId,
                Orders = group.Select(row => row.DraftId).Distinct().Count(),
            })
            .OrderBy(group => group.CompanyAccountId).ThenBy(group => group.PartyId).ThenBy(group => group.ProductId)
            .ThenBy(group => group.OrderUnitId)
            .Take(take)
            .ToListAsync(cancellationToken);
        return [.. groups.Select(group => new SalesOpenOrderGroupScope(
            new SalesOpenOrderGroupKey(group.CompanyAccountId, group.PartyId, group.ProductId, group.OrderUnitId), group.Orders))];
    }

    public async Task<IReadOnlyList<SalesOrderDraft>> ListOpenOrdersOfGroupAsync(SalesOpenOrderReportFilter filter, SalesOpenOrderGroupKey key,
        Guid? afterOrderId, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(key);
        var companyId = RequireResolvedCompany();
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        var ids = OpenLines(companyId, filter)
            .Where(row => row.CompanyAccountId == key.CompanyAccountId && row.PartyId == key.PartyId && row.ProductId == key.ProductId
                && row.OrderUnitId == key.UnitId)
            .Select(row => row.DraftId)
            .Distinct();
        if (afterOrderId is { } after)
        {
            ids = ids.Where(id => id.CompareTo(after) > 0);
        }

        var page = await ids.OrderBy(id => id).Take(take).ToListAsync(cancellationToken);
        if (page.Count == 0)
        {
            return [];
        }

        var orderRows = await dbContext.Drafts.AsNoTracking()
            .Where(row => row.CompanyId == companyId && page.Contains(row.Id))
            .ToListAsync(cancellationToken);
        // The caller pages by id: answer in the id order the database chose for the page.
        var restored = await RestoreAsync(orderRows, cancellationToken);
        var position = page.Select((id, index) => (id, index)).ToDictionary(entry => entry.id, entry => entry.index);
        return [.. restored.OrderBy(draft => position[draft.Id])];
    }

    public async Task<SalesQuotePage> ListLostQuotesAsync(SalesLostQuoteReportFilter filter, int pageSize, string? cursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var companyId = RequireResolvedCompany();
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Sayfa boyutu 1–{MaximumPageSize} olmalıdır.");
        }

        var lost = (int)SalesQuoteState.Lost;
        var query = dbContext.Quotes.AsNoTracking().Where(row => row.CompanyId == companyId && row.State == lost && row.LostAtUtc != null);
        if (filter.LostFromUtc is { } from)
        {
            query = query.Where(row => row.LostAtUtc >= from);
        }

        if (filter.LostBeforeUtc is { } before)
        {
            query = query.Where(row => row.LostAtUtc < before);
        }

        if (filter.CompanyAccountId is { } account)
        {
            query = query.Where(row => row.CompanyAccountId == account);
        }

        if (filter.ReasonId is { } reason)
        {
            query = query.Where(row => row.LossReasonId == reason);
        }

        var ordering = ReportCursor.LostQuoteOrdering(filter);
        if (cursor is not null)
        {
            var (lostAtUtc, id) = ListCursor.Decode(cursor, companyId, ordering);
            query = query.Where(row => row.LostAtUtc < lostAtUtc || (row.LostAtUtc == lostAtUtc && row.Id.CompareTo(id) < 0));
        }

        var rows = await query.OrderByDescending(row => row.LostAtUtc).ThenByDescending(row => row.Id).Take(pageSize + 1)
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var page = hasMore ? rows.Take(pageSize).ToList() : rows;
        var quotes = new List<SalesQuote>(page.Count);
        foreach (var row in page)
        {
            quotes.Add(await RestoreQuoteAsync(row, cancellationToken));
        }

        var last = page.LastOrDefault();
        return new SalesQuotePage(quotes,
            hasMore && last?.LostAtUtc is { } lastLost ? ListCursor.Encode(companyId, lastLost, last.Id, ordering) : null);
    }

    /// <summary>
    /// The current lines of the open-status orders of the filter — Draft lifecycle, approved now or earlier (an archived approved
    /// revision) — as (draft, account, party, product, unit) rows.
    /// </summary>
    private IQueryable<OpenLineRow> OpenLines(Guid companyId, SalesOpenOrderReportFilter filter)
    {
        var open = (int)SalesDraftState.Draft;
        var approved = (int)InternalApprovalState.Approved;
        var drafts = dbContext.Drafts.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.State == open
                && (row.ApprovalState == approved
                    || dbContext.DraftRevisions.Any(revision => revision.CompanyId == companyId && revision.DraftId == row.Id)));
        if (filter.From is { } from)
        {
            drafts = drafts.Where(row => row.BusinessDate >= from);
        }

        if (filter.To is { } to)
        {
            drafts = drafts.Where(row => row.BusinessDate <= to);
        }

        if (filter.CompanyAccountId is { } account)
        {
            drafts = drafts.Where(row => row.CompanyAccountId == account);
        }

        return dbContext.DraftLines.AsNoTracking()
            .Where(line => line.CompanyId == companyId)
            .Join(drafts, line => line.DraftId, draft => draft.Id, (line, draft) => new OpenLineRow
            {
                DraftId = draft.Id,
                CompanyAccountId = draft.CompanyAccountId,
                PartyId = draft.PartyId,
                ProductId = line.ProductId,
                OrderUnitId = line.OrderUnitId,
            });
    }

    private sealed class OpenLineRow
    {
        public Guid DraftId { get; init; }
        public Guid CompanyAccountId { get; init; }
        public Guid PartyId { get; init; }
        public Guid ProductId { get; init; }
        public Guid OrderUnitId { get; init; }
    }

    /// <summary>The report cursors: base64url JSON bound to the company, the ordering and a digest of the filter.</summary>
    internal static class ReportCursor
    {
        private const string LostQuoteOrderingPrefix = "reports.lost-quotes: lostAtUtc desc, id desc";

        public static string LostQuoteOrdering(SalesLostQuoteReportFilter filter) =>
            LostQuoteOrderingPrefix + "|" + Digest(Instant(filter.LostFromUtc), Instant(filter.LostBeforeUtc), Id(filter.CompanyAccountId),
                Id(filter.ReasonId));

        private static string Digest(params string[] parts) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', parts))));

        private static string Instant(DateTimeOffset? instant) =>
            instant?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? "-";

        private static string Id(Guid? id) => id?.ToString("D") ?? "-";
    }
}
