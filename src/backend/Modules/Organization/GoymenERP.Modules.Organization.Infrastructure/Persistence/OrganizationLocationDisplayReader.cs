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

        // IReadOnlyCollection does not promise an immutable implementation. Snapshot once so the
        // validated batch is exactly the batch translated into the one SQL query.
        var requestedIds = locationIds.ToArray();
        if (requestedIds.Length is < 1 or > MaximumLocations
            || requestedIds.Any(id => id == Guid.Empty)
            || requestedIds.Distinct().Count() != requestedIds.Length)
        {
            throw new OrganizationLocationDisplayQueryException();
        }

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
