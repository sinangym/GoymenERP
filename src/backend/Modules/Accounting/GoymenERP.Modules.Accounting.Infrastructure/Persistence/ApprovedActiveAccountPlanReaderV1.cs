using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>
/// FIN-S16 (M-05): the company's single ACTIVE approved source catalog package. It finds the package id through
/// the ordinary company-filtered catalog set (no bypass; one active package per company is a filtered unique
/// index) and restores it through the catalog store's own <see cref="ApprovedAccountingSourceCatalogStoreV1.ReadAsync"/>,
/// so the reviewed store is not changed. Read-only.
/// </summary>
public sealed class ApprovedActiveAccountPlanReaderV1(
    AccountingDbContext dbContext,
    ApprovedAccountingSourceCatalogStoreV1 catalog,
    ICompanyContext companyContext) : IActiveAccountPlanReaderV1
{
    private const int Active = (int)ApprovedAccountingSourceCatalogStatusV1.Active;

    public async Task<ApprovedAccountingSourceCatalogRecordV1?> ReadActiveAsync(CancellationToken cancellationToken = default)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            return null;
        }

        var packageId = await dbContext.ApprovedSourceCatalogs.AsNoTracking()
            .Where(candidate => candidate.Status == Active)
            .Select(candidate => (Guid?)candidate.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (packageId is not { } id)
        {
            return null;
        }

        var record = await catalog.ReadAsync(id, cancellationToken);
        return record is not null
            && record.CompanyId == companyContext.CompanyId
            && record.Status == ApprovedAccountingSourceCatalogStatusV1.Active
            ? record
            : null;
    }
}
