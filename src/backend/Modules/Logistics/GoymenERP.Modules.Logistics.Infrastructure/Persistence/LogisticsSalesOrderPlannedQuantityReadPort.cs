using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

/// <summary>
/// SAT-D367: Logistics's answer to <see cref="ILogisticsSalesOrderPlannedQuantityReadPortV1"/> — instructed minus shipped,
/// summed per (order, order line, unit) over the lines of the context company's <b>issued</b> instructions only (a draft is
/// not approved yet, a cancelled instruction no longer plans anything; F-020 refuses a cancel once a shipment exists). It
/// reads Logistics's own company-filtered lines, untracked, and creates nothing. Order lines whose open quantity is zero are
/// left out (the contract's "missing line = zero"). No authority: the Sales caller measures its own name around the call.
/// </summary>
internal sealed class LogisticsSalesOrderPlannedQuantityReadPort(LogisticsDbContext dbContext, ICompanyContext companyContext)
    : ILogisticsSalesOrderPlannedQuantityReadPortV1
{
    public async Task<LogisticsSalesOrderPlannedQuantityReadV1> ReadOpenInstructionQuantitiesAsync(Guid companyId,
        IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            // Without a server-resolved company there is no tenant filter to read under: answer nothing, never read unscoped.
            return LogisticsSalesOrderPlannedQuantityReadV1.Denied(companyId,
                LogisticsSalesOrderPlannedQuantityReasonCodesV1.CompanyNotResolved);
        }

        var contextCompanyId = companyContext.CompanyId;
        if (companyId != contextCompanyId)
        {
            // Another company's plan is never answered (not a 403 either: the caller asked under the wrong company).
            return LogisticsSalesOrderPlannedQuantityReadV1.Denied(companyId,
                LogisticsSalesOrderPlannedQuantityReasonCodesV1.CompanyMismatch);
        }

        var ids = orderIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length > LogisticsSalesOrderPlannedQuantityContractV1.MaximumOrderIds)
        {
            throw new ArgumentOutOfRangeException(nameof(orderIds),
                $"En çok {LogisticsSalesOrderPlannedQuantityContractV1.MaximumOrderIds} sipariş okunur.");
        }

        if (ids.Length == 0)
        {
            return LogisticsSalesOrderPlannedQuantityReadV1.Found(contextCompanyId, [], [], hasMore: false);
        }

        var issued = (int)LoadingInstructionStatus.Issued;
        var totals = await (
                from line in dbContext.Lines.AsNoTracking()
                join instruction in dbContext.Instructions.AsNoTracking()
                    on new { line.CompanyId, line.InstructionId } equals new { instruction.CompanyId, InstructionId = instruction.Id }
                where line.CompanyId == contextCompanyId && ids.Contains(line.SourceOrderId) && instruction.Status == issued
                select line)
            .GroupBy(line => new { line.SourceOrderId, line.SourceOrderLineId, line.UnitId, line.UnitIsKilogram })
            .Select(group => new
            {
                group.Key.SourceOrderId,
                group.Key.SourceOrderLineId,
                group.Key.UnitId,
                group.Key.UnitIsKilogram,
                Open = group.Sum(line => line.Instructed > line.Shipped ? line.Instructed - line.Shipped : 0m),
            })
            .ToListAsync(cancellationToken);

        // One Settings unit is either kilograms or pieces; two kinds under one unit id is a contradictory store.
        if (totals.GroupBy(total => new { total.SourceOrderId, total.SourceOrderLineId, total.UnitId }).Any(group => group.Count() > 1))
        {
            return LogisticsSalesOrderPlannedQuantityReadV1.Unavailable(contextCompanyId,
                LogisticsSalesOrderPlannedQuantityReasonCodesV1.RecordInconsistent);
        }

        var lines = new List<LogisticsSalesOrderPlannedQuantityLineV1>(totals.Count);
        foreach (var total in totals.OrderBy(total => total.SourceOrderId).ThenBy(total => total.SourceOrderLineId)
                     .ThenBy(total => total.UnitId))
        {
            var unit = total.UnitIsKilogram ? QuantityUnit.Kilogram : QuantityUnit.Piece;
            Quantity open;
            try
            {
                // The stored totals obey the share check; the subtraction refuses a contradictory row instead of answering it.
                // An approved exception never expands the plan and cannot consume another instruction's planned share.
                open = Quantity.Of(total.Open, unit);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                // ArgumentException: a stored value off the unit's scale; InvalidOperationException (LogisticsRuleException):
                // shipped above instructed.
                return LogisticsSalesOrderPlannedQuantityReadV1.Unavailable(contextCompanyId,
                    LogisticsSalesOrderPlannedQuantityReasonCodesV1.RecordInconsistent);
            }

            if (!open.IsZero)
            {
                lines.Add(new LogisticsSalesOrderPlannedQuantityLineV1(total.SourceOrderId, total.SourceOrderLineId, total.UnitId,
                    unit, open.Value));
            }
        }

        return Page(contextCompanyId, ids, lines);
    }

    /// <summary>Keeps whole orders only, in order-id order, until the line cap (the first order always); the rest is HasMore.</summary>
    private static LogisticsSalesOrderPlannedQuantityReadV1 Page(Guid companyId, Guid[] ids,
        List<LogisticsSalesOrderPlannedQuantityLineV1> lines)
    {
        var linesByOrder = lines.GroupBy(line => line.OrderId).ToDictionary(group => group.Key, group => group.ToList());
        var answeredOrderIds = new List<Guid>(ids.Length);
        var answeredLines = new List<LogisticsSalesOrderPlannedQuantityLineV1>(lines.Count);
        var hasMore = false;
        foreach (var orderId in ids.OrderBy(id => id))
        {
            var orderLines = linesByOrder.TryGetValue(orderId, out var found) ? found : [];
            // The first order is always answered whole, so one large order can never stall the consumer on an empty page.
            if (answeredOrderIds.Count > 0
                && answeredLines.Count + orderLines.Count > LogisticsSalesOrderPlannedQuantityContractV1.MaximumLines)
            {
                hasMore = true;
                break;
            }

            answeredOrderIds.Add(orderId);
            answeredLines.AddRange(orderLines);
        }

        return LogisticsSalesOrderPlannedQuantityReadV1.Found(companyId, answeredLines, answeredOrderIds, hasMore);
    }
}
