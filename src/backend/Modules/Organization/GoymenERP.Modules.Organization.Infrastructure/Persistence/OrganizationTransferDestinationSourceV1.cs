using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

internal sealed class OrganizationTransferDestinationSourceV1(
    OrganizationDbContext dbContext,
    ICompanyContext companyContext)
    : IOrganizationTransferDestinationSourceV1
{
    public Task<OrganizationTransferDestinationV1?> ReadAsync(
        Guid companyId,
        Guid locationId,
        CancellationToken cancellationToken = default)
    {
        if (!companyContext.IsResolved || companyId != companyContext.CompanyId || locationId == Guid.Empty)
        {
            return Task.FromResult<OrganizationTransferDestinationV1?>(null);
        }

        return dbContext.Locations
            .AsNoTracking()
            .Where(x => x.Id == locationId)
            .Select(x => new OrganizationTransferDestinationV1(
                x.Id,
                x.IsActive,
                x.IsExternal,
                x.OperatorPartyId,
                x.Version))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
