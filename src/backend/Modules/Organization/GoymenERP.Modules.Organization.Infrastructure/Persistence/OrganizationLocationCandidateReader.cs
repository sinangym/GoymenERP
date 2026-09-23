using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

/// <summary>
/// Tenant-filtered module read for location selectors. It releases only the location's accepted
/// eligibility facts (active and not external) and the joined site's identity; it applies no user
/// permission and does not infer a site-activity cascade.
/// </summary>
internal sealed class OrganizationLocationCandidateReader(
    OrganizationDbContext dbContext,
    ICompanyContext companyContext)
    : IOrganizationLocationCandidatesV1
{
    private const int MaximumLimit = 100;
    private const int MaximumQueryLength = 200;

    public async Task<OrganizationLocationCandidatesV1> SearchAsync(
        Guid companyId,
        OrganizationLocationCandidateQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireCompany(companyId);
        ArgumentNullException.ThrowIfNull(query);

        var q = query.Q?.Trim();
        if (query.Limit is < 1 or > MaximumLimit
            || q?.Length > MaximumQueryLength
            || q?.Any(char.IsControl) == true)
        {
            throw new OrganizationLocationCandidateQueryException();
        }

        if (string.IsNullOrWhiteSpace(q))
        {
            q = null;
        }

        var candidates =
            from location in dbContext.Locations.AsNoTracking()
            join site in dbContext.Sites.AsNoTracking()
                on new { location.CompanyId, location.SiteId } equals new { site.CompanyId, SiteId = site.Id }
            where location.CompanyId == companyId && location.IsActive && !location.IsExternal
            select new
            {
                location.Id,
                location.Code,
                location.Name,
                location.SiteId,
                SiteCode = site.Code,
                SiteName = site.Name,
            };

        if (q is not null)
        {
            candidates = candidates.Where(item =>
                item.Code.Contains(q)
                || item.Name.Contains(q)
                || item.SiteCode.Contains(q)
                || item.SiteName.Contains(q));
        }

        var rows = await candidates
            .OrderBy(item => item.Code)
            .ThenBy(item => item.Id)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);
        var searchWindowFull = rows.Count > query.Limit;

        return new OrganizationLocationCandidatesV1(
            rows.Take(query.Limit)
                .Select(item => new OrganizationLocationCandidateV1(
                    item.Id,
                    item.Code,
                    item.Name,
                    item.SiteId,
                    item.SiteCode,
                    item.SiteName))
                .ToArray(),
            searchWindowFull);
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
