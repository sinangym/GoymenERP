using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

/// <summary>
/// One tenant-filtered, no-tracking batch read of Organization-owned location display facts. This
/// module read carries no user permission: the consuming module authorizes its own response.
/// </summary>
internal sealed class OrganizationLocationDisplayReader(
    OrganizationDbContext dbContext,
    ICompanyContext companyContext)
    : IOrganizationLocationDisplayReaderV1
{
    private const int MaximumLocations = 100;

    public async Task<IReadOnlyList<OrganizationLocationDisplayV1>> ReadAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> locationIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireCompany(companyId);
        ArgumentNullException.ThrowIfNull(locationIds);

        if (locationIds.Count is < 1 or > MaximumLocations
            || locationIds.Any(id => id == Guid.Empty)
            || locationIds.Distinct().Count() != locationIds.Count)
        {
            throw new OrganizationLocationDisplayQueryException();
        }

        var requestedIds = locationIds.ToArray();
        var rows = await dbContext.Locations
            .AsNoTracking()
            .Where(location => requestedIds.Contains(location.Id))
            .OrderBy(location => location.Id)
            .Select(location => new OrganizationLocationDisplayV1(
                location.Id,
                location.Code,
                location.Name))
            .ToListAsync(cancellationToken);

        // Query filters protect the SQL read. This second live fence prevents already captured
        // rows from being released if the request company context changed while the query awaited.
        RequireCompany(companyId);
        return rows;
    }

    private void RequireCompany(Guid companyId)
    {
        if (!companyContext.IsResolved
            || companyContext.CompanyId == Guid.Empty
            || companyId == Guid.Empty
            || companyId != companyContext.CompanyId)
        {
            throw new OrganizationCompanyContextMismatchException();
        }
    }
}
