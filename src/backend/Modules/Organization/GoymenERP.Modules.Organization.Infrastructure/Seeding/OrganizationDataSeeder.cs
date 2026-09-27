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
    OrganizationBootstrapWriteScope bootstrapWriteScope,
    IOptions<OrganizationSeedOptions> options)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var seed = options.Value;
        Validate(seed);

        var existingCompanies = await dbContext.Companies
            .IgnoreQueryFilters()
            .Select(x => new { x.Id, x.Code })
            .ToListAsync(cancellationToken);
        var existingCompanyIds = existingCompanies
            .Select(x => x.Id)
            .ToHashSet();
        var existingCompanyCodes = existingCompanies
            .ToDictionary(x => x.Code, x => x.Id, StringComparer.OrdinalIgnoreCase);
        var existingSites = await dbContext.Sites
            .IgnoreQueryFilters()
            .Select(x => new { x.Id, x.CompanyId, x.Code })
            .ToListAsync(cancellationToken);
        var existingLocations = await dbContext.Locations
            .IgnoreQueryFilters()
            .Select(x => new { x.Id, x.CompanyId, x.Code })
            .ToListAsync(cancellationToken);
        var existingWorkCenters = await dbContext.WorkCenters
            .IgnoreQueryFilters()
            .Select(x => new { x.Id, x.CompanyId, x.Code })
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

        // A seeded record keeps its deterministic id when a user renames it (the master edit changes code and name),
        // so the seed also recognises its own records by id: re-adding a renamed record under its old code would
        // collide with the id it already holds and stop the host from starting.
        var siteIds = existingSites.Select(x => x.Id).ToHashSet();
        var locationIds = existingLocations.Select(x => x.Id).ToHashSet();
        var workCenterIds = existingWorkCenters.Select(x => x.Id).ToHashSet();

        foreach (var companySeed in seed.Companies)
        {
            if (!existingCompanyIds.Add(companySeed.Id))
            {
                var persistedCode = existingCompanies
                    .Single(company => company.Id == companySeed.Id)
                    .Code;
                if (!string.Equals(
                        persistedCode,
                        companySeed.Code,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Seed şirket kimliği beklenmeyen bir şirket koduna ait: {companySeed.Id:D}.");
                }
            }
            else
            {
                if (existingCompanyCodes.TryGetValue(companySeed.Code, out var codeOwnerId)
                    && codeOwnerId != companySeed.Id)
                {
                    throw new InvalidOperationException(
                        $"Seed şirket kodu başka bir şirket kimliğiyle çakışıyor: {companySeed.Code}.");
                }

                dbContext.Companies.Add(new Company(
                    companySeed.Id,
                    companySeed.Code,
                    companySeed.LegalName,
                    companySeed.Vkn,
                    companySeed.IsPlaceholder,
                    companySeed.InvoiceCapable));
                existingCompanyCodes.Add(companySeed.Code, companySeed.Id);
            }

            if (!companySeed.ApplyTopologyTemplates)
            {
                continue;
            }

            foreach (var siteSeed in seed.SiteTemplates)
            {
                var siteId = DeterministicGuid.Create(companySeed.Id, "site", siteSeed.Code);
                if (!siteIds.Contains(siteId) && siteKeys.Add((companySeed.Id, siteSeed.Code)))
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
                    var locationId = DeterministicGuid.Create(companySeed.Id, "location", locationSeed.Code);
                    if (locationIds.Contains(locationId)
                        || !locationKeys.Add((companySeed.Id, locationSeed.Code)))
                    {
                        continue;
                    }

                    dbContext.Locations.Add(new Location(
                        locationId,
                        companySeed.Id,
                        siteId,
                        locationSeed.Code,
                        locationSeed.Name,
                        isExternal));
                }
            }

            foreach (var workCenterSeed in seed.WorkCenters)
            {
                var workCenterId = DeterministicGuid.Create(companySeed.Id, "work-center", workCenterSeed.Code);
                if (workCenterIds.Contains(workCenterId)
                    || !workCenterKeys.Add((companySeed.Id, workCenterSeed.Code)))
                {
                    continue;
                }

                dbContext.WorkCenters.Add(new WorkCenter(
                    workCenterId,
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

        using var bootstrapWrite = bootstrapWriteScope.BeginBootstrapWrite();
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

        if (seed.Companies
            .GroupBy(x => x.Id)
            .Any(group => group.Count() > 1)
            || seed.Companies
                .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException(
                "Seed şirket kimliği ve kodu benzersiz olmalıdır.");
        }

        foreach (var company in seed.Companies)
        {
            _ = new Company(
                company.Id,
                company.Code,
                company.LegalName,
                company.Vkn,
                company.IsPlaceholder,
                company.InvoiceCapable);
        }

        if (!seed.Companies.Any(x => x.ApplyTopologyTemplates))
        {
            throw new InvalidOperationException(
                "En az bir seed şirketi organizasyon topolojisi uygulamalıdır.");
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
