using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Locations;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Domain.WorkCenters;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Organization.UnitTests;

public sealed class OrganizationLocationAuthorityRevalidationV1Tests
{
    private const string Actor = "synthetic-location-authority-actor";
    private static readonly Guid Company = Guid.Parse("81000000-0000-4000-8000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("81000000-0000-4000-8000-000000000002");
    private static readonly Guid SiteOne = Guid.Parse("82000000-0000-4000-8000-000000000001");
    private static readonly Guid SiteTwo = Guid.Parse("82000000-0000-4000-8000-000000000002");
    private static readonly Guid LocationOne = Guid.Parse("83000000-0000-4000-8000-000000000001");
    private static readonly Guid LocationTwo = Guid.Parse("83000000-0000-4000-8000-000000000002");
    private static readonly Guid WorkCenterOne = Guid.Parse("84000000-0000-4000-8000-000000000001");

    [Fact]
    public async Task Canonical_company_location_site_and_work_center_scopes_authorize_the_same_location()
    {
        var database = await SeedAsync();
        var scopes = new[]
        {
            Scope(companyWide: true),
            Scope(locationIds: new HashSet<Guid> { LocationOne }),
            Scope(siteIds: new HashSet<Guid> { SiteOne }),
            Scope(workCenterIds: new HashSet<Guid> { WorkCenterOne }),
        };

        foreach (var scope in scopes)
        {
            var authority = new RecordingAuthority(scope);
            await using var context = Context(database, Resolved(Company));
            var service = Service(context, authority);

            var outcome = await service.RevalidateAsync(Request(
                OrganizationLocationAuthorityAccessV1.Read,
                [LocationOne]));

            Assert.Equal(OrganizationLocationAuthorityOutcomeV1.Authorized, outcome);
            Assert.Equal((ErpPermissions.OrganizationLocationsRead, Company), Assert.Single(authority.Calls));
        }
    }

    [Fact]
    public async Task Every_requested_location_must_match_the_same_fresh_scope()
    {
        var database = await SeedAsync();
        var authority = new RecordingAuthority(Scope(
            locationIds: new HashSet<Guid> { LocationOne }));
        await using var context = Context(database, Resolved(Company));
        var service = Service(context, authority);

        var outcome = await service.RevalidateAsync(Request(
            OrganizationLocationAuthorityAccessV1.Read,
            [LocationOne, LocationTwo]));

        Assert.Equal(OrganizationLocationAuthorityOutcomeV1.Denied, outcome);
    }

    [Theory]
    [InlineData(OrganizationLocationAuthorityAccessV1.Read, ErpPermissions.OrganizationLocationsRead)]
    [InlineData(OrganizationLocationAuthorityAccessV1.Write, ErpPermissions.OrganizationLocationsWrite)]
    public async Task Closed_access_maps_only_to_the_existing_location_permissions(
        OrganizationLocationAuthorityAccessV1 access,
        string expectedPermission)
    {
        var database = await SeedAsync();
        var authority = new RecordingAuthority(Scope(companyWide: true));
        await using var context = Context(database, Resolved(Company));
        var service = Service(context, authority);

        var outcome = await service.RevalidateAsync(Request(access, [LocationOne]));

        Assert.Equal(OrganizationLocationAuthorityOutcomeV1.Authorized, outcome);
        Assert.Equal((expectedPermission, Company), Assert.Single(authority.Calls));
    }

    [Fact]
    public async Task Invalid_or_mismatched_input_denies_before_authority_and_row_access()
    {
        var company = Resolved(Company);
        var context = Context($"disposed-{Guid.NewGuid():N}", company);
        await context.DisposeAsync();
        var authority = new ThrowingAuthority();
        var service = new OrganizationLocationAuthorityRevalidationV1(
            context,
            company,
            new CurrentUser(Actor, Company),
            authority);
        var tooMany = Enumerable.Range(1, OrganizationLocationAuthorityContractV1.MaximumLocationCount + 1)
            .Select(_ => Guid.NewGuid())
            .ToArray();
        var invalid = new[]
        {
            Request(OrganizationLocationAuthorityAccessV1.Read, []),
            Request(OrganizationLocationAuthorityAccessV1.Read, [Guid.Empty]),
            Request(OrganizationLocationAuthorityAccessV1.Read, [LocationOne, LocationOne]),
            Request(OrganizationLocationAuthorityAccessV1.Read, tooMany),
            Request((OrganizationLocationAuthorityAccessV1)999, [LocationOne]),
            Request(OrganizationLocationAuthorityAccessV1.Read, [LocationOne]) with { CompanyId = OtherCompany },
            Request(OrganizationLocationAuthorityAccessV1.Read, [LocationOne]) with { ActorId = "other-actor" },
            Request(OrganizationLocationAuthorityAccessV1.Read, [LocationOne]) with { ActorId = $" {Actor}" },
        };

        foreach (var request in invalid)
        {
            Assert.Equal(
                OrganizationLocationAuthorityOutcomeV1.Denied,
                await service.RevalidateAsync(request));
        }
    }

    [Fact]
    public async Task Denied_fresh_authority_stops_before_the_location_query()
    {
        var company = Resolved(Company);
        var context = Context($"disposed-{Guid.NewGuid():N}", company);
        await context.DisposeAsync();
        var authority = new RecordingAuthority(PermissionScope.Denied);
        var service = new OrganizationLocationAuthorityRevalidationV1(
            context,
            company,
            new CurrentUser(Actor, Company),
            authority);

        var outcome = await service.RevalidateAsync(Request(
            OrganizationLocationAuthorityAccessV1.Write,
            [LocationOne]));

        Assert.Equal(OrganizationLocationAuthorityOutcomeV1.Denied, outcome);
        Assert.Single(authority.Calls);
    }

    [Fact]
    public async Task Cancellation_and_infrastructure_failures_propagate()
    {
        var database = await SeedAsync();
        await using var context = Context(database, Resolved(Company));
        var service = Service(context, new ThrowingAuthority());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RevalidateAsync(
            Request(OrganizationLocationAuthorityAccessV1.Read, [LocationOne]),
            cancellation.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RevalidateAsync(
            Request(OrganizationLocationAuthorityAccessV1.Read, [LocationOne])));
    }

    private static OrganizationLocationAuthorityRequestV1 Request(
        OrganizationLocationAuthorityAccessV1 access,
        IReadOnlyList<Guid> locationIds) =>
        new(Company, Actor, access, locationIds);

    private static OrganizationLocationAuthorityRevalidationV1 Service(
        OrganizationDbContext context,
        IErpAuthorityRevalidationService authority) =>
        new(context, Resolved(Company), new CurrentUser(Actor, Company), authority);

    private static PermissionScope Scope(
        bool companyWide = false,
        IReadOnlySet<Guid>? siteIds = null,
        IReadOnlySet<Guid>? locationIds = null,
        IReadOnlySet<Guid>? workCenterIds = null) =>
        new(
            true,
            companyWide,
            new HashSet<string>(StringComparer.Ordinal),
            siteIds ?? new HashSet<Guid>(),
            locationIds ?? new HashSet<Guid>(),
            workCenterIds ?? new HashSet<Guid>());

    private static async Task<string> SeedAsync()
    {
        var database = $"organization-location-authority-{Guid.NewGuid():N}";
        await using var context = Context(database, Resolved(Company));
        await context.Database.EnsureCreatedAsync();
        context.Sites.AddRange(
            new Site(SiteOne, Company, "SITE-1", "Site 1", SiteType.Factory),
            new Site(SiteTwo, Company, "SITE-2", "Site 2", SiteType.Factory));
        context.Locations.AddRange(
            new Location(LocationOne, Company, SiteOne, "LOC-1", "Location 1", false),
            new Location(LocationTwo, Company, SiteTwo, "LOC-2", "Location 2", false));
        context.WorkCenters.Add(new WorkCenter(
            WorkCenterOne,
            Company,
            LocationOne,
            "WC-1",
            "Work centre 1",
            WorkCenterKind.Slitting));
        await context.SaveChangesAsync();
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

    private sealed class CurrentUser(string actorId, Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actorId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid value) => AllowedCompanyIds.Contains(value);
    }

    private sealed class RecordingAuthority(PermissionScope answer) : IErpAuthorityRevalidationService
    {
        public List<(string Permission, Guid CompanyId)> Calls { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((permission, companyId));
            return Task.FromResult(answer);
        }
    }

    private sealed class ThrowingAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic authority infrastructure failure");
    }
}
