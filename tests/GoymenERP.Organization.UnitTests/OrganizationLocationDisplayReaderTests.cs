using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Locations;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Organization.UnitTests;

public sealed class OrganizationLocationDisplayReaderTests
{
    private static readonly Guid CompanyA = Guid.Parse("da000000-0000-4000-8000-00000000000a");
    private static readonly Guid CompanyB = Guid.Parse("db000000-0000-4000-8000-00000000000b");
    private static readonly Guid Active = Guid.Parse("da100000-0000-4000-8000-000000000001");
    private static readonly Guid External = Guid.Parse("da100000-0000-4000-8000-000000000002");
    private static readonly Guid Inactive = Guid.Parse("da100000-0000-4000-8000-000000000003");
    private static readonly Guid Foreign = Guid.Parse("db100000-0000-4000-8000-000000000001");

    [Fact]
    public async Task Resolves_active_inactive_and_external_locations_in_deterministic_order_without_disclosing_missing_or_foreign_rows()
    {
        var database = await SeedAsync();

        var answer = await Reader(database, Resolved(CompanyA)).ReadAsync(CompanyA,
            [Inactive, Guid.NewGuid(), Foreign, Active, External]);

        Assert.Equal(
        [
            new OrganizationLocationDisplayV1(Active, "A-HALL", "Aktif hol"),
            new OrganizationLocationDisplayV1(External, "B-EXT", "Harici depo"),
            new OrganizationLocationDisplayV1(Inactive, "C-OFF", "Pasif hol"),
        ], answer);
    }

    [Fact]
    public async Task Refuses_a_company_outside_the_live_context_before_validating_the_batch()
    {
        var database = await SeedAsync();

        await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(() =>
            Reader(database, Resolved(CompanyB)).ReadAsync(CompanyA, []));
        await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(() =>
            Reader(database, new CompanyContext()).ReadAsync(CompanyA, []));
        await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(() =>
            Reader(database, Resolved(CompanyA)).ReadAsync(Guid.Empty, []));
    }

    [Fact]
    public async Task Refuses_empty_duplicate_zero_and_over_bound_batches()
    {
        var database = await SeedAsync();
        var reader = Reader(database, Resolved(CompanyA));
        var tooMany = Enumerable.Range(0, 101)
            .Select(index => Guid.Parse($"da200000-0000-4000-8000-{index:D12}"))
            .ToArray();

        foreach (var invalid in new IReadOnlyCollection<Guid>[]
                 {
                     Array.Empty<Guid>(),
                     [Active, Active],
                     [Guid.Empty],
                     tooMany,
                 })
        {
            var error = await Assert.ThrowsAsync<OrganizationLocationDisplayQueryException>(() =>
                reader.ReadAsync(CompanyA, invalid));
            Assert.Equal(OrganizationLocationDisplayQueryException.ErrorCode, error.Code);
        }
    }

    [Fact]
    public async Task A_context_that_unresolves_during_the_query_releases_no_location_names()
    {
        var database = await SeedAsync();
        var drifting = new UnresolvingCompanyContext(CompanyA);

        await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(() =>
            Reader(database, drifting).ReadAsync(CompanyA, [Active]));

        Assert.Equal(2, drifting.ResolutionChecks);
    }

    [Fact]
    public void The_module_read_depends_on_no_user_or_permission_service()
    {
        var parameters = typeof(OrganizationLocationDisplayReader).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType);
        Assert.Equal([typeof(OrganizationDbContext), typeof(ICompanyContext)], parameters);
    }

    private static OrganizationLocationDisplayReader Reader(string database, ICompanyContext company) =>
        new(Context(database, company), company);

    private static async Task<string> SeedAsync()
    {
        var database = $"organization-location-display-{Guid.NewGuid():N}";
        await using (var context = Context(database, Resolved(CompanyA)))
        {
            var internalSite = Guid.Parse("da300000-0000-4000-8000-000000000001");
            var externalSite = Guid.Parse("da300000-0000-4000-8000-000000000002");
            context.Sites.AddRange(
                new Site(internalSite, CompanyA, "SYN-INT", "Sentetik tesis", SiteType.Factory),
                new Site(externalSite, CompanyA, "SYN-EXT", "Sentetik dış depo", SiteType.ExternalWarehouse));
            context.Locations.AddRange(
                new Location(Active, CompanyA, internalSite, "A-HALL", "Aktif hol", isExternal: false),
                new Location(External, CompanyA, externalSite, "B-EXT", "Harici depo", isExternal: true));
            var inactive = new Location(Inactive, CompanyA, internalSite, "C-OFF", "Pasif hol", isExternal: false);
            inactive.SetActive(false);
            context.Locations.Add(inactive);
            await context.SaveChangesAsync();
        }

        await using (var context = Context(database, Resolved(CompanyB)))
        {
            var site = Guid.Parse("db300000-0000-4000-8000-000000000001");
            context.Sites.Add(new Site(site, CompanyB, "SYN-B", "Başka şirket tesisi", SiteType.Warehouse));
            context.Locations.Add(new Location(Foreign, CompanyB, site, "FOREIGN", "Başka şirket deposu", false));
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

    private sealed class UnresolvingCompanyContext(Guid companyId) : ICompanyContext
    {
        public int ResolutionChecks { get; private set; }

        public bool IsResolved => ++ResolutionChecks == 1;

        public Guid CompanyId => companyId;

        public void Set(Guid value) => throw new NotSupportedException();
    }
}
