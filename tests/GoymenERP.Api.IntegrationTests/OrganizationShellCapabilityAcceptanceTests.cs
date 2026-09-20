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
        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15AndOrganizationV11, authority);
        using var container = services.BuildServiceProvider();
        var providers = container.GetServices<IShellCapabilityProvider>().ToArray();
        var registry = new ShellCapabilityRegistry(providers, new ShellCapabilityAuthorityCatalog([authority]));
        Assert.Equal(authority, registry.Revision);
        // Counterparty's four plus Organization's FIVE (ORG-03 joined the four ORG-01/02
        // declarations). Measured, not incremented: the count is asserted here so that a
        // declaration appearing or vanishing has to be stated rather than absorbed.
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

    /// <summary>
    /// One bit per Organization permission, in this order. `ControlledAuthority` reads a permission's
    /// bit from its POSITION here, and <see cref="PermissionCases"/> derives its bound from the LENGTH,
    /// so the theory's case count follows this list instead of a hand-written number.
    /// </summary>
    private static readonly string[] PermissionBits =
    [
        ErpPermissions.OrganizationLocationsRead,
        ErpPermissions.OrganizationSitesRead,
        ErpPermissions.OrganizationLocationsStatusUpdate,
        ErpPermissions.OrganizationWorkCentersRead,
    ];

    /// <summary>
    /// The mask of one permission for the AUTHORITY path, where an unknown name must answer 0 and be
    /// denied — the host asks this same authority about other modules' permissions. Only
    /// <see cref="ControlledAuthority.Scope"/> may use this; the authoring side uses <see cref="Bit"/>.
    /// </summary>
    private static int AuthorityBit(string permission)
    {
        var index = Array.IndexOf(PermissionBits, permission);
        return index < 0 ? 0 : 1 << index;
    }

    /// <summary>
    /// The mask of one permission for the TEST-AUTHORING side — the granted sets and the expectation
    /// arms — where an unknown name is a mistake in the test and must say so.
    ///
    /// <para>
    /// FİNANS's P3 on the first version of this head, and it was this head's own lesson turned on it:
    /// the permissive lookup was correct for the authority and I reused it here, so the silent zero was
    /// not removed but MOVED UP ONE LEVEL. `Bits(LocationsRead, SomethingElse)` would have contributed
    /// only the first and left the case green. No call site was wrong — every one used a pinned name —
    /// which is exactly the state the `_ => 0` arm was in before ORG-03 added a permission to it.
    /// Repairing an example is not closing a class, so the two uses now have two functions.
    /// </para>
    /// </summary>
    private static int Bit(string permission)
    {
        var index = Array.IndexOf(PermissionBits, permission);
        Assert.True(index >= 0,
            $"'{permission}' is not one of this fixture's permission bits, so it can grant nothing and "
            + "assert nothing here. Add it to PermissionBits (and the provider), or name one of: "
            + string.Join(", ", PermissionBits));
        return 1 << index;
    }

    private static int Bits(params string[] permissions) => permissions.Aggregate(0, (mask, p) => mask | Bit(p));

    /// <summary>
    /// The bit list is EXACTLY the permissions the Organization provider declares — the guard that makes
    /// the fixture's "unknown permission" arm unreachable for this module.
    ///
    /// <para>
    /// WHY THIS EXISTS. `ControlledAuthority` answers 0 for a permission it does not know, and 0 means
    /// DENIED in every case. So a permission added to the provider and not added here would be refused in
    /// all of the theory's cases, the theory would stay green, and its green would be read as a statement
    /// about the whole set while covering none of the new permission's combinations. That is exactly what
    /// happened to `organization.work-centers.read` (#379): the fourth bit fixed the INSTANCE; this fixes
    /// the CAUSE. The same shape bit Finance the same day — `FinancePermissionPort.NameOf` ended in
    /// `_ => null`, making namelessness the default (#376). A catch-all arm answers for everything added
    /// after the theory was written.
    /// </para>
    /// <para>
    /// IT REDDENS RATHER THAN THROWS, deliberately. Making the fake throw on an unknown permission would
    /// be wrong: the host asks this same authority about OTHER modules' permissions, and those are
    /// legitimately unknown to it and must simply be denied. The unknown arm has to stay; what must not
    /// stay silent is an ORGANIZATION permission falling into it. So the pin is here, on the set, where it
    /// fails by NAME and in both directions — a permission added to the provider and not mapped, and a
    /// mapped name the provider no longer declares.
    /// </para>
    /// </summary>
    [Fact]
    public void TheFixturesPermissionBits_AreExactlyTheProvidersDeclaredPermissions()
    {
        var declared = new OrganizationShellCapabilityProvider().GetDeclarations()
            .SelectMany(declaration => declaration.RequiredPermissions)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        Assert.Equal(declared, PermissionBits.Order(StringComparer.Ordinal));
    }

    // The bound is DERIVED: every subset of the bits above, so adding a permission to the provider (and
    // therefore to the list, which the fact above enforces) widens the theory by itself. The previous
    // hand-written `16` was the other half of the same defect as the `_ => 0` arm — a number that had to
    // be remembered, beside a default that made forgetting invisible.
    public static IEnumerable<object[]> PermissionCases()
    {
        for (var permissions = 0; permissions < 1 << PermissionBits.Length; permissions++)
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
        // Each branch names the PERMISSION it turns on rather than a numeric mask, so the expectation
        // cannot drift from the bit list when its order changes.
        var expected = new List<string>();
        if ((permissions & Bit(ErpPermissions.OrganizationLocationsRead)) != 0)
        {
            expected.Add(OrganizationShellCapabilityProvider.ListCapabilityId);
            expected.Add(OrganizationShellCapabilityProvider.DetailCapabilityId);
            if ((permissions & Bit(ErpPermissions.OrganizationSitesRead)) != 0)
                expected.Add(OrganizationShellCapabilityProvider.SitesReadCapabilityId);
            if ((permissions & Bit(ErpPermissions.OrganizationLocationsStatusUpdate)) != 0)
                expected.Add(OrganizationShellCapabilityProvider.StatusCapabilityId);
        }

        // OUTSIDE the locations-read branch, and that placement is the assertion: ORG-03 stands on
        // `organization.work-centers.read` ALONE. A work-centre reader with no location access still
        // gets this capability — which is why the screen exists as its own sidebar entry rather than
        // a link inside ORG-01, a screen that caller cannot open at all.
        if ((permissions & Bit(ErpPermissions.OrganizationWorkCentersRead)) != 0)
            expected.Add(OrganizationShellCapabilityProvider.WorkCenterListCapabilityId);

        Assert.Equal(expected.Order(StringComparer.Ordinal), snapshot.Capabilities);
    }

    [Fact]
    public async Task LocationsOnly_KeepListAndDetailWhileSitesAndStatusStillReturn403()
    {
        await using var host = ControlledHost(
            new ControlledAuthority(Bit(ErpPermissions.OrganizationLocationsRead), 0));
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
        // NAMED, not `7` (SİPARİŞ's note on #379). The literal used to mean "all of them"; once ORG-03
        // added a fourth bit it silently came to mean "all but the new one" — still the right input for
        // this case, but correct BY ACCIDENT. Naming the three permissions makes it a decision: this case
        // is about revoking the two CONTROLS, so it grants exactly the location read and those two.
        var authority = new ControlledAuthority(
            Bits(ErpPermissions.OrganizationLocationsRead, ErpPermissions.OrganizationSitesRead,
                ErpPermissions.OrganizationLocationsStatusUpdate), 0);
        await using var host = ControlledHost(authority);
        using var client = Client(host);
        var before = await Snapshot(client);
        Assert.Equal(4, before.Capabilities.Count);
        authority.Permissions = Bit(ErpPermissions.OrganizationLocationsRead);
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
            // The bit comes from PermissionBits' ORDER, not from a switch that has to be extended by hand.
            // AuthorityBit, not Bit: a permission this fixture does not know still answers 0 and is denied
            // — which is CORRECT here, because the host asks this same authority about other modules'
            // permissions. The strict Bit() belongs to the authoring side, where an unknown name is a
            // mistake rather than a legitimate question. What guarantees no ORGANIZATION permission lands
            // in this arm is the set-equality fact above, not this line.
            var bit = AuthorityBit(permission);
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
