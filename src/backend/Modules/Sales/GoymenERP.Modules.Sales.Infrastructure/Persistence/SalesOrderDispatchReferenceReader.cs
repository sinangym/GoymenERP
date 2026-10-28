using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// API-30 / API-31: the Sales-own batch read of the order facts a dispatch share is shown with — the order header (document
/// number, customer, currency, payment terms reference) and each line's number, product and order unit, all as the Sales store
/// holds them now. Two untracked statements over the company-filtered draft tables (the second keyed by company + draft), so
/// another company's order is invisible; nothing is created and no other module's table is read. No resolved company is an
/// <see cref="InvalidOperationException"/> before any read (the consumer has measured its own name first).
/// </summary>
internal sealed class SalesOrderDispatchReferenceReader(SalesDbContext dbContext, ICompanyContext companyContext)
    : ISalesOrderDispatchReferenceReader
{
    public async Task<IReadOnlyList<SalesOrderDispatchReference>> ReadOrderReferencesAsync(IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("Satış sipariş referansı okuması çözülmüş şirket bağlamı ister.");
        }

        var ids = orderIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var companyId = companyContext.CompanyId;
        var headers = await dbContext.Drafts.AsNoTracking()
            .Where(row => row.CompanyId == companyId && ids.Contains(row.Id))
            .Select(row => new
            {
                row.Id,
                row.CompanyId,
                row.DocumentNo,
                row.PartyId,
                row.CompanyAccountId,
                row.CurrencyId,
                row.PaymentTermsId,
                row.PaymentTermsVersion,
            })
            .ToListAsync(cancellationToken);
        if (headers.Count == 0)
        {
            return [];
        }

        var found = headers.Select(row => row.Id).ToArray();
        var lines = await dbContext.DraftLines.AsNoTracking()
            .Where(row => row.CompanyId == companyId && found.Contains(row.DraftId))
            .Select(row => new
            {
                row.DraftId,
                row.LineId,
                row.LineNo,
                row.ProductId,
                row.ProductCode,
                row.ProductName,
                row.OrderUnitId,
                row.OrderUnitCode,
                row.OrderUnitIsKilogram,
            })
            .ToListAsync(cancellationToken);

        return [.. headers.OrderBy(row => row.Id).Select(row => new SalesOrderDispatchReference(row.Id, row.CompanyId, row.DocumentNo,
            row.PartyId, row.CompanyAccountId, row.CurrencyId, row.PaymentTermsId, row.PaymentTermsVersion,
            [.. lines.Where(line => line.DraftId == row.Id).OrderBy(line => line.LineNo)
                .Select(line => new SalesOrderDispatchLineReference(line.LineId, line.LineNo, line.ProductId, line.ProductCode,
                    line.ProductName, line.OrderUnitId, line.OrderUnitCode, line.OrderUnitIsKilogram))]))];
    }
}
