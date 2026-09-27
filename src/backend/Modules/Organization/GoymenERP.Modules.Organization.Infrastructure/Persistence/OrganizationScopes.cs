using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Domain.Locations;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Domain.WorkCenters;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

/// <summary>
/// The Organization row-scope predicates, one per record kind, shared by every read and command of the module so that
/// "the row is listed", "the card answers 200" and "the command may touch it" are answered by the same rule for the
/// same permission. They run under the tenant query filter and never bypass it.
/// </summary>
internal static class OrganizationScopes
{
    /// <summary>
    /// The location scope (ORG-01/02, unchanged): company-wide, the location itself, its site, or a work centre on it.
    /// </summary>
    public static IQueryable<Location> ApplyLocationScope(
        OrganizationDbContext dbContext,
        IQueryable<Location> query,
        PermissionScope scope)
    {
        if (scope.IsCompanyWide)
        {
            return query;
        }

        var siteIds = scope.SiteIds.ToArray();
        var locationIds = scope.LocationIds.ToArray();
        var workCenterIds = scope.WorkCenterIds.ToArray();
        return query.Where(location =>
            locationIds.Contains(location.Id)
            || siteIds.Contains(location.SiteId)
            || dbContext.WorkCenters.Any(workCenter =>
                workCenter.LocationId == location.Id
                && workCenterIds.Contains(workCenter.Id)));
    }

    /// <summary>
    /// The work-centre scope (ORG-03, unchanged): company-wide, or the work centre itself, its location, or its
    /// location's site. The list, the ORG-04 card and the work-centre edit all use this one predicate.
    /// </summary>
    public static IQueryable<WorkCenter> ApplyWorkCenterScope(
        OrganizationDbContext dbContext,
        IQueryable<WorkCenter> query,
        PermissionScope scope)
    {
        if (scope.IsCompanyWide)
        {
            return query;
        }

        var siteIds = scope.SiteIds.ToArray();
        var locationIds = scope.LocationIds.ToArray();
        var workCenterIds = scope.WorkCenterIds.ToArray();
        return query.Where(workCenter =>
            workCenterIds.Contains(workCenter.Id)
            || locationIds.Contains(workCenter.LocationId)
            || dbContext.Locations.Any(location =>
                location.Id == workCenter.LocationId
                && siteIds.Contains(location.SiteId)));
    }

    /// <summary>
    /// Where a NEW location may be placed: company-wide, or a site inside the scope. A location or work-centre scope
    /// covers existing records only and never lets its holder add a location beside them.
    /// </summary>
    public static IQueryable<Site> ApplyLocationParentScope(IQueryable<Site> query, PermissionScope scope)
    {
        if (scope.IsCompanyWide)
        {
            return query;
        }

        var siteIds = scope.SiteIds.ToArray();
        return query.Where(site => siteIds.Contains(site.Id));
    }

    /// <summary>
    /// Where a NEW work centre may be placed: company-wide, a location inside the scope, or a location of a site inside
    /// the scope. A work-centre scope covers existing work centres only.
    /// </summary>
    public static IQueryable<Location> ApplyWorkCenterParentScope(IQueryable<Location> query, PermissionScope scope)
    {
        if (scope.IsCompanyWide)
        {
            return query;
        }

        var siteIds = scope.SiteIds.ToArray();
        var locationIds = scope.LocationIds.ToArray();
        return query.Where(location =>
            locationIds.Contains(location.Id)
            || siteIds.Contains(location.SiteId));
    }
}
