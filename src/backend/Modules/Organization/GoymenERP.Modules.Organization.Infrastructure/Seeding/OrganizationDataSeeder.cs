using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Domain.Locations;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Domain.WorkCenters;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GoymenERP.Modules.Organization.Infrastructure.Seeding;

internal sealed class OrganizationDataSeeder(
    OrganizationDbContext dbContext,
    IOptions<OrganizationSeedOptions> options)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var seed = options.Value;
        Validate(seed);

        var existingCompanyIds = await dbContext.Companies
            .IgnoreQueryFilters()
            .Select(x => x.Id)
            .ToHashSetAsync(cancellationToken);
        var existingSites = await dbContext.Sites
            .IgnoreQueryFilters()
            .Select(x => new { x.CompanyId, x.Code })
            .ToListAsync(cancellationToken);
        var existingLocations = await dbContext.Locations
            .IgnoreQueryFilters()
            .Select(x => new { x.CompanyId, x.Code })
            .ToListAsync(cancellationToken);
        var existingWorkCenters = await dbContext.WorkCenters
            .IgnoreQueryFilters()
            .Select(x => new { x.CompanyId, x.Code })
            .ToListAsync(cancellationToken);

        var siteKeys = existingSites
            .Select(x => (x.CompanyId, x.Code))
            .ToHashSet();
        var locationKeys = existingLocations
            .Select(x => (x.CompanyId, x.Code))
            .ToHashSet();
        var workCenterKeys = existingWorkCenters
            .Select(x => (x.CompanyId, x.Code))
            .ToHashSet();

        foreach (var companySeed in seed.Companies)
        {
            if (existingCompanyIds.Add(companySeed.Id))
            {
                dbContext.Companies.Add(new Company(
                    companySeed.Id,
                    companySeed.Code,
                    companySeed.LegalName,
                    companySeed.IsPlaceholder));
            }

            foreach (var siteSeed in seed.SiteTemplates)
            {
                var siteId = DeterministicGuid.Create(companySeed.Id, "site", siteSeed.Code);
                if (siteKeys.Add((companySeed.Id, siteSeed.Code)))
                {
                    dbContext.Sites.Add(new Site(
                        siteId,
                        companySeed.Id,
                        siteSeed.Code,
                        siteSeed.Name,
                        Enum.Parse<SiteType>(siteSeed.Type, ignoreCase: true)));
                }

                var isExternal = Enum.Parse<SiteType>(siteSeed.Type, ignoreCase: true)
                    == SiteType.ExternalWarehouse;

                foreach (var locationSeed in siteSeed.Locations)
                {
                    if (!locationKeys.Add((companySeed.Id, locationSeed.Code)))
                    {
                        continue;
                    }

                    dbContext.Locations.Add(new Location(
                        DeterministicGuid.Create(companySeed.Id, "location", locationSeed.Code),
                        companySeed.Id,
                        siteId,
                        locationSeed.Code,
                        locationSeed.Name,
                        isExternal));
                }
            }

            foreach (var workCenterSeed in seed.WorkCenters)
            {
                if (!workCenterKeys.Add((companySeed.Id, workCenterSeed.Code)))
                {
                    continue;
                }

                dbContext.WorkCenters.Add(new WorkCenter(
                    DeterministicGuid.Create(companySeed.Id, "work-center", workCenterSeed.Code),
                    companySeed.Id,
                    DeterministicGuid.Create(
                        companySeed.Id,
                        "location",
                        workCenterSeed.LocationCode),
                    workCenterSeed.Code,
                    workCenterSeed.Name,
                    Enum.Parse<WorkCenterKind>(workCenterSeed.Kind, ignoreCase: true)));
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void Validate(OrganizationSeedOptions seed)
    {
        if (seed.Companies.Count == 0 || seed.SiteTemplates.Count == 0)
        {
            throw new InvalidOperationException("Organizasyon seed yapılandırması boş olamaz.");
        }

        if (seed.Companies.Any(x => x.Id == Guid.Empty))
        {
            throw new InvalidOperationException("Seed şirket kimliği boş olamaz.");
        }

        var locationCodes = seed.SiteTemplates
            .SelectMany(x => x.Locations)
            .Select(x => x.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (seed.WorkCenters.Any(x => !locationCodes.Contains(x.LocationCode)))
        {
            throw new InvalidOperationException("İş merkezi, tanımsız bir lokasyona bağlanamaz.");
        }
    }
}
