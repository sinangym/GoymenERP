using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ShellCapabilityRegistryTests
{
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
    public async Task Resolver_RequiresEveryPermissionAndNeverEscalatesNarrowScope()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var registry = new ShellCapabilityRegistry([
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
            new FakeAuthorizationService(scope));

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
            new FakeAuthorizationService(scope));
        return await service.GetSnapshotAsync();
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
        return new FakeCapabilityProvider(owner, declarations);
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
        IReadOnlyCollection<ShellCapabilityDeclaration> declarations)
        : IShellCapabilityProvider
    {
        public string Owner => owner;

        public string ContractVersion => "OWNER-CONTRACT-V1";

        public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
            declarations;
    }

    private sealed class FakeSessionBindingResolver(string binding)
        : IActorSessionBindingResolver
    {
        public Task<string?> ResolveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(binding);
    }

    private sealed class FakeAuthorizationService(PermissionScope scope)
        : IErpAuthorizationService
    {
        public Task<bool> HasPermissionAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(scope.IsGranted);

        public Task<PermissionScope> GetPermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(scope);
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
