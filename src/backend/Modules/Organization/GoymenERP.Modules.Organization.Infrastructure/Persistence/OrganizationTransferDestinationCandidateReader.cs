using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

internal sealed class OrganizationTransferDestinationCandidateReader(OrganizationDbContext database, ICompanyContext company)
    : IOrganizationTransferDestinationCandidatesV1
{
    public async Task<OrganizationTransferDestinationCandidatesV1> SearchAsync(Guid companyId,
        OrganizationLocationCandidateQueryV1 query, CancellationToken cancellationToken = default)
    {
        Same();
        ArgumentNullException.ThrowIfNull(query);
        var q = query.Q?.Trim();
        if (query.Limit is < 1 or > 100 || q?.Length > 200 || q?.Any(char.IsControl) == true)
            throw new OrganizationLocationCandidateQueryException();
        var rows = from location in database.Locations.AsNoTracking()
            join site in database.Sites.AsNoTracking() on new { location.CompanyId, location.SiteId }
                equals new { site.CompanyId, SiteId = site.Id }
            where location.CompanyId == companyId && location.IsActive
            select new { location.Id, location.Code, location.Name, location.SiteId, SiteCode = site.Code, SiteName = site.Name,
                location.IsExternal, location.OperatorPartyId, location.Version };
        if (!string.IsNullOrWhiteSpace(q)) rows = rows.Where(x => x.Code.Contains(q) || x.Name.Contains(q)
            || x.SiteCode.Contains(q) || x.SiteName.Contains(q));
        var page = await rows.OrderBy(x => x.Code).ThenBy(x => x.Id).Take(query.Limit + 1).ToListAsync(cancellationToken);
        Same();
        if (page.Any(x => x.Version < 1 || x.IsExternal && (!x.OperatorPartyId.HasValue || x.OperatorPartyId == Guid.Empty)))
            throw new InvalidOperationException("Transfer destination owner facts are incomplete.");
        return new(page.Take(query.Limit).Select(x => new OrganizationTransferDestinationCandidateV1(x.Id, x.Code, x.Name,
            x.SiteId, x.SiteCode, x.SiteName, x.IsExternal, x.OperatorPartyId, x.Version)).ToArray(), page.Count > query.Limit);

        void Same()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!company.IsResolved || companyId == Guid.Empty || company.CompanyId != companyId)
                throw new OrganizationCompanyContextMismatchException();
        }
    }
}
