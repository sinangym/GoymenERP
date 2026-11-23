using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// YOL-D42: the order of an acceptance, read from Purchasing's own company-filtered acceptance rows with the resolved company
/// named explicitly. Untracked; creates nothing. Without a resolved company it refuses rather than read unscoped.
/// </summary>
internal sealed class PurchaseAcceptanceOrderLookup(PurchasingDbContext dbContext, ICompanyContext companyContext) : IPurchaseAcceptanceOrderLookupV1
{
    public async Task<Guid?> FindOrderIdAsync(Guid acceptanceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
            throw new InvalidOperationException("Mal kabul yalnız çözümlenmiş şirket bağlamında okunur.");
        if (acceptanceId == Guid.Empty) return null;
        var companyId = companyContext.CompanyId;
        return await dbContext.Acceptances.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.AcceptanceId == acceptanceId)
            .Select(row => (Guid?)row.OrderId)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
