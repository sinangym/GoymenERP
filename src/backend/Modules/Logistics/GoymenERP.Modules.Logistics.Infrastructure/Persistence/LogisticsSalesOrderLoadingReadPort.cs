using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

/// <summary>
/// DEC-SAL-005 ("irsaliyesiz yükleme"): the Logistics-owned answer to the Sales-declared <see cref="ISalesOrderLoadingReadPortV1"/>
/// — per Sales order line (the D4 binding every loading-instruction line carries) and unit, how many instruction lines Logistics
/// holds for it, in any instruction state (Logistics has no cancellation of an instruction on main), and their instructed,
/// loaded, shipped and delivered totals. It complements the AT-024 <see cref="LogisticsSalesOrderDeliveryReadPort"/> (definite
/// totals only) with the lines that are only instructed or loaded. It reads Logistics's own company-filtered lines only
/// (another company's are invisible, never a 403), untracked, and creates nothing. The totals are the stored share totals
/// (<c>CK_LoadingInstructionLines_Shares</c>: delivered ≤ shipped ≤ loaded ≤ instructed). The same Logistics-owned answer also
/// counts distinct approved, non-cancelled dispatch-note invoice-source candidates bound to the order for DEC-SAL-005's
/// "faturalanmamış irsaliye" category. No authority (LOG-4 D6): the Sales caller measures its own name around the call.
/// </summary>
internal sealed class LogisticsSalesOrderLoadingReadPort(LogisticsDbContext dbContext, ICompanyContext companyContext)
    : ISalesOrderLoadingReadPortV1
{
    public Task<SalesOrderLoadingReadAnswerV1> ReadOrderLoadingAsync(Guid orderId,
        CancellationToken cancellationToken = default) => ReadAsync(orderId, includeDispatchNotes: false, cancellationToken);

    public Task<SalesOrderLoadingReadAnswerV1> ReadOrderCancellationImpactAsync(Guid orderId,
        CancellationToken cancellationToken = default) => ReadAsync(orderId, includeDispatchNotes: true, cancellationToken);

    private async Task<SalesOrderLoadingReadAnswerV1> ReadAsync(Guid orderId, bool includeDispatchNotes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            // Without a server-resolved company there is no tenant filter to read under: refuse, never read unscoped.
            throw new InvalidOperationException("Yükleme okuması yalnız çözümlenmiş şirket bağlamında yapılır.");
        }

        var companyId = companyContext.CompanyId;
        if (orderId == Guid.Empty)
        {
            return new SalesOrderLoadingReadAnswerV1(companyId, orderId, []);
        }

        var totals = await dbContext.Lines.AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.SourceOrderId == orderId)
            .GroupBy(line => new { line.SourceOrderLineId, line.UnitId })
            .Select(group => new
            {
                group.Key.SourceOrderLineId,
                group.Key.UnitId,
                Count = group.Count(),
                Instructed = group.Sum(line => line.Instructed),
                Loaded = group.Sum(line => line.Loaded),
                Shipped = group.Sum(line => line.Shipped),
                Delivered = group.Sum(line => line.Delivered),
            })
            .ToListAsync(cancellationToken);

        var activeApprovedDispatchNoteCount = includeDispatchNotes
            ? await (
                from note in dbContext.DispatchNotes.AsNoTracking()
                join line in dbContext.DispatchNoteLines.AsNoTracking()
                    on new { note.CompanyId, note.InstructionId, note.DispatchNoteId }
                    equals new { line.CompanyId, line.InstructionId, line.DispatchNoteId }
                where note.CompanyId == companyId
                    && note.Status == (int)DispatchNoteStatus.Approved
                    && !line.Released
                    && line.SourceOrderId == orderId
                select note.DispatchNoteId)
                .Distinct()
                .CountAsync(cancellationToken)
            : 0;

        return new SalesOrderLoadingReadAnswerV1(companyId, orderId,
            [.. totals.OrderBy(total => total.SourceOrderLineId).ThenBy(total => total.UnitId)
                .Select(total => new SalesOrderLineLoadingV1(total.SourceOrderLineId, total.UnitId, total.Count, total.Instructed,
                    total.Loaded, total.Shipped, total.Delivered))],
            activeApprovedDispatchNoteCount);
    }
}
