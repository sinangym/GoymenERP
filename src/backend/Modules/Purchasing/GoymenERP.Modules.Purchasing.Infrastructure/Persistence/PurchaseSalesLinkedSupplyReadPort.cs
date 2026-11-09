using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// A4 / MOD-03: the Purchasing-owned answer to <see cref="IPurchaseSalesLinkedSupplyReadPortV1"/> over Purchasing's own
/// company-filtered rows. It starts at the request line source index (<c>IX_PurchaseRequestLines_Source</c>: company, kind Sales,
/// the Sales order), follows <c>PurchaseOrderLines.SourceRequestLineId</c> to the APPROVED orders' lines and lists the commitments
/// that still have a remainder. Untracked; creates nothing (no attempt, audit or version effect). A stored value that cannot be
/// answered throws (the host adapter reads it as Unreadable), never a guessed quantity.
/// </summary>
internal sealed class PurchaseSalesLinkedSupplyReadPort(PurchasingDbContext dbContext, ICompanyContext companyContext)
    : IPurchaseSalesLinkedSupplyReadPortV1
{
    /// <summary>The most Sales order line ids one query may narrow to (a bound, never an unbounded IN list).</summary>
    public const int MaximumLineFilter = 500;

    private const int ApprovedStatus = (int)PurchaseOrderStatus.Approved;
    private const int SalesSource = (int)PurchaseRequestSourceKind.Sales;

    public async Task<IReadOnlyList<PurchaseSalesLinkedSupplyLineV1>> ListOpenForSalesOrderAsync(
        Guid companyId, Guid salesOrderId, IReadOnlyList<Guid>? salesOrderLineIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || companyContext.CompanyId != companyId)
        {
            // Deny-first: without the caller's resolved company, or for another company, there is no tenant filter to read under.
            throw new InvalidOperationException("Bağlı alış yalnız çözümlenmiş şirket bağlamında okunur.");
        }

        if (salesOrderId == Guid.Empty || salesOrderLineIds is { Count: > MaximumLineFilter } || salesOrderLineIds?.Contains(Guid.Empty) == true)
        {
            throw new ArgumentException("Bağlı alış sorgusu geçersiz.", nameof(salesOrderId));
        }

        var lineFilter = salesOrderLineIds is { Count: > 0 } ? salesOrderLineIds.Distinct().ToArray() : null;
        var requestLines = dbContext.RequestLines.AsNoTracking()
            .Where(line => line.SourceKind == SalesSource && line.SourceDocumentId == salesOrderId);
        if (lineFilter is not null)
        {
            requestLines = requestLines.Where(line => line.SourceDocumentLineId != null && lineFilter.Contains(line.SourceDocumentLineId.Value));
        }

        var rows = await (
            from orderLine in dbContext.OrderLines.AsNoTracking()
            join order in dbContext.Orders.AsNoTracking() on new { orderLine.CompanyId, Id = orderLine.OrderId } equals new { order.CompanyId, order.Id }
            join requestLine in requestLines on new { orderLine.CompanyId, Id = orderLine.SourceRequestLineId } equals new { requestLine.CompanyId, Id = (Guid?)requestLine.LineId }
            where order.Status == ApprovedStatus && order.SourceRequestId == requestLine.RequestId
            select new PurchaseSalesLinkedSupplyRow(
                orderLine.CompanyId, orderLine.OrderId, orderLine.LineId, orderLine.LineNo, orderLine.Kind, orderLine.ParentLineId,
                requestLine.SourceDocumentLineId, orderLine.UnitId, orderLine.UnitIsKilogram,
                orderLine.Ordered, orderLine.Technical, orderLine.Accepted, orderLine.Cancelled))
            .ToListAsync(cancellationToken);

        return PurchaseSalesLinkedSupplyComposer.Compose(rows, companyId);
    }
}

/// <summary>One stored order line reached from a Sales-sourced request line (general or technical), before the commitment is composed.</summary>
internal sealed record PurchaseSalesLinkedSupplyRow(
    Guid CompanyId, Guid OrderId, Guid LineId, int LineNo, int Kind, Guid? ParentLineId, Guid? SalesOrderLineId, Guid UnitId,
    bool UnitIsKilogram, decimal Ordered, decimal Technical, decimal Accepted, decimal Cancelled);

/// <summary>
/// The one rule of "open": per GENERAL line, committed (ordered − technical) − accepted − cancelled, plus every technical slice of
/// it (slice − accepted − cancelled). A technical line carries its general line's request source, so it is reached by the same
/// index; it is folded into its general line and never listed or counted a second time. A commitment with nothing left is closed
/// and not listed. A row that cannot be answered (other company, no Sales line, a technical slice without its general line, a
/// negative remainder, a value above the unit's scale) throws: the answer is Unreadable, never a guess.
/// </summary>
internal static class PurchaseSalesLinkedSupplyComposer
{
    public static IReadOnlyList<PurchaseSalesLinkedSupplyLineV1> Compose(IReadOnlyCollection<PurchaseSalesLinkedSupplyRow> rows, Guid companyId)
    {
        var general = new List<PurchaseSalesLinkedSupplyRow>();
        var slices = new Dictionary<Guid, List<PurchaseSalesLinkedSupplyRow>>();
        foreach (var row in rows)
        {
            if (row.CompanyId != companyId || row.SalesOrderLineId is not { } || !Enum.IsDefined(typeof(PurchaseOrderLineKind), row.Kind))
            {
                throw new InvalidOperationException("Kayıtlı alış satırı okunamadı.");
            }

            if ((PurchaseOrderLineKind)row.Kind == PurchaseOrderLineKind.General)
            {
                general.Add(row);
            }
            else if (row.ParentLineId is { } parent)
            {
                if (!slices.TryGetValue(parent, out var list))
                {
                    slices[parent] = list = [];
                }

                list.Add(row);
            }
            else
            {
                throw new InvalidOperationException("Kayıtlı teknik satırın genel satırı okunamadı.");
            }
        }

        if (slices.Keys.Any(parent => general.All(line => line.LineId != parent)))
        {
            throw new InvalidOperationException("Kayıtlı teknik satırın genel satırı okunamadı.");
        }

        var lines = new List<PurchaseSalesLinkedSupplyLineV1>();
        foreach (var line in general.OrderBy(line => line.OrderId).ThenBy(line => line.LineNo))
        {
            var open = line.Ordered - line.Technical - line.Accepted - line.Cancelled;
            if (slices.TryGetValue(line.LineId, out var own))
            {
                open += own.Sum(slice => slice.Ordered - slice.Accepted - slice.Cancelled);
            }

            if (open < 0m)
            {
                throw new InvalidOperationException("Kayıtlı alış satırının kalanı negatif.");
            }

            if (open == 0m)
            {
                continue;
            }

            var unit = line.UnitIsKilogram ? QuantityUnit.Kilogram : QuantityUnit.Piece;
            lines.Add(new PurchaseSalesLinkedSupplyLineV1(
                line.CompanyId, line.OrderId, line.LineId, line.SalesOrderLineId!.Value, PurchaseOrderStatus.Approved.ToString(),
                Quantity.Of(open, unit).Value.ToString(CultureInfo.InvariantCulture), line.UnitId));
        }

        return lines;
    }
}
