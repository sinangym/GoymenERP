using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quotes;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// API-41 / API-42: the read-only report queries of the Sales store. Untracked reads through the same company-filtered context (and
/// the company again, explicitly); orders and quotes are rebuilt through the domain like every read; nothing is written. The cursors
/// are opaque, bound to the company, the ordering AND the filter (a cursor of another filter is refused, never read as a position).
/// </summary>
public sealed partial class SalesStore : ISalesReportReadStore
{
    public async Task<SalesOpenOrderGroupPage> ListOpenOrderGroupsAsync(SalesOpenOrderReportFilter filter, int pageSize, string? cursor,
        int maximumOrders, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var companyId = RequireResolvedCompany();
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Sayfa boyutu 1–{MaximumPageSize} olmalıdır.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maximumOrders, 1);
        var binding = ReportCursor.OpenOrderBinding(filter);
        var open = OpenOrders(companyId, filter);
        var keys = dbContext.DraftLines.AsNoTracking()
            .Where(line => line.CompanyId == companyId)
            .Join(open, line => line.DraftId, draft => draft.Id, (line, draft) => new
            {
                draft.CompanyAccountId,
                draft.PartyId,
                line.ProductId,
                line.OrderUnitId,
            })
            .Distinct();
        if (cursor is not null)
        {
            var after = ReportCursor.DecodeGroup(cursor, companyId, binding);
            keys = keys.Where(key => key.CompanyAccountId.CompareTo(after.CompanyAccountId) > 0
                || key.CompanyAccountId == after.CompanyAccountId && (key.PartyId.CompareTo(after.PartyId) > 0
                    || key.PartyId == after.PartyId && (key.ProductId.CompareTo(after.ProductId) > 0
                        || key.ProductId == after.ProductId && key.OrderUnitId.CompareTo(after.UnitId) > 0)));
        }

        var rows = await keys
            .OrderBy(key => key.CompanyAccountId).ThenBy(key => key.PartyId).ThenBy(key => key.ProductId).ThenBy(key => key.OrderUnitId)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var groups = rows.Take(pageSize)
            .Select(row => new SalesOpenOrderGroupKey(row.CompanyAccountId, row.PartyId, row.ProductId, row.OrderUnitId))
            .ToArray();
        var next = hasMore ? ReportCursor.EncodeGroup(companyId, binding, groups[^1]) : null;
        if (groups.Length == 0)
        {
            return new SalesOpenOrderGroupPage([], [], false, next);
        }

        // The orders the page's keys span: candidates by account and product (a superset), narrowed to the exact keys here.
        var accounts = groups.Select(group => group.CompanyAccountId).Distinct().ToArray();
        var products = groups.Select(group => group.ProductId).Distinct().ToArray();
        var candidates = await dbContext.DraftLines.AsNoTracking()
            .Where(line => line.CompanyId == companyId && products.Contains(line.ProductId))
            .Join(open.Where(draft => accounts.Contains(draft.CompanyAccountId)), line => line.DraftId, draft => draft.Id,
                (line, draft) => new { line.DraftId, draft.CompanyAccountId, draft.PartyId, line.ProductId, line.OrderUnitId })
            .ToListAsync(cancellationToken);
        var wanted = groups.ToHashSet();
        var orderIds = candidates
            .Where(row => wanted.Contains(new SalesOpenOrderGroupKey(row.CompanyAccountId, row.PartyId, row.ProductId, row.OrderUnitId)))
            .Select(row => row.DraftId)
            .Distinct()
            .ToArray();
        if (orderIds.Length > maximumOrders)
        {
            return new SalesOpenOrderGroupPage(groups, [], true, next);
        }

        var orderRows = await dbContext.Drafts.AsNoTracking()
            .Where(row => row.CompanyId == companyId && orderIds.Contains(row.Id))
            .OrderBy(row => row.BusinessDate).ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        return new SalesOpenOrderGroupPage(groups, await RestoreAsync(orderRows, cancellationToken), false, next);
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

    /// <summary>The open orders of the filter: Draft lifecycle, approved now or earlier (an archived approved revision).</summary>
    private IQueryable<DraftRow> OpenOrders(Guid companyId, SalesOpenOrderReportFilter filter)
    {
        var open = (int)SalesDraftState.Draft;
        var approved = (int)InternalApprovalState.Approved;
        var query = dbContext.Drafts.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.State == open
                && (row.ApprovalState == approved
                    || dbContext.DraftRevisions.Any(revision => revision.CompanyId == companyId && revision.DraftId == row.Id)));
        if (filter.From is { } from)
        {
            query = query.Where(row => row.BusinessDate >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(row => row.BusinessDate <= to);
        }

        if (filter.CompanyAccountId is { } account)
        {
            query = query.Where(row => row.CompanyAccountId == account);
        }

        return query;
    }

    /// <summary>The report cursors: base64url JSON bound to the company, the ordering and a digest of the filter.</summary>
    internal static class ReportCursor
    {
        private const string OpenOrderOrdering = "reports.open-orders: companyAccountId, partyId, productId, unitId";
        private const string LostQuoteOrderingPrefix = "reports.lost-quotes: lostAtUtc desc, id desc";

        public static string OpenOrderBinding(SalesOpenOrderReportFilter filter) =>
            OpenOrderOrdering + "|" + Digest(Day(filter.From), Day(filter.To), Id(filter.CompanyAccountId));

        public static string LostQuoteOrdering(SalesLostQuoteReportFilter filter) =>
            LostQuoteOrderingPrefix + "|" + Digest(Instant(filter.LostFromUtc), Instant(filter.LostBeforeUtc), Id(filter.CompanyAccountId),
                Id(filter.ReasonId));

        public static string EncodeGroup(Guid companyId, string binding, SalesOpenOrderGroupKey after) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                new GroupPayload(companyId, binding, after.CompanyAccountId, after.PartyId, after.ProductId, after.UnitId), Json)))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static SalesOpenOrderGroupKey DecodeGroup(string cursor, Guid companyId, string binding)
        {
            GroupPayload? payload;
            try
            {
                var padded = cursor.Replace('-', '+').Replace('_', '/');
                padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
                payload = JsonSerializer.Deserialize<GroupPayload>(Encoding.UTF8.GetString(Convert.FromBase64String(padded)), Json);
            }
            catch (Exception exception) when (exception is FormatException or JsonException)
            {
                throw Refused(SalesDraftStoreCodes.InvalidCursor, "Rapor imleci çözülemedi.", exception);
            }

            if (payload is null || payload.Company != companyId || !string.Equals(payload.Binding, binding, StringComparison.Ordinal)
                || payload.CompanyAccountId == Guid.Empty || payload.PartyId == Guid.Empty || payload.ProductId == Guid.Empty
                || payload.UnitId == Guid.Empty)
            {
                throw Refused(SalesDraftStoreCodes.InvalidCursor, "Rapor imleci bu şirketin, bu sıralamanın ve bu süzgecin imleci değil.");
            }

            return new SalesOpenOrderGroupKey(payload.CompanyAccountId, payload.PartyId, payload.ProductId, payload.UnitId);
        }

        private static string Digest(params string[] parts) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', parts))));

        private static string Day(DateOnly? day) => day?.ToString("O", CultureInfo.InvariantCulture) ?? "-";

        private static string Instant(DateTimeOffset? instant) =>
            instant?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? "-";

        private static string Id(Guid? id) => id?.ToString("D") ?? "-";

        private sealed record GroupPayload(Guid Company, string Binding, Guid CompanyAccountId, Guid PartyId, Guid ProductId, Guid UnitId);
    }
}
