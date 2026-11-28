using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore : ISalesRequestDecisionReadStoreV1
{
    public async Task<SalesRequest?> ReadRequestDecisionAsync(Guid requestId, Func<CancellationToken, Task> freshFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshFence);
        var companyId = RequireResolvedCompany();
        await Fence();
        // One bounded statement observes the header and every retained line together, without tracking or borrowing a TX.
        var rows = await (from header in dbContext.Requests.AsNoTracking()
                          where header.Id == requestId && header.CompanyId == companyId
                          from line in dbContext.RequestLines.AsNoTracking()
                              .Where(x => x.RequestId == header.Id && x.CompanyId == companyId).DefaultIfEmpty()
                          orderby line!.LineNo
                          select new { Header = header, Line = line })
            .Take(SalesRequest.MaximumLines + 1).ToListAsync(cancellationToken);
        await Fence();
        if (rows.Count == 0) return null;
        if (rows.Count > SalesRequest.MaximumLines || rows.Any(x => x.Line is null
                || x.Header.Version != rows[0].Header.Version || !x.Header.RowVersion.SequenceEqual(rows[0].Header.RowVersion)))
            throw new InvalidOperationException("Kayıtlı satış talebinin tam satırları okunamadı.");
        return SalesRequestRows.Restore(rows[0].Header, rows.Select(x => x.Line!));

        async Task Fence()
        {
            cancellationToken.ThrowIfCancellationRequested(); RequireCompany(companyId);
            await freshFence(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); RequireCompany(companyId);
        }
    }
}
