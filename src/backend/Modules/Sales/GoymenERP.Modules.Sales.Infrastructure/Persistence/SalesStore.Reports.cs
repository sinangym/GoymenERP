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

    public async Task<SalesOpenOrderBatch> ListOpenOrdersOfGroupsAsync(SalesOpenOrderReportFilter filter, SalesOpenOrderGroupKey first,
        SalesOpenOrderGroupKey last, Guid? afterOrderId, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(last);
        var companyId = RequireResolvedCompany();
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        var rows = OpenLines(companyId, filter);
        if (first == last)
        {
            rows = rows.Where(row => row.CompanyAccountId == first.CompanyAccountId && row.PartyId == first.PartyId
                && row.ProductId == first.ProductId && row.OrderUnitId == first.UnitId);
        }
        else
        {
            // The same keyset comparison as the group scan, so the range is the scan's own key order (first ≤ key ≤ last).
            rows = rows
                .Where(row => row.CompanyAccountId.CompareTo(first.CompanyAccountId) > 0
                    || row.CompanyAccountId == first.CompanyAccountId && (row.PartyId.CompareTo(first.PartyId) > 0
                        || row.PartyId == first.PartyId && (row.ProductId.CompareTo(first.ProductId) > 0
                            || row.ProductId == first.ProductId && row.OrderUnitId.CompareTo(first.UnitId) >= 0)))
                .Where(row => row.CompanyAccountId.CompareTo(last.CompanyAccountId) < 0
                    || row.CompanyAccountId == last.CompanyAccountId && (row.PartyId.CompareTo(last.PartyId) < 0
                        || row.PartyId == last.PartyId && (row.ProductId.CompareTo(last.ProductId) < 0
                            || row.ProductId == last.ProductId && row.OrderUnitId.CompareTo(last.UnitId) <= 0)));
        }

        var ids = rows.Select(row => row.DraftId).Distinct();
        if (afterOrderId is { } after)
        {
            ids = ids.Where(id => id.CompareTo(after) > 0);
        }

        // One id more than asked tells whether another order follows, without a further read.
        var page = await ids.OrderBy(id => id).Take(take + 1).ToListAsync(cancellationToken);
        var hasMore = page.Count > take;
        if (hasMore)
        {
            page.RemoveAt(take);
        }

        if (page.Count == 0)
        {
            return new SalesOpenOrderBatch([], false);
        }

        var orderRows = await dbContext.Drafts.AsNoTracking()
            .Where(row => row.CompanyId == companyId && page.Contains(row.Id))
            .ToListAsync(cancellationToken);
        // The caller pages by id: answer in the id order the database chose for the page.
        var restored = await RestoreAsync(orderRows, cancellationToken);
        var position = page.Select((id, index) => (id, index)).ToDictionary(entry => entry.id, entry => entry.index);
        return new SalesOpenOrderBatch([.. restored.OrderBy(draft => position[draft.Id])], hasMore);
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
    /// revision) — as (draft, account, party, product, unit) rows. The account and party are those of the revision the open
    /// quantity is MEASURED on (API-04 × API-41, SAT-D137; the service's <c>BasisCustomer</c> is the same rule): the
    /// customer-accepted revision when acceptance is required and one is accepted, else the approved one (the current revision when
    /// approved, else the last archived revision); the current header for the current revision, else that revision's archived
    /// header (<c>DraftRevisionHeaders</c>). The customer filter, the group key and the keyset all use it, in SQL — a pending
    /// customer change never moves an order to the new, unapproved customer, and paging stays exact.
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

        var measured = drafts
            .Select(row => new
            {
                row.Id,
                row.Revision,
                row.CompanyAccountId,
                row.PartyId,
                BasisRevision = row.CustomerAcceptanceRequired && row.CustomerAcceptedRevision != null
                    ? row.CustomerAcceptedRevision.Value
                    : row.ApprovalState == approved
                        ? row.Revision
                        : dbContext.DraftRevisions.Where(revision => revision.CompanyId == companyId && revision.DraftId == row.Id)
                            .Max(revision => (int?)revision.Revision) ?? row.Revision,
            })
            .Select(row => new
            {
                row.Id,
                CompanyAccountId = row.BasisRevision == row.Revision
                    ? row.CompanyAccountId
                    : dbContext.DraftRevisionHeaders
                        .Where(header => header.CompanyId == companyId && header.DraftId == row.Id && header.Revision == row.BasisRevision)
                        .Select(header => header.CompanyAccountId).FirstOrDefault(),
                PartyId = row.BasisRevision == row.Revision
                    ? row.PartyId
                    : dbContext.DraftRevisionHeaders
                        .Where(header => header.CompanyId == companyId && header.DraftId == row.Id && header.Revision == row.BasisRevision)
                        .Select(header => header.PartyId).FirstOrDefault(),
            });
        if (filter.CompanyAccountId is { } account)
        {
            measured = measured.Where(row => row.CompanyAccountId == account);
        }

        return dbContext.DraftLines.AsNoTracking()
            .Where(line => line.CompanyId == companyId)
            .Join(measured, line => line.DraftId, draft => draft.Id, (line, draft) => new OpenLineRow
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
