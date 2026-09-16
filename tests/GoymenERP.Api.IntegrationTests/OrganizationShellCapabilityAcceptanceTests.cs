using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

public sealed class OrganizationShellCapabilityAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    [Fact]
    public void NormalProviderPair_RequiresTheExactAcceptedAuthority()
    {
        var services = CanonicalServices();
        var authority = C01E2eShellCapabilityActivation.Configure(
            services, new TestingEnvironment(), new ConfigurationBuilder().Build());
        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15AndOrganizationV11, authority);
        using var container = services.BuildServiceProvider();
        var providers = container.GetServices<IShellCapabilityProvider>().ToArray();
        var registry = new ShellCapabilityRegistry(providers, new ShellCapabilityAuthorityCatalog([authority]));
        Assert.Equal(authority, registry.Revision);
        Assert.Equal(9, registry.Registrations.Count);
        Assert.Throws<InvalidOperationException>(() => new ShellCapabilityRegistry(
            providers, new ShellCapabilityAuthorityCatalog(
                [ShellCapabilityRegistryAuthorities.CounterpartyListCreateLinkAndDetailV15])));
    }

    [Fact]
    public void DisposableC01_RemovesOrganizationAndStillRejectsUnexpectedProviders()
    {
        var services = CanonicalServices();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(C01E2eShellCapabilityActivationTests.ExactEnvelope()).Build();
        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(), configuration);
        using var container = services.BuildServiceProvider();
        var provider = Assert.Single(container.GetServices<IShellCapabilityProvider>());
        var registry = new ShellCapabilityRegistry([provider], new ShellCapabilityAuthorityCatalog([authority]));
        Assert.Equal(ShellCapabilityRegistryAuthorities.C01DisposableListOnlyV1, registry.Revision);
        Assert.Equal(CounterpartyShellCapabilityProvider.ListCapabilityId,
            Assert.Single(registry.Registrations, item => item.Declaration.ImplementationAvailable).Declaration.CapabilityId);
        Assert.DoesNotContain(registry.Registrations, item => item.Owner == OrganizationShellCapabilityProvider.OwnerId);

        var duplicate = CanonicalServices();
        duplicate.AddSingleton<IShellCapabilityProvider, OrganizationShellCapabilityProvider>();
        Assert.Throws<InvalidOperationException>(() => C01E2eShellCapabilityActivation.Configure(
            duplicate, new TestingEnvironment(), configuration));
    }

    // Bit 1 locations.read, 2 sites.read, 4 locations.status.update, 8 work-centers.read.
    // The work-centers bit is enumerated ACROSS the whole matrix on purpose: the one
    // claim this slice makes is that the ORG-03 route neither needs nor is withheld by
    // any of the other three, and only every combination shows that.
    public static IEnumerable<object[]> PermissionCases()
    {
        for (var permissions = 0; permissions < 16; permissions++)
        for (var scope = 0; scope < 4; scope++)
            yield return [permissions, scope];
    }

    [Theory]
    [MemberData(nameof(PermissionCases))]
    public async Task ExistingPermissionAndNarrowScope_CombinationsRemainIndependent(int permissions, int scope)
    {
        var authority = new ControlledAuthority(permissions, scope);
        await using var host = ControlledHost(authority);
        using var client = Client(host);
        var snapshot = await Snapshot(client);
        var expected = new List<string>();
        if ((permissions & 1) != 0)
        {
            expected.Add(OrganizationShellCapabilityProvider.ListCapabilityId);
            expected.Add(OrganizationShellCapabilityProvider.DetailCapabilityId);
            if ((permissions & 2) != 0)
                expected.Add(OrganizationShellCapabilityProvider.SitesReadCapabilityId);
            if ((permissions & 4) != 0)
                expected.Add(OrganizationShellCapabilityProvider.StatusCapabilityId);
        }

        // Independent of the locations branch above, in BOTH directions.
        if ((permissions & 8) != 0)
            expected.Add(OrganizationShellCapabilityProvider.WorkCenterListCapabilityId);

        Assert.Equal(expected.Order(StringComparer.Ordinal), snapshot.Capabilities);
    }

    [Fact]
    public async Task LocationsOnly_KeepListAndDetailWhileSitesAndStatusStillReturn403()
    {
        await using var host = ControlledHost(new ControlledAuthority(1, 0));
        using var client = Client(host);
        var snapshot = await Snapshot(client);
        Assert.DoesNotContain(OrganizationShellCapabilityProvider.SitesReadCapabilityId, snapshot.Capabilities);
        Assert.DoesNotContain(OrganizationShellCapabilityProvider.StatusCapabilityId, snapshot.Capabilities);
        using var sites = await client.GetAsync("/api/v1/organization/sites");
        Assert.Equal(HttpStatusCode.Forbidden, sites.StatusCode);
        using var list = await client.GetAsync("/api/v1/organization/locations?page=1&pageSize=25");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var page = await list.Content.ReadFromJsonAsync<JsonElement>();
        var first = page.GetProperty("items")[0];
        var id = first.GetProperty("id").GetGuid();
        using var detail = await client.GetAsync($"/api/v1/organization/locations/{id:D}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var command = await client.PutAsJsonAsync($"/api/v1/organization/locations/{id:D}/active-state",
            new { isActive = false, expectedVersion = first.GetProperty("version").GetInt64(), reason = "Controlled denied command" });
        Assert.Equal(HttpStatusCode.Forbidden, command.StatusCode);
        Assert.DoesNotContain(TestCompanyIds.CompanyB.ToString("D"), await command.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The acceptance this slice exists for: a user holding ONLY
    /// `organization.work-centers.read` gets the ORG-03 route, and the absence of
    /// `organization.locations.read` does not close it. The location reads stay 403 —
    /// seeing a work center is not seeing its location — and the refusal discloses
    /// nothing about the other company.
    /// </summary>
    [Fact]
    public async Task WorkCentersOnly_PublishesTheListRouteWhileEveryLocationReadStays403()
    {
        await using var host = ControlledHost(new ControlledAuthority(8, 0));
        using var client = Client(host);
        var snapshot = await Snapshot(client);
        Assert.Equal(
            OrganizationShellCapabilityProvider.WorkCenterListCapabilityId,
            Assert.Single(snapshot.Capabilities));

        using var workCenters = await client.GetAsync("/api/v1/organization/work-centers");
        Assert.Equal(HttpStatusCode.OK, workCenters.StatusCode);
        var rows = await workCenters.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, rows.ValueKind);
        // The seeded company A topology carries five; the point is that this caller
        // reads them with no locations grant at all.
        Assert.Equal(5, rows.GetArrayLength());
        var locationId = rows[0].GetProperty("locationId").GetGuid();

        using var list = await client.GetAsync("/api/v1/organization/locations?page=1&pageSize=25");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        // The id the work-center row exposes is still not a key to the location card.
        using var detail = await client.GetAsync($"/api/v1/organization/locations/{locationId:D}");
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
        Assert.DoesNotContain(
            TestCompanyIds.CompanyB.ToString("D"),
            await detail.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The mirror image: locations without work centers. ORG-01/ORG-02 keep working and
    /// the work-center read is refused, so neither list is a back door into the other.
    /// </summary>
    [Fact]
    public async Task LocationsOnly_DoesNotPublishTheWorkCenterRouteAndItsReadStays403()
    {
        await using var host = ControlledHost(new ControlledAuthority(1, 0));
        using var client = Client(host);
        var snapshot = await Snapshot(client);
        Assert.DoesNotContain(
            OrganizationShellCapabilityProvider.WorkCenterListCapabilityId,
            snapshot.Capabilities);
        using var workCenters = await client.GetAsync("/api/v1/organization/work-centers");
        Assert.Equal(HttpStatusCode.Forbidden, workCenters.StatusCode);
    }

    [Fact]
    public async Task FreshSnapshot_DropsRevokedControlsAndGroupContextNeverPublishesOrganization()
    {
        var authority = new ControlledAuthority(7, 0);
        await using var host = ControlledHost(authority);
        using var client = Client(host);
        var before = await Snapshot(client);
        Assert.Equal(4, before.Capabilities.Count);
        authority.Permissions = 1;
        var after = await Snapshot(client);
        Assert.NotEqual(before.Generation, after.Generation);
        Assert.Equal(2, after.Capabilities.Count);
        Assert.DoesNotContain(OrganizationShellCapabilityProvider.StatusCapabilityId, after.Capabilities);
        Assert.DoesNotContain(OrganizationShellCapabilityProvider.SitesReadCapabilityId, after.Capabilities);
        client.DefaultRequestHeaders.Remove("X-Company-Id");
        Assert.Empty((await Snapshot(client)).Capabilities);
    }

    /// <summary>
    /// The SCOPE half of "yalnız yetkili şirket/scope kayıtları", measured rather than
    /// assumed.
    ///
    /// A caller whose `organization.work-centers.read` is granted on a scope that matches
    /// NO record still gets the ROUTE — the capability stands on the permission, and
    /// closing the screen would tell the caller something about rows they cannot read —
    /// and gets an EMPTY list rather than the company's rows.
    ///
    /// Why this case is needed at all: the seeded company A topology puts all five work
    /// centers under the single SITE-OSM site, so the site-scoped seeded reader sees
    /// exactly what a company-wide reader sees. Deleting the scope filter in
    /// <c>OrganizationService.GetWorkCentersAsync</c> therefore left every existing test
    /// green. This case and the positive theory below fail when it is deleted.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task WorkCentersGrantedOnAnEmptyScope_PublishesTheRouteAndListsNothing(int scopeKind)
    {
        await using var host = ControlledHost(new ControlledAuthority(8, scopeKind));
        using var client = Client(host);
        Assert.Equal(
            OrganizationShellCapabilityProvider.WorkCenterListCapabilityId,
            Assert.Single((await Snapshot(client)).Capabilities));
        Assert.Empty(await ReadCodesAsync(client));
    }

    /// <summary>
    /// Each branch of the scope filter, against REAL seeded ids: a site scope answers with
    /// that site's work centers, a location scope with that location's, and a work-center
    /// scope with exactly itself.
    ///
    /// The expected sets come from the seed, not from the endpoint:
    /// <c>Seed/organization-seed.json</c> places WC-SLIT and WC-CR on OSM-H3, WC-PAINT on
    /// OSM-H0, WC-TUBE on OSM-H1 and WC-CUT on OSM-H6, with all of them under SITE-OSM.
    /// </summary>
    [Theory]
    [InlineData(1, "SITE-OSM", "WC-CR,WC-CUT,WC-PAINT,WC-SLIT,WC-TUBE")]
    [InlineData(2, "OSM-H3", "WC-CR,WC-SLIT")]
    [InlineData(2, "OSM-H0", "WC-PAINT")]
    [InlineData(3, "WC-TUBE", "WC-TUBE")]
    public async Task WorkCentersGrantedOnOneScope_ListExactlyThatScopesRecords(
        int scopeKind, string code, string expected)
    {
        var authority = new ControlledAuthority(8, scopeKind);
        await using var host = ControlledHost(authority);
        authority.ScopeId = await ResolveScopeIdAsync(host, scopeKind, code);
        using var client = Client(host);
        Assert.Equal(expected.Split(','), await ReadCodesAsync(client));
    }

    /// <summary>
    /// ORG-03 row eligibility (#66 5700319744) with the two permissions on DIFFERENT
    /// scopes. `work-centers.read` always covers OSM-H3 + OSM-H0 (L-A + L-B), so the rows
    /// are WC-CR, WC-PAINT and WC-SLIT in every case; only `locations.read` moves.
    ///
    /// Each row's <c>canOpenLocationDetail</c> is then held against the card read itself:
    /// true must mean the card answers 200, false must mean it does not (404 inside a
    /// granted permission, 403 without one). The site and work-center cases pin the other
    /// two branches of the location scope, so a flag computed from location ids alone, or
    /// from the work-center scope, fails here. The seed places WC-CR and WC-SLIT on OSM-H3
    /// and WC-PAINT on OSM-H0, all under SITE-OSM.
    /// </summary>
    [Theory]
    [InlineData("none", "WC-CR:false,WC-PAINT:false,WC-SLIT:false")]
    [InlineData("location:OSM-H3", "WC-CR:true,WC-PAINT:false,WC-SLIT:true")]
    [InlineData("location:OSM-H0", "WC-CR:false,WC-PAINT:true,WC-SLIT:false")]
    [InlineData("location:OSM-H3+OSM-H0", "WC-CR:true,WC-PAINT:true,WC-SLIT:true")]
    [InlineData("site:SITE-OSM", "WC-CR:true,WC-PAINT:true,WC-SLIT:true")]
    [InlineData("work-center:WC-PAINT", "WC-CR:false,WC-PAINT:true,WC-SLIT:false")]
    [InlineData("company", "WC-CR:true,WC-PAINT:true,WC-SLIT:true")]
    [InlineData("location:unknown", "WC-CR:false,WC-PAINT:false,WC-SLIT:false")]
    public async Task WorkCenterRows_OfferTheLocationCardExactlyWhereTheCardAnswers(
        string locationsScope, string expected)
    {
        var authority = new ControlledAuthority(8, 0);
        await using var host = ControlledHost(authority);
        authority.ScopeOverrides[ErpPermissions.OrganizationWorkCentersRead] = Scope(
            locations: [
                await ResolveScopeIdAsync(host, 2, "OSM-H3"),
                await ResolveScopeIdAsync(host, 2, "OSM-H0")]);
        var locationsGranted = locationsScope != "none";
        if (locationsGranted)
        {
            var parts = locationsScope.Split(':');
            var codes = parts.Length > 1 ? parts[1].Split('+') : [];
            async Task<Guid[]> Ids(int scopeKind) => codes[0] == "unknown"
                ? [authority.ScopeId]
                : await Task.WhenAll(codes.Select(code => ResolveScopeIdAsync(host, scopeKind, code)));
            authority.Permissions |= 1;
            authority.ScopeOverrides[ErpPermissions.OrganizationLocationsRead] = parts[0] switch
            {
                "company" => Scope(companyWide: true),
                "site" => Scope(sites: await Ids(1)),
                "location" => Scope(locations: await Ids(2)),
                _ => Scope(workCenters: await Ids(3)),
            };
        }

        using var client = Client(host);
        using var response = await client.GetAsync("/api/v1/organization/work-centers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await response.Content.ReadFromJsonAsync<JsonElement>();
        var actual = new List<string>();
        foreach (var row in rows.EnumerateArray())
        {
            // The signal is one boolean beside the six row fields — no scope list, no
            // permission list, nothing about any record the caller does not already see.
            Assert.Equal(
                ["id", "locationId", "code", "name", "kind", "isActive", "canOpenLocationDetail"],
                row.EnumerateObject().Select(property => property.Name).ToArray());
            var flag = row.GetProperty("canOpenLocationDetail");
            Assert.True(flag.ValueKind is JsonValueKind.True or JsonValueKind.False);
            actual.Add($"{row.GetProperty("code").GetString()}:{(flag.GetBoolean() ? "true" : "false")}");

            using var card = await client.GetAsync(
                $"/api/v1/organization/locations/{row.GetProperty("locationId").GetGuid():D}");
            Assert.Equal(
                flag.GetBoolean() ? HttpStatusCode.OK
                    : locationsGranted ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden,
                card.StatusCode);
        }

        Assert.Equal(expected, string.Join(',', actual));
    }

    private static PermissionScope Scope(
        bool companyWide = false,
        Guid[]? sites = null,
        Guid[]? locations = null,
        Guid[]? workCenters = null) =>
        new(true, companyWide, new HashSet<string>(),
            new HashSet<Guid>(sites ?? []),
            new HashSet<Guid>(locations ?? []),
            new HashSet<Guid>(workCenters ?? []));

    /// <summary>Work-center codes as the endpoint returns them, in server order.</summary>
    private static async Task<string[]> ReadCodesAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/organization/work-centers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, rows.ValueKind);
        return [.. rows.EnumerateArray().Select(row => row.GetProperty("code").GetString()!)];
    }

    /// <summary>
    /// The seeded id behind a code, read from the database the host itself serves — so the
    /// scope points at a record that exists rather than at a value this test invented.
    /// </summary>
    private static async Task<Guid> ResolveScopeIdAsync(
        WebApplicationFactory<Program> host, int scopeKind, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
        var database = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        return scopeKind switch
        {
            1 => (await database.Sites.AsNoTracking()
                .SingleAsync(site => site.Code == code)).Id,
            2 => (await database.Locations.AsNoTracking()
                .SingleAsync(location => location.Code == code)).Id,
            _ => (await database.WorkCenters.AsNoTracking()
                .SingleAsync(workCenter => workCenter.Code == code)).Id,
        };
    }

    private WebApplicationFactory<Program> ControlledHost(ControlledAuthority authority) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IErpAuthorizationService>();
            services.AddSingleton<IErpAuthorizationService>(authority);
        }));

    private static HttpClient Client(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private static async Task<ShellCapabilitySnapshot> Snapshot(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/shell/capabilities");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, json.EnumerateObject().Count());
        return new ShellCapabilitySnapshot(json.GetProperty("contractVersion").GetString()!,
            json.GetProperty("generation").GetString()!,
            json.GetProperty("capabilities").EnumerateArray().Select(value => value.GetString()!).ToArray());
    }

    private static ServiceCollection CanonicalServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IShellCapabilityProvider, CounterpartyShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, OrganizationShellCapabilityProvider>();
        return services;
    }

    // Controlled authorization and EF InMemory HTTP tests, not real SQL or IdP revocation evidence.
    private sealed class ControlledAuthority(int permissions, int scopeKind) : IErpAuthorizationService
    {
        public int Permissions { get; set; } = permissions;

        /// <summary>
        /// The id the narrow scope carries. It defaults to one that matches NO seeded
        /// record — the out-of-scope case — and a case that wants the opposite points it
        /// at a real site/location/work-center id read from the database.
        /// </summary>
        public Guid ScopeId { get; set; } = Guid.Parse("76000000-0000-0000-0000-000000000001");

        /// <summary>
        /// A per-permission scope that replaces the shared one, so a case can put two
        /// permissions on DIFFERENT scopes. The permission bit still decides the grant.
        /// </summary>
        public Dictionary<string, PermissionScope> ScopeOverrides { get; } = new(StringComparer.Ordinal);

        private PermissionScope Scope(string permission, Guid companyId)
        {
            var bit = permission switch
            {
                ErpPermissions.OrganizationLocationsRead => 1,
                ErpPermissions.OrganizationSitesRead => 2,
                ErpPermissions.OrganizationLocationsStatusUpdate => 4,
                ErpPermissions.OrganizationWorkCentersRead => 8,
                _ => 0,
            };
            if (companyId != TestCompanyIds.CompanyA || bit == 0 || (Permissions & bit) == 0)
                return PermissionScope.Denied;
            if (ScopeOverrides.TryGetValue(permission, out var overridden))
                return overridden;
            return new PermissionScope(true, scopeKind == 0, new HashSet<string>(),
                scopeKind == 1 ? new HashSet<Guid> { ScopeId } : new HashSet<Guid>(),
                scopeKind == 2 ? new HashSet<Guid> { ScopeId } : new HashSet<Guid>(),
                scopeKind == 3 ? new HashSet<Guid> { ScopeId } : new HashSet<Guid>());
        }

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId,
            IReadOnlyCollection<string> requested, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthorizationSnapshot($"controlled-org-{Permissions}-{scopeKind}",
                companyId == TestCompanyIds.CompanyA ? new HashSet<Guid> { companyId } : new HashSet<Guid>(),
                requested.ToDictionary(permission => permission, permission => Scope(permission, companyId))));

        public Task<bool> HasPermissionAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(Scope(permission, companyId).IsGranted);

        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(Scope(permission, companyId));
    }

    private sealed class TestingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
