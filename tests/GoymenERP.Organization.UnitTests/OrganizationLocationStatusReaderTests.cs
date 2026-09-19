using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Locations;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Organization.UnitTests;

/// <summary>
/// The module read for other modules' commands: the location's own active flag and external flag,
/// company-scoped by the resolved context and nothing else. The rows here are synthetic.
/// </summary>
public sealed class OrganizationLocationStatusReaderTests
{
    private static readonly Guid CompanyA = Guid.Parse("0f000000-0000-4000-8000-00000000000a");
    private static readonly Guid CompanyB = Guid.Parse("0f000000-0000-4000-8000-00000000000b");
    private static readonly Guid OwnLocation = Guid.Parse("0f100000-0000-4000-8000-000000000001");
    private static readonly Guid ExternalLocation = Guid.Parse("0f100000-0000-4000-8000-000000000002");
    private static readonly Guid InactiveLocation = Guid.Parse("0f100000-0000-4000-8000-000000000003");
    private static readonly Guid LocationOfB = Guid.Parse("0f100000-0000-4000-8000-000000000004");

    [Fact]
    public async Task Reads_the_locations_own_active_and_external_flags()
    {
        var database = await SeedAsync();
        var reader = Reader(database, Resolved(CompanyA));

        Assert.Equal(new OrganizationLocationStatusV1(OwnLocation, true, false), await reader.ReadAsync(CompanyA, OwnLocation));
        Assert.Equal(new OrganizationLocationStatusV1(ExternalLocation, true, true), await reader.ReadAsync(CompanyA, ExternalLocation));
        Assert.Equal(new OrganizationLocationStatusV1(InactiveLocation, false, false), await reader.ReadAsync(CompanyA, InactiveLocation));
    }

    [Fact]
    public async Task Another_companys_location_an_unknown_id_and_the_empty_id_are_not_found()
    {
        var database = await SeedAsync();

        Assert.Null(await Reader(database, Resolved(CompanyA)).ReadAsync(CompanyA, LocationOfB));
        Assert.Null(await Reader(database, Resolved(CompanyB)).ReadAsync(CompanyB, OwnLocation));
        Assert.Null(await Reader(database, Resolved(CompanyA)).ReadAsync(CompanyA, Guid.NewGuid()));
        Assert.Null(await Reader(database, Resolved(CompanyA)).ReadAsync(CompanyA, Guid.Empty));
        Assert.Equal(new OrganizationLocationStatusV1(LocationOfB, true, false),
            await Reader(database, Resolved(CompanyB)).ReadAsync(CompanyB, LocationOfB));
    }

    [Fact]
    public async Task A_company_that_is_not_the_resolved_context_is_refused_not_answered()
    {
        var database = await SeedAsync();

        // The context's company decides; a different company argument cannot widen or switch it.
        await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(
            () => Reader(database, Resolved(CompanyB)).ReadAsync(CompanyA, OwnLocation));
        await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(
            () => Reader(database, new CompanyContext()).ReadAsync(CompanyA, OwnLocation));
        await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(
            () => Reader(database, Resolved(CompanyA)).ReadAsync(Guid.Empty, OwnLocation));
    }

    [Fact]
    public void The_read_depends_on_no_user_or_permission_service()
    {
        // A module read: the calling module authorized its own command; no Organization user grant decides it.
        var parameters = typeof(OrganizationLocationStatusReader).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType);
        Assert.Equal([typeof(OrganizationDbContext), typeof(ICompanyContext)], parameters);
    }

    private static OrganizationLocationStatusReader Reader(string database, ICompanyContext context) =>
        new(Context(database, context), context);

    private static async Task<string> SeedAsync()
    {
        var database = $"organization-location-status-{Guid.NewGuid():N}";
        await using (var a = Context(database, Resolved(CompanyA)))
        {
            var site = Guid.Parse("0f200000-0000-4000-8000-00000000000a");
            var externalSite = Guid.Parse("0f200000-0000-4000-8000-0000000000ea");
            a.Sites.Add(new Site(site, CompanyA, "SYN-SITE", "Sentetik saha", SiteType.Factory));
            a.Sites.Add(new Site(externalSite, CompanyA, "SYN-EXT", "Sentetik dış depo", SiteType.ExternalWarehouse));
            a.Locations.Add(new Location(OwnLocation, CompanyA, site, "SYN-OWN", "Sentetik hol", isExternal: false));
            a.Locations.Add(new Location(ExternalLocation, CompanyA, externalSite, "SYN-EXT-1", "Sentetik dış depo 1", isExternal: true));
            var inactive = new Location(InactiveLocation, CompanyA, site, "SYN-OFF", "Sentetik pasif hol", isExternal: false);
            inactive.SetActive(false);
            a.Locations.Add(inactive);
            await a.SaveChangesAsync();
        }

        await using (var b = Context(database, Resolved(CompanyB)))
        {
            var site = Guid.Parse("0f200000-0000-4000-8000-00000000000b");
            b.Sites.Add(new Site(site, CompanyB, "SYN-SITE", "Sentetik saha B", SiteType.Warehouse));
            b.Locations.Add(new Location(LocationOfB, CompanyB, site, "SYN-OWN", "Sentetik hol B", isExternal: false));
            await b.SaveChangesAsync();
        }

        return database;
    }

    private static CompanyContext Resolved(Guid companyId)
    {
        var context = new CompanyContext();
        context.Set(companyId);
        return context;
    }

    private static OrganizationDbContext Context(string database, ICompanyContext companyContext) => new(
        new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase(database).Options,
        companyContext,
        new OrganizationBootstrapWriteScope());
}
