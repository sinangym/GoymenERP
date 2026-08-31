using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ShellCapabilityRegistryTests
{
    [Fact]
    public void AcceptedCounterpartyListProvider_MatchesExactOwnerSourceAndAuthority()
    {
        var provider = new CounterpartyShellCapabilityProvider();
        var registry = new ShellCapabilityRegistry(
            [provider],
            new ShellCapabilityAuthorityCatalog(
                [ShellCapabilityRegistryAuthorities.CounterpartyListV15]));

        var registration = Assert.Single(registry.Registrations);
        Assert.Equal(
            ShellCapabilityRegistryAuthorities.CounterpartyListV15,
            registry.Revision);
        Assert.Equal(CounterpartyShellCapabilityProvider.OwnerId, registration.Owner);
        Assert.Equal(
            CounterpartyShellCapabilityProvider.OwnerContractVersion,
            registration.OwnerContractVersion);
        Assert.Equal(
            CounterpartyShellCapabilityProvider.ListCapabilityId,
            registration.Declaration.CapabilityId);
        Assert.Equal(
            CounterpartyShellCapabilityProvider.ListRouteId,
            registration.Declaration.RouteId);
        Assert.Equal(
            CounterpartyShellCapabilityProvider.ListRoutePath,
            registration.Declaration.RoutePath);
        Assert.Equal(
            CounterpartyShellCapabilityProvider.ListScreenId,
            registration.Declaration.ScreenId);
        Assert.Equal(
            ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
            registration.Declaration.ContextRequirement);
        Assert.Equal(
            ShellCapabilityScopeRequirement.CompanyWide,
            registration.Declaration.ScopeRequirement);
        Assert.Equal(
            [ErpPermissions.CounterpartyPartiesRead],
            registration.Declaration.RequiredPermissions);
        Assert.False(registration.Declaration.ImplementationAvailable);
    }

    [Fact]
    public void EmptyRegistry_IsValidDeterministicAndExposesNoCapabilities()
    {
        var first = new ShellCapabilityRegistry([]);
        var second = new ShellCapabilityRegistry([]);

        Assert.Empty(first.Registrations);
        Assert.Equal(second.Revision, first.Revision);
        Assert.StartsWith("sha256:", first.Revision, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateCapabilityRoutePathOrOwner_RejectsTheWholeRegistry()
    {
        var first = Provider(
            "OWNER-A",
            Declaration("owner.a.read", "OWNER-A-ROUTE", "/owner/a", "OWNER-A-01"));
        var duplicateCapability = Provider(
            "OWNER-B",
            Declaration("owner.a.read", "OWNER-B-ROUTE", "/owner/b", "OWNER-B-01"));
        var duplicatePath = Provider(
            "OWNER-B",
            Declaration("owner.b.read", "OWNER-B-ROUTE", "/owner/a", "OWNER-B-01"));

        Assert.Throws<InvalidOperationException>(() =>
            new ShellCapabilityRegistry([first, duplicateCapability]));
        Assert.Throws<InvalidOperationException>(() =>
            new ShellCapabilityRegistry([first, duplicatePath]));
        Assert.Throws<InvalidOperationException>(() =>
            new ShellCapabilityRegistry([first, Provider(
                "OWNER-A",
                Declaration("owner.c.read", "OWNER-C-ROUTE", "/owner/c", "OWNER-C-01"))]));
    }

    [Fact]
    public void DuplicateRouteOrScreenIdentity_RejectsTheWholeRegistry()
    {
        var first = Provider(
            "OWNER-A",
            Declaration("owner.a.read", "OWNER-A-ROUTE", "/owner/a", "OWNER-A-01"));
        var duplicateRoute = Provider(
            "OWNER-B",
            Declaration("owner.b.read", "OWNER-A-ROUTE", "/owner/b", "OWNER-B-01"));
        var duplicateScreen = Provider(
            "OWNER-B",
            Declaration("owner.b.read", "OWNER-B-ROUTE", "/owner/b", "OWNER-A-01"));

        Assert.Throws<InvalidOperationException>(() =>
            new ShellCapabilityRegistry([first, duplicateRoute]));
        Assert.Throws<InvalidOperationException>(() =>
            new ShellCapabilityRegistry([first, duplicateScreen]));
    }

    [Fact]
    public void DifferentRouteParameterNames_CollideAfterCanonicalization()
    {
        var first = Provider(
            "OWNER-A",
            Declaration(
                "owner.a.read",
                "OWNER-A-ROUTE",
                "/owner/{id}",
                "OWNER-A-01"));
        var second = Provider(
            "OWNER-B",
            Declaration(
                "owner.b.read",
                "OWNER-B-ROUTE",
                "/owner/{party-id}",
                "OWNER-B-01"));

        Assert.Throws<InvalidOperationException>(() =>
            new ShellCapabilityRegistry([first, second]));
    }

    [Theory]
    [InlineData("/cari/new", "/cari/{id}")]
    [InlineData("/cari/{id}", "/cari/new")]
    [InlineData("/a/{id}/details", "/a/active/{section}")]
    public void StaticAndParameterRouteOverlaps_RejectTheWholeRegistry(
        string firstPath,
        string secondPath)
    {
        var first = Provider(
            "OWNER-A",
            Declaration("owner.a.read", "OWNER-A-ROUTE", firstPath, "OWNER-A-01"));
        var second = Provider(
            "OWNER-B",
            Declaration("owner.b.read", "OWNER-B-ROUTE", secondPath, "OWNER-B-01"));

        Assert.Throws<InvalidOperationException>(() =>
            new ShellCapabilityRegistry([first, second]));
    }

    [Fact]
    public void DistinctRoutesAndDifferentSegmentCounts_DoNotCollide()
    {
        var providers = new[]
        {
            Provider(
                "OWNER-A",
                Declaration("owner.a.read", "OWNER-A-ROUTE", "/a/new", "OWNER-A-01")),
            Provider(
                "OWNER-B",
                Declaration("owner.b.read", "OWNER-B-ROUTE", "/a/edit", "OWNER-B-01")),
            Provider(
                "OWNER-C",
                Declaration(
                    "owner.c.read",
                    "OWNER-C-ROUTE",
                    "/a/{id}/details",
                    "OWNER-C-01")),
        };

        var registry = AuthorizedRegistry(providers);

        Assert.Equal(3, registry.Registrations.Count);
    }

    [Fact]
    public void NonEmptyProviderSet_RequiresExactPlatformAuthorityRevision()
    {
        var providers = new[]
        {
            Provider(
                "OWNER-A",
                Declaration("owner.a.read", "OWNER-A-ROUTE", "/owner/a", "OWNER-A-01")),
        };

        Assert.Throws<InvalidOperationException>(() =>
            new ShellCapabilityRegistry(providers));

        var acceptedRevision = ShellCapabilityRegistry.ComputeCandidateRevision(providers);
        var accepted = new ShellCapabilityRegistry(
            providers,
            new ShellCapabilityAuthorityCatalog([acceptedRevision]));
        Assert.Single(accepted.Registrations);

        var changedProviders = new[]
        {
            Provider(
                "OWNER-A",
                Declaration(
                    "owner.a.read",
                    "OWNER-A-ROUTE",
                    "/owner/changed",
                    "OWNER-A-01")),
        };
        Assert.Throws<InvalidOperationException>(() =>
            new ShellCapabilityRegistry(
                changedProviders,
                new ShellCapabilityAuthorityCatalog([acceptedRevision])));
    }

    [Fact]
    public void UnknownPermissionMalformedIdentityAndPermissionlessAvailability_AreRejected()
    {
        var unknownPermission = Declaration(
            "owner.a.read",
            "OWNER-A-ROUTE",
            "/owner/a",
            "OWNER-A-01",
            requiredPermissions: ["unknown.permission"]);
        var malformedCapability = Declaration(
            "Owner A Read",
            "OWNER-A-ROUTE",
            "/owner/a",
            "OWNER-A-01");
        var permissionless = Declaration(
            "owner.a.read",
            "OWNER-A-ROUTE",
            "/owner/a",
            "OWNER-A-01",
            requiredPermissions: []);

        Assert.Throws<InvalidOperationException>(() =>
            new ShellCapabilityRegistry([Provider("OWNER-A", unknownPermission)]));
        Assert.Throws<InvalidOperationException>(() =>
            new ShellCapabilityRegistry([Provider("OWNER-A", malformedCapability)]));
        Assert.Throws<InvalidOperationException>(() =>
            new ShellCapabilityRegistry([Provider("OWNER-A", permissionless)]));
    }

    [Fact]
    public void MalformedRouteContractContextScopeAndDuplicatePermission_AreRejected()
    {
        var invalidRoute = Declaration(
            "owner.a.read",
            "OWNER-A-ROUTE",
            "/owner//a",
            "OWNER-A-01");
        var invalidContext = Declaration(
            "owner.a.read",
            "OWNER-A-ROUTE",
            "/owner/a",
            "OWNER-A-01") with
        {
            ContextRequirement = (ShellCapabilityContextRequirement)999,
        };
        var invalidScope = Declaration(
            "owner.a.read",
            "OWNER-A-ROUTE",
            "/owner/a",
            "OWNER-A-01") with
        {
            ScopeRequirement = (ShellCapabilityScopeRequirement)999,
        };
        var duplicatePermission = Declaration(
            "owner.a.read",
            "OWNER-A-ROUTE",
            "/owner/a",
            "OWNER-A-01",
            requiredPermissions:
            [
                ErpPermissions.OrganizationContextRead,
                ErpPermissions.OrganizationContextRead,
            ]);

        Assert.Throws<InvalidOperationException>(() =>
            new ShellCapabilityRegistry([Provider("OWNER-A", invalidRoute)]));
        Assert.Throws<InvalidOperationException>(() =>
            new ShellCapabilityRegistry([Provider(
                "OWNER-A",
                "bad contract",
                Declaration(
                    "owner.a.read",
                    "OWNER-A-ROUTE",
                    "/owner/a",
                    "OWNER-A-01"))]));
        Assert.Throws<InvalidOperationException>(() =>
            new ShellCapabilityRegistry([Provider("OWNER-A", invalidContext)]));
        Assert.Throws<InvalidOperationException>(() =>
            new ShellCapabilityRegistry([Provider("OWNER-A", invalidScope)]));
        Assert.Throws<InvalidOperationException>(() =>
            new ShellCapabilityRegistry([Provider("OWNER-A", duplicatePermission)]));
    }

    [Fact]
    public async Task Resolver_RequiresEveryPermissionAndNeverEscalatesNarrowScope()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var registry = AuthorizedRegistry([
            Provider(
                "OWNER-A",
                Declaration(
                    "owner.a.company-wide",
                    "OWNER-A-WIDE",
                    "/owner/a-wide",
                    "OWNER-A-01",
                    ShellCapabilityScopeRequirement.CompanyWide),
                Declaration(
                    "owner.a.scoped",
                    "OWNER-A-SCOPED",
                    "/owner/a-scoped",
                    "OWNER-A-02",
                    ShellCapabilityScopeRequirement.AnyGrantedScope))
        ]);
        var companyContext = new CompanyContext();
        companyContext.Set(companyId);
        var scope = new PermissionScope(
            true,
            false,
            new HashSet<string>(["TEST-ROLE"], StringComparer.Ordinal),
            new HashSet<Guid>([Guid.NewGuid()]),
            new HashSet<Guid>(),
            new HashSet<Guid>());
        using var protector = new ShellCapabilityGenerationProtector();
        var service = new ShellCapabilityService(
            registry,
            new FakeSessionBindingResolver("trusted-test-session"),
            protector,
            new FakeCurrentUser(userId, companyId),
            companyContext,
            new FakeAuthorizationService(scope, companyId),
            new FakeOrganizationService(companyId));

        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal(
            ["owner.a.scoped"],
            snapshot.Capabilities);
        Assert.DoesNotContain("TEST-ROLE", snapshot.Generation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generation_ChangesWithSessionAuthorizationAndCompanyContext()
    {
        var userId = Guid.NewGuid();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var registry = new ShellCapabilityRegistry([]);
        using var protector = new ShellCapabilityGenerationProtector();

        var first = await SnapshotAsync(
            registry,
            protector,
            userId,
            [companyA, companyB],
            companyA,
            "session-a",
            PermissionScope.Denied);
        var differentSession = await SnapshotAsync(
            registry,
            protector,
            userId,
            [companyA, companyB],
            companyA,
            "session-b",
            PermissionScope.Denied);
        var differentAuthorization = await SnapshotAsync(
            registry,
            protector,
            userId,
            [companyA, companyB],
            companyA,
            "session-a",
            CompanyWideScope());
        var differentCompany = await SnapshotAsync(
            registry,
            protector,
            userId,
            [companyA, companyB],
            companyB,
            "session-a",
            PermissionScope.Denied);

        Assert.NotEqual(first.Generation, differentSession.Generation);
        Assert.NotEqual(first.Generation, differentAuthorization.Generation);
        Assert.NotEqual(first.Generation, differentCompany.Generation);
    }

    [Fact]
    public async Task Snapshot_RejectsStaleIdentityAccessOrInactiveOrganizationCompany()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var companyContext = new CompanyContext();
        companyContext.Set(companyId);
        using var protector = new ShellCapabilityGenerationProtector();

        var staleIdentity = new ShellCapabilityService(
            new ShellCapabilityRegistry([]),
            new FakeSessionBindingResolver("trusted-test-session"),
            protector,
            new FakeCurrentUser(userId, companyId),
            companyContext,
            new FakeAuthorizationService(PermissionScope.Denied),
            new FakeOrganizationService(companyId));
        var inactiveOrganization = new ShellCapabilityService(
            new ShellCapabilityRegistry([]),
            new FakeSessionBindingResolver("trusted-test-session"),
            protector,
            new FakeCurrentUser(userId, companyId),
            companyContext,
            new FakeAuthorizationService(PermissionScope.Denied, companyId),
            new FakeOrganizationService());

        await Assert.ThrowsAsync<ShellCapabilityUnavailableException>(
            async () => await staleIdentity.GetSnapshotAsync());
        await Assert.ThrowsAsync<ShellCapabilityUnavailableException>(
            async () => await inactiveOrganization.GetSnapshotAsync());
    }

    private static async Task<ShellCapabilitySnapshot> SnapshotAsync(
        ShellCapabilityRegistry registry,
        ShellCapabilityGenerationProtector protector,
        Guid userId,
        Guid[] allowedCompanyIds,
        Guid selectedCompanyId,
        string session,
        PermissionScope scope)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(selectedCompanyId);
        var service = new ShellCapabilityService(
            registry,
            new FakeSessionBindingResolver(session),
            protector,
            new FakeCurrentUser(userId, allowedCompanyIds),
            companyContext,
            new FakeAuthorizationService(scope, allowedCompanyIds),
            new FakeOrganizationService(allowedCompanyIds));
        return await service.GetSnapshotAsync();
    }

    private static ShellCapabilityRegistry AuthorizedRegistry(
        IReadOnlyCollection<IShellCapabilityProvider> providers)
    {
        var revision = ShellCapabilityRegistry.ComputeCandidateRevision(providers);
        return new ShellCapabilityRegistry(
            providers,
            new ShellCapabilityAuthorityCatalog([revision]));
    }

    private static ShellCapabilityDeclaration Declaration(
        string capabilityId,
        string routeId,
        string routePath,
        string screenId,
        ShellCapabilityScopeRequirement scopeRequirement =
            ShellCapabilityScopeRequirement.CompanyWide,
        IReadOnlyCollection<string>? requiredPermissions = null)
    {
        return new ShellCapabilityDeclaration(
            capabilityId,
            routeId,
            routePath,
            screenId,
            ShellCapabilityContextRequirement.SingleCompany,
            scopeRequirement,
            requiredPermissions ?? [ErpPermissions.OrganizationContextRead],
            ImplementationAvailable: true);
    }

    private static IShellCapabilityProvider Provider(
        string owner,
        params ShellCapabilityDeclaration[] declarations)
    {
        return Provider(owner, "OWNER-CONTRACT-V1", declarations);
    }

    private static IShellCapabilityProvider Provider(
        string owner,
        string contractVersion,
        params ShellCapabilityDeclaration[] declarations)
    {
        return new FakeCapabilityProvider(owner, contractVersion, declarations);
    }

    private static PermissionScope CompanyWideScope()
    {
        return new PermissionScope(
            true,
            true,
            new HashSet<string>(["TEST-ROLE"], StringComparer.Ordinal),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>());
    }

    private sealed class FakeCapabilityProvider(
        string owner,
        string contractVersion,
        IReadOnlyCollection<ShellCapabilityDeclaration> declarations)
        : IShellCapabilityProvider
    {
        public string Owner => owner;

        public string ContractVersion => contractVersion;

        public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
            declarations;
    }

    private sealed class FakeSessionBindingResolver(string binding)
        : IActorSessionBindingResolver
    {
        public Task<string?> ResolveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(binding);
    }

    private sealed class FakeAuthorizationService(
        PermissionScope scope,
        params Guid[] allowedCompanyIds)
        : IErpAuthorizationService
    {
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(
            Guid companyId,
            IReadOnlyCollection<string> permissions,
            CancellationToken cancellationToken = default)
        {
            var scopes = permissions.ToDictionary(
                permission => permission,
                _ => scope,
                StringComparer.Ordinal);
            return Task.FromResult(new AuthorizationSnapshot(
                $"fake:{scope.IsGranted}:{scope.IsCompanyWide}",
                allowedCompanyIds.ToHashSet(),
                scopes));
        }

        public Task<bool> HasPermissionAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Shell bulk snapshot kullanmalıdır.");

        public Task<PermissionScope> GetPermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Shell bulk snapshot kullanmalıdır.");
    }

    private sealed class FakeOrganizationService(params Guid[] activeCompanyIds)
        : IOrganizationService
    {
        public Task<IReadOnlyList<CompanySummary>> GetAuthorizedCompaniesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CompanySummary>>(activeCompanyIds
                .Select(id => new CompanySummary(
                    id,
                    $"TEST-{id:N}",
                    $"Test Company {id:N}",
                    false))
                .ToArray());

        public Task<IReadOnlyList<SiteSummary>> GetSitesAsync(
            CancellationToken cancellationToken) => throw NotUsed();

        public Task<PageResult<LocationSummary>> GetLocationsAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken) => throw NotUsed();

        public Task<LocationSummary?> GetLocationAsync(
            Guid id,
            CancellationToken cancellationToken) => throw NotUsed();

        public Task<IReadOnlyList<WorkCenterSummary>> GetWorkCentersAsync(
            CancellationToken cancellationToken) => throw NotUsed();

        public Task<CommandExecutionResult> SetLocationActiveStateAsync(
            Guid id,
            SetLocationActiveState command,
            CommandMetadata metadata,
            CancellationToken cancellationToken) => throw NotUsed();

        private static InvalidOperationException NotUsed() =>
            new("Shell testinde bu Organization işlemi kullanılmamalıdır.");
    }

    private sealed class FakeCurrentUser(Guid userId, params Guid[] companyIds)
        : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string UserId => userId.ToString("D");

        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companyIds.ToHashSet();

        public bool CanAccessCompany(Guid companyId) =>
            AllowedCompanyIds.Contains(companyId);
    }
}
