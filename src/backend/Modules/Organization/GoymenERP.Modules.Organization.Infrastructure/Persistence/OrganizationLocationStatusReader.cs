using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

/// <summary>
/// The module read of <see cref="IOrganizationLocationStatusReaderV1"/>: one tenant-filtered,
/// no-tracking read of the location's own row. The company filter is the context's own query filter
/// (never bypassed); there is no permission scope, by design.
/// </summary>
internal sealed class OrganizationLocationStatusReader(
    OrganizationDbContext dbContext,
    ICompanyContext companyContext)
    : IOrganizationLocationStatusReaderV1
{
    public async Task<OrganizationLocationStatusV1?> ReadAsync(
        Guid companyId,
        Guid locationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || companyId != companyContext.CompanyId)
        {
            throw new OrganizationCompanyContextMismatchException();
        }

        if (locationId == Guid.Empty)
        {
            return null;
        }

        return await dbContext.Locations
            .AsNoTracking()
            .Where(location => location.Id == locationId)
            .Select(location => new OrganizationLocationStatusV1(location.Id, location.IsActive, location.IsExternal))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
