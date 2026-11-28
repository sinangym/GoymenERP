using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore : ISalesPriceFloorDecisionReadStoreV1
{
    public async Task<SalesPriceFloorDecisionSourceV1?> ReadPriceFloorDecisionSourceAsync(Guid draftId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshFence);
        var company = RequireResolvedCompany();
        await Fence();
        // The selected header and all ACTIVE commercial lines are observed by one statement; never a live owner lookup.
        var rows = await (from header in dbContext.Drafts.AsNoTracking()
                          where header.Id == draftId && header.CompanyId == company
                          from line in dbContext.DraftLines.AsNoTracking()
                              .Where(x => x.CompanyId == company && x.DraftId == header.Id && x.RemovedInRevision == null).DefaultIfEmpty()
                          orderby line!.LineNo
                          select new { Header = header, Line = line })
            .Take(SalesOrderDraft.MaximumLines + 1).ToArrayAsync(cancellationToken);
        await Fence();
        if (rows.Length == 0) return null;
        var h = rows[0].Header;
        if (rows.Length > SalesOrderDraft.MaximumLines || rows.Any(x => x.Line is null || x.Header.Version != h.Version
            || !x.Header.RowVersion.SequenceEqual(h.RowVersion))
            || rows.Select(x => x.Line!.LineId).Distinct().Count() != rows.Length
            || rows.Select(x => x.Line!.LineNo).Distinct().Count() != rows.Length
            || !Enum.IsDefined((SalesDraftState)h.State) || !Enum.IsDefined((InternalApprovalState)h.ApprovalState))
            throw new InvalidOperationException("Kayıtlı taban fiyat kararının tam satırları okunamadı.");
        return new(company, h.Id, h.DocumentNo, h.Version, h.Revision, ((SalesDraftState)h.State).ToString(),
            ((InternalApprovalState)h.ApprovalState).ToString(), h.PartyId, h.CompanyAccountId, h.CurrencyId, h.BusinessDate,
            Array.AsReadOnly(rows.Select(x => x.Line!).Select(x => new SalesPriceFloorDecisionSourceLineV1(x.LineId, x.LineNo,
                x.ProductId, x.ProductVersion, x.ProductCode, x.ProductName, x.OrderUnitId, x.QuantityText,
                SalesLinePricingJson.Restore(x.UnitPrice, x.PriceUnitId, x.DiscountPercent, x.PricingJson))).ToArray()));

        async Task Fence()
        {
            cancellationToken.ThrowIfCancellationRequested(); RequireCompany(company);
            await freshFence(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); RequireCompany(company);
        }
    }
}
