using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>
/// BLG-05b: the stored mirror of one exact account plan version for the active company. It looks up
/// candidate packages through the ordinary company-filtered catalog set (no bypass) and restores each
/// one through the catalog store's own <see cref="ApprovedAccountingSourceCatalogStoreV1.ReadAsync"/>,
/// so the reviewed store is not changed. Any package status counts (Prepared, Active, Superseded): a
/// draft keeps the names it was prepared with after a later plan is activated. Read-only.
/// </summary>
public sealed class ApprovedAccountPlanMirrorReaderV1(
    AccountingDbContext dbContext,
    ApprovedAccountingSourceCatalogStoreV1 catalog,
    ICompanyContext companyContext) : IAccountPlanMirrorReaderV1
{
    public async Task<AccountPlanMirrorV1?> ReadExactAsync(
        string sourceVersion,
        string sourceSha256,
        CancellationToken cancellationToken = default)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || string.IsNullOrWhiteSpace(sourceVersion) || string.IsNullOrWhiteSpace(sourceSha256))
        {
            return null;
        }

        // The catalog store keeps the accounts SHA-256 upper-case; filtering on it as well keeps this to
        // the packages of that exact plan (normally one), not every package that reused the version name.
        var accountsSha256 = sourceSha256.ToUpperInvariant();
        var packageIds = await dbContext.ApprovedSourceCatalogs.AsNoTracking()
            .Where(candidate => candidate.AccountPlanSourceVersion == sourceVersion
                && candidate.AccountsSourceSha256 == accountsSha256)
            .OrderBy(candidate => candidate.CreatedAtUtc)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => candidate.Id)
            .ToListAsync(cancellationToken);
        foreach (var packageId in packageIds)
        {
            var mirror = (await catalog.ReadAsync(packageId, cancellationToken))?.Package.Mirror;
            if (mirror is not null
                && mirror.CompanyId == companyContext.CompanyId
                && string.Equals(mirror.SourceVersion, sourceVersion, StringComparison.Ordinal)
                && string.Equals(mirror.SourceSha256, sourceSha256, StringComparison.OrdinalIgnoreCase))
            {
                return mirror;
            }
        }

        return null;
    }
}
