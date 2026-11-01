using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

/// <summary>
/// AT-024: the Logistics-owned answer to the Sales-declared <see cref="ISalesOrderDeliveryReadPortV1"/> — per Sales order
/// line (the D4 binding every loading-instruction line carries) and unit, the SHIPPED and DELIVERED totals Logistics
/// recorded, summed over every instruction of the context company. It reads Logistics's own company-filtered lines only
/// (another company's instructions are invisible, never a 403), untracked, and creates nothing: no attempt, audit, share
/// or version effect. The totals are the stored share totals the store moves in the same save as each appended share
/// (checked on every rebuild, <c>CK_LoadingInstructionLines_Shares</c>: delivered ≤ shipped ≤ loaded ≤ instructed), so a
/// replayed command or a repeated delivery id — which never appends a second share — is never counted twice.
/// The port carries no authority (LOG-4 D6): the Sales draft read measures its own name around the call.
/// </summary>
internal sealed class LogisticsSalesOrderDeliveryReadPort(LogisticsDbContext dbContext, ICompanyContext companyContext)
    : ISalesOrderDeliveryReadPortV1
{
    /// <summary>
    /// API-41: the per-order answers of up to <see cref="ISalesOrderDeliveryReadPortV1.MaximumBatchSize"/> orders in ONE grouped
    /// statement over the same company-filtered lines; every requested order answers (an empty list when nothing is recorded).
    /// </summary>
    public async Task<IReadOnlyList<SalesOrderDeliveryReadAnswerV1>> ReadOrdersDeliveriesAsync(IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderIds);
        cancellationToken.ThrowIfCancellationRequested();
        var companyId = RequireCompany();
        if (orderIds.Count > ISalesOrderDeliveryReadPortV1.MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(orderIds),
                $"En fazla {ISalesOrderDeliveryReadPortV1.MaximumBatchSize} sipariş birlikte okunur.");
        }

        var ids = orderIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [.. orderIds.Distinct().Select(orderId => new SalesOrderDeliveryReadAnswerV1(companyId, orderId, []))];
        }

        var totals = await dbContext.Lines.AsNoTracking()
                .Where(line => ids.Contains(line.SourceOrderId))
                .GroupBy(line => new { line.SourceOrderId, line.SourceOrderLineId, line.UnitId })
                .Select(group => new
                {
                    group.Key.SourceOrderId,
                    group.Key.SourceOrderLineId,
                    group.Key.UnitId,
                    Shipped = group.Sum(line => line.Shipped),
                    Delivered = group.Sum(line => line.Delivered),
                })
                .ToListAsync(cancellationToken);
        return [.. orderIds.Distinct().Select(orderId => new SalesOrderDeliveryReadAnswerV1(companyId, orderId,
            [.. totals.Where(total => total.SourceOrderId == orderId)
                .OrderBy(total => total.SourceOrderLineId).ThenBy(total => total.UnitId)
                .Select(total => new SalesOrderLineDeliveryV1(total.SourceOrderLineId, total.UnitId, total.Shipped, total.Delivered))]))];
    }

    private Guid RequireCompany()
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            // Without a server-resolved company there is no tenant filter to read under: refuse, never read unscoped.
            throw new InvalidOperationException("Teslimat okuması yalnız çözümlenmiş şirket bağlamında yapılır.");
        }

        return companyContext.CompanyId;
    }

    public async Task<SalesOrderDeliveryReadAnswerV1> ReadOrderDeliveriesAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var companyId = RequireCompany();
        if (orderId == Guid.Empty)
        {
            return new SalesOrderDeliveryReadAnswerV1(companyId, orderId, []);
        }

        var totals = await dbContext.Lines.AsNoTracking()
            .Where(line => line.SourceOrderId == orderId)
            .GroupBy(line => new { line.SourceOrderLineId, line.UnitId })
            .Select(group => new
            {
                group.Key.SourceOrderLineId,
                group.Key.UnitId,
                Shipped = group.Sum(line => line.Shipped),
                Delivered = group.Sum(line => line.Delivered),
            })
            .ToListAsync(cancellationToken);

        return new SalesOrderDeliveryReadAnswerV1(companyId, orderId,
            [.. totals.OrderBy(total => total.SourceOrderLineId).ThenBy(total => total.UnitId)
                .Select(total => new SalesOrderLineDeliveryV1(total.SourceOrderLineId, total.UnitId, total.Shipped, total.Delivered))]);
    }
}
