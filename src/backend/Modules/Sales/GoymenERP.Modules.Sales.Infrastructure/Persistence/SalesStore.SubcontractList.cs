using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore : ISalesSubcontractInvoiceListStoreV1
{
    public async Task<SalesSubcontractInvoiceListV1> ListSubcontractInvoicesAsync(Guid salesOrderId, int pageSize, string? cursor,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        if (salesOrderId == Guid.Empty || pageSize is < 1 or > SalesInvoiceApiV1.MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        var ordering = $"subcontract:{salesOrderId:D}: createdAtUtc desc, id desc";
        var query = dbContext.SubcontractServiceInvoices.AsNoTracking().Where(row => row.SalesOrderId == salesOrderId);
        if (cursor is not null)
        {
            var (createdAtUtc, id) = ListCursor.Decode(cursor, companyId, ordering);
            query = query.Where(row => row.CreatedAtUtc < createdAtUtc || (row.CreatedAtUtc == createdAtUtc && row.Id.CompareTo(id) < 0));
        }
        var rows = await query.OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id).Take(pageSize + 1)
            .Select(row => new { row.Id, row.DocumentNo, row.State, row.Version, row.BusinessDate, row.CreatedAtUtc })
            .ToListAsync(cancellationToken);
        if (RequireResolvedCompany() != companyId) throw new InvalidOperationException("Şirket bağlamı okuma sırasında değişti.");
        var items = rows.Take(pageSize).ToArray();
        var next = rows.Count > pageSize ? ListCursor.Encode(companyId, items[^1].CreatedAtUtc, items[^1].Id, ordering) : null;
        return new(salesOrderId, [.. items.Select(row => new SalesSubcontractInvoiceListItemV1(row.Id, row.DocumentNo,
            ((SalesSubcontractServiceInvoiceState)row.State).ToString(), row.Version.ToString(CultureInfo.InvariantCulture),
            row.BusinessDate, row.CreatedAtUtc))], next);
    }
}
