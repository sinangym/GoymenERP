using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Locations;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Organization.UnitTests;

public sealed class OrganizationLocationCandidateReaderTests
{
    private static readonly Guid CompanyA = Guid.Parse("a1000000-0000-4000-8000-000000000001");
    private static readonly Guid CompanyB = Guid.Parse("b1000000-0000-4000-8000-000000000001");
    private static readonly Guid ActiveSite = Guid.Parse("a2000000-0000-4000-8000-000000000001");
    private static readonly Guid InactiveSite = Guid.Parse("a2000000-0000-4000-8000-000000000002");

    [Fact]
    public async Task Returns_only_active_nonexternal_locations_with_real_site_facts()
    {
        var database = await SeedAsync();
        var reader = Reader(database, Resolved(CompanyA));

        var answer = await reader.SearchAsync(CompanyA, new("tesis", 10));

        Assert.Equal(
        [
            new OrganizationLocationCandidateV1(
                Guid.Parse("a3000000-0000-4000-8000-000000000001"), "A-HALL", "Aktif hol",
                ActiveSite, "A-SITE", "Aktif tesis"),
            // No accepted cascade makes a location unusable because its site is inactive.
            new OrganizationLocationCandidateV1(
                Guid.Parse("a3000000-0000-4000-8000-000000000002"), "B-HALL", "İkinci hol",
                InactiveSite, "B-SITE", "Pasif tesis"),
        ], answer.Items);
        Assert.False(answer.SearchWindowFull);
    }

    [Fact]
    public async Task Bounds_the_window_and_never_returns_another_company()
    {
        var database = await SeedAsync();

        var bounded = await Reader(database, Resolved(CompanyA)).SearchAsync(CompanyA, new(null, 1));
        Assert.Single(bounded.Items);
        Assert.Equal("A-HALL", bounded.Items[0].LocationCode);
        Assert.True(bounded.SearchWindowFull);

        var companyB = await Reader(database, Resolved(CompanyB)).SearchAsync(CompanyB, new());
        Assert.Equal("A-HALL", Assert.Single(companyB.Items).LocationCode);
        Assert.NotEqual(bounded.Items[0].LocationId, companyB.Items[0].LocationId);
    }

    [Fact]
    public async Task Refuses_context_drift_before_query_validation()
    {
        var database = await SeedAsync();
        var invalid = new OrganizationLocationCandidateQueryV1(null, 0);

        await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(
            () => Reader(database, Resolved(CompanyB)).SearchAsync(CompanyA, invalid));
        await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(
            () => Reader(database, new CompanyContext()).SearchAsync(CompanyA, invalid));

        var error = await Assert.ThrowsAsync<OrganizationLocationCandidateQueryException>(
            () => Reader(database, Resolved(CompanyA)).SearchAsync(CompanyA, invalid));
        Assert.Equal(OrganizationLocationCandidateQueryException.ErrorCode, error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Invalid_bounds_read_no_candidates(int limit)
    {
        var database = await SeedAsync();
        await Assert.ThrowsAsync<OrganizationLocationCandidateQueryException>(
            () => Reader(database, Resolved(CompanyA)).SearchAsync(CompanyA, new(null, limit)));
    }

    [Fact]
    public void The_module_read_depends_on_no_user_or_permission_service()
    {
        var parameters = typeof(OrganizationLocationCandidateReader).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType);
        Assert.Equal([typeof(OrganizationDbContext), typeof(ICompanyContext)], parameters);
    }

    private static OrganizationLocationCandidateReader Reader(string database, ICompanyContext company) =>
        new(Context(database, company), company);

    private static async Task<string> SeedAsync()
    {
        var database = $"organization-location-candidates-{Guid.NewGuid():N}";
        await using (var context = Context(database, Resolved(CompanyA)))
        {
            var activeSite = new Site(ActiveSite, CompanyA, "A-SITE", "Aktif tesis", SiteType.Factory);
            var inactiveSite = new Site(InactiveSite, CompanyA, "B-SITE", "Pasif tesis", SiteType.Warehouse);
            inactiveSite.SetActive(false);
            context.Sites.AddRange(activeSite, inactiveSite);
            context.Locations.AddRange(
                new Location(Guid.Parse("a3000000-0000-4000-8000-000000000001"), CompanyA, ActiveSite,
                    "A-HALL", "Aktif hol", isExternal: false),
                new Location(Guid.Parse("a3000000-0000-4000-8000-000000000002"), CompanyA, InactiveSite,
                    "B-HALL", "İkinci hol", isExternal: false),
                new Location(Guid.Parse("a3000000-0000-4000-8000-000000000003"), CompanyA, ActiveSite,
                    "C-EXT", "Dış depo", isExternal: true));
            var inactive = new Location(Guid.Parse("a3000000-0000-4000-8000-000000000004"), CompanyA, ActiveSite,
                "D-OFF", "Pasif hol", isExternal: false);
            inactive.SetActive(false);
            context.Locations.Add(inactive);
            await context.SaveChangesAsync();
        }

        await using (var context = Context(database, Resolved(CompanyB)))
        {
            var site = Guid.Parse("b2000000-0000-4000-8000-000000000001");
            context.Sites.Add(new Site(site, CompanyB, "A-SITE", "Başka şirket tesisi", SiteType.Factory));
            context.Locations.Add(new Location(Guid.Parse("b3000000-0000-4000-8000-000000000001"), CompanyB,
                site, "A-HALL", "Başka şirket holü", isExternal: false));
            await context.SaveChangesAsync();
        }

        return database;
    }

    private static CompanyContext Resolved(Guid companyId)
    {
        var context = new CompanyContext();
        context.Set(companyId);
        return context;
    }

    private static OrganizationDbContext Context(string database, ICompanyContext company) => new(
        new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase(database).Options,
        company,
        new OrganizationBootstrapWriteScope());
}
