using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

/// <summary>
/// One tenant-filtered, no-tracking read of the selected work center's immutable planning kind.
/// The DbContext query filter remains in force and no user permission is inferred here.
/// </summary>
internal sealed class OrganizationWorkCenterPlanningReferenceReader(
    OrganizationDbContext dbContext,
    ICompanyContext companyContext)
    : IOrganizationWorkCenterPlanningReferenceV1
{
    public async Task<OrganizationWorkCenterPlanningReferenceV1?> ReadAsync(
        Guid companyId,
        Guid workCenterId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || companyId != companyContext.CompanyId)
        {
            throw new OrganizationCompanyContextMismatchException();
        }

        if (workCenterId == Guid.Empty)
        {
            return null;
        }

        var stored = await dbContext.WorkCenters
            .AsNoTracking()
            .Where(workCenter => workCenter.Id == workCenterId)
            .Select(workCenter => new { workCenter.Id, workCenter.Kind })
            .SingleOrDefaultAsync(cancellationToken);
        return stored is null
            ? null
            : new OrganizationWorkCenterPlanningReferenceV1(stored.Id, stored.Kind.ToString());
    }
}
