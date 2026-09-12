using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using Microsoft.AspNetCore.Mvc.Testing;
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
        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15AndOrganizationV10, authority);
        using var container = services.BuildServiceProvider();
        var providers = container.GetServices<IShellCapabilityProvider>().ToArray();
        var registry = new ShellCapabilityRegistry(providers, new ShellCapabilityAuthorityCatalog([authority]));
        Assert.Equal(authority, registry.Revision);
        Assert.Equal(8, registry.Registrations.Count);
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

    public static IEnumerable<object[]> PermissionCases()
    {
        for (var permissions = 0; permissions < 8; permissions++)
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
        private static readonly Guid ScopeId = Guid.Parse("76000000-0000-0000-0000-000000000001");

        private PermissionScope Scope(string permission, Guid companyId)
        {
            var bit = permission switch
            {
                ErpPermissions.OrganizationLocationsRead => 1,
                ErpPermissions.OrganizationSitesRead => 2,
                ErpPermissions.OrganizationLocationsStatusUpdate => 4,
                _ => 0,
            };
            if (companyId != TestCompanyIds.CompanyA || bit == 0 || (Permissions & bit) == 0)
                return PermissionScope.Denied;
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
