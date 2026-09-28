using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

/// <summary>
/// AT-025: Logistics's own recorded totals per Sales order line and unit for the sales section of the operations work list —
/// instructed, loaded, shipped and delivered summed over every instruction of the context company (the
/// <see cref="LogisticsSalesOrderDeliveryReadPort"/> shape, for a page of order lines at once). It reads Logistics's own
/// company-filtered lines only (another company's instructions are invisible, never a 403), untracked, and creates nothing.
/// The totals are the stored share totals the store moves in the same save as each appended share, so a replayed command
/// is never counted twice. No authority here: the operations work service measures its own name around the call.
/// </summary>
internal sealed class LogisticsOrderLineProgressReader(LogisticsDbContext dbContext, ICompanyContext companyContext) : ILogisticsOrderLineProgressReader
{
    /// <summary>A page is at most 100 lines (the consumer's cap); more ids is a caller error, not a bigger query.</summary>
    public const int MaximumLineIds = 100;

    public async Task<IReadOnlyList<LogisticsOrderLineProgress>> ReadAsync(IReadOnlyCollection<Guid> orderLineIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderLineIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            // Without a server-resolved company there is no tenant filter to read under: refuse, never read unscoped.
            throw new InvalidOperationException("Operasyon ilerlemesi yalnız çözümlenmiş şirket bağlamında okunur.");
        }

        var ids = orderLineIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length > MaximumLineIds)
        {
            throw new ArgumentOutOfRangeException(nameof(orderLineIds), $"En çok {MaximumLineIds} sipariş satırı okunur.");
        }

        if (ids.Length == 0)
        {
            return [];
        }

        var totals = await dbContext.Lines.AsNoTracking()
            .Where(line => ids.Contains(line.SourceOrderLineId))
            .GroupBy(line => new { line.SourceOrderLineId, line.UnitId })
            .Select(group => new
            {
                group.Key.SourceOrderLineId,
                group.Key.UnitId,
                Instructed = group.Sum(line => line.Instructed),
                Loaded = group.Sum(line => line.Loaded),
                Shipped = group.Sum(line => line.Shipped),
                Delivered = group.Sum(line => line.Delivered),
            })
            .ToListAsync(cancellationToken);

        return [.. totals.OrderBy(total => total.SourceOrderLineId).ThenBy(total => total.UnitId)
            .Select(total => new LogisticsOrderLineProgress(total.SourceOrderLineId, total.UnitId, total.Instructed, total.Loaded,
                total.Shipped, total.Delivered))];
    }
}
