using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ShellCapabilityRegistryTests
{
    private static ShellCapabilityDeclaration[] ProductDeclarations() =>
    [
        ProductDeclaration("product.items.list", "/urun", "PRODUCT-TEST-LIST", GroupSharedMasterOperation.Read),
        ProductDeclaration("product.items.detail", "/urun/:productId", "PRODUCT-TEST-DETAIL", GroupSharedMasterOperation.Read),
        ProductDeclaration("product.items.create", "/urun/yeni", "PRODUCT-TEST-CREATE", GroupSharedMasterOperation.DraftCreate),
        ProductDeclaration("product.items.edit", "/urun/:productId/duzenle", "PRODUCT-TEST-EDIT", GroupSharedMasterOperation.DraftBasicEdit),
    ];

    private static ShellCapabilityDeclaration ProductDeclaration(string id, string path, string fixtureViewId,
        GroupSharedMasterOperation operation) => Declaration(id, id, path, fixtureViewId,
            ShellCapabilityScopeRequirement.AnyGrantedScope, requiredPermissions: []) with { GlobalProductOperation = operation };

    [Fact]
    public void GlobalProductRegistry_RequiresExactAuthorityAndDoesNotChangeCompanyRevision()
    {
        var providers = new[] { Provider("PRODUCT", ProductDeclarations()) };
        Assert.Throws<InvalidOperationException>(() => new ShellCapabilityRegistry(providers));
        Assert.Equal(4, AuthorizedRegistry(providers).Registrations.Count);
        var companyProvider = new GoymenERP.Modules.Counterparty.Infrastructure.Shell.CounterpartyShellCapabilityProvider();
        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyListCreateLinkAndDetailV15,
            ShellCapabilityRegistry.ComputeCandidateRevision([companyProvider]));
    }

    [Theory]
    [InlineData("company-alias")]
    [InlineData("missing-operation")]
    [InlineData("wrong-operation")]
    [InlineData("unknown-operation")]
    [InlineData("unknown-capability")]
    [InlineData("wrong-route")]
    [InlineData("multi-company")]
    [InlineData("company-scope")]
    [InlineData("wrong-owner")]
    [InlineData("parent-control")]
    public void GlobalProductRegistry_RejectsAmbiguousOrCompanyDerivedAuthority(string change)
    {
        var declaration = ProductDeclarations()[2];
        declaration = change switch
        {
            "company-alias" => declaration with { RequiredPermissions = [ErpPermissions.IdentityRolesManage] },
            "missing-operation" => declaration with { GlobalProductOperation = null, RequiredPermissions = [ErpPermissions.IdentityRolesManage] },
            "wrong-operation" => declaration with { GlobalProductOperation = GroupSharedMasterOperation.Read },
            "unknown-operation" => declaration with { GlobalProductOperation = (GroupSharedMasterOperation)99 },
            "unknown-capability" => declaration with { CapabilityId = "product.items.unapproved" },
            "wrong-route" => declaration with { RoutePath = "/other" },
            "multi-company" => declaration with { ContextRequirement = ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead },
            "company-scope" => declaration with { ScopeRequirement = ShellCapabilityScopeRequirement.CompanyWide },
            "parent-control" => declaration with { ParentCapabilityId = "product.items.list" },
            _ => declaration,
        };
        Assert.Throws<InvalidOperationException>(() => ShellCapabilityRegistry.ComputeCandidateRevision(
            [Provider(change == "wrong-owner" ? "OTHER" : "PRODUCT", declaration)]));
    }

    [Theory]
    [InlineData(GroupSharedMasterOperation.Read, "product.items.detail,product.items.list")]
    [InlineData(GroupSharedMasterOperation.DraftCreate, "product.items.create")]
    [InlineData(GroupSharedMasterOperation.DraftBasicEdit, "product.items.edit")]
    public async Task GlobalProductSnapshot_OnlyExplicitOperationGrantEnablesItsCapabilities(
        GroupSharedMasterOperation allowed, string expected)
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        var authority = new ProductAuthority((operation, _) => operation == allowed
            ? new(true, GroupSharedMasterKind.Product, operation, actor, company, "global:1")
            : GroupSharedMasterAuthorizationSnapshot.Denied(GroupSharedMasterKind.Product, operation));
        using var protector = new ShellCapabilityGenerationProtector();
        var snapshot = await ProductService(actor, company, protector, authority, PermissionScope.Denied).GetSnapshotAsync();
        Assert.Equal(expected.Split(','), snapshot.Capabilities);
        Assert.All(authority.Calls.Values, count => Assert.Equal(2, count));
    }

    [Fact]
    public void GlobalProductAndOrganizationControls_CoexistWithoutChangingAcceptedNormalAuthority()
    {
        IShellCapabilityProvider[] normal =
        [
            new GoymenERP.Modules.Counterparty.Infrastructure.Shell.CounterpartyShellCapabilityProvider(),
            new GoymenERP.Modules.Organization.Infrastructure.Shell.OrganizationShellCapabilityProvider(),
        ];
        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15AndOrganizationV11,
            ShellCapabilityRegistry.ComputeCandidateRevision(normal));
        var combined = AuthorizedRegistry([.. normal, Provider("PRODUCT", ProductDeclarations())]);
        Assert.Equal(12, combined.Registrations.Count);
        Assert.Equal(4, combined.Registrations.Count(item => item.Declaration.GlobalProductOperation is not null));
        Assert.Equal(2, combined.Registrations.Count(item => item.Declaration.ParentCapabilityId is not null));
        Assert.All(combined.Registrations.Where(item => item.Declaration.ParentCapabilityId is not null),
            item => Assert.Null(item.Declaration.GlobalProductOperation));
        Assert.NotEqual(ShellCapabilityRegistryAuthorities.CounterpartyV15AndOrganizationV11, combined.Revision);
    }

    [Fact]
    public async Task GlobalProductSnapshot_CompanyAdministratorWithoutGlobalAuthorityHasNoProductCapabilities()
    {
        using var protector = new ShellCapabilityGenerationProtector();
        var snapshot = await ProductService(Guid.NewGuid(), Guid.NewGuid(), protector, null, CompanyWideScope()).GetSnapshotAsync();
        Assert.Empty(snapshot.Capabilities);
    }

    [Fact]
    public async Task GlobalProductSnapshot_NoSelectedOriginDoesNotProbeOrGrantGlobalOperations()
    {
        var authority = new ProductAuthority((_, _) => throw new InvalidOperationException("No selected origin must not probe global authority."));
        using var protector = new ShellCapabilityGenerationProtector();
        var snapshot = await ProductService(Guid.NewGuid(), Guid.NewGuid(), protector, authority,
            CompanyWideScope(), selectOrigin: false).GetSnapshotAsync();
        Assert.Empty(snapshot.Capabilities);
        Assert.Empty(authority.Calls);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("origin")]
    [InlineData("operation")]
    [InlineData("subject")]
    [InlineData("revision")]
    public async Task GlobalProductSnapshot_RejectsMismatchedTrustedDecision(string change)
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        var authority = new ProductAuthority((operation, _) =>
        {
            var result = new GroupSharedMasterAuthorizationSnapshot(true, GroupSharedMasterKind.Product,
                operation, actor, company, "global:1");
            return change switch
            {
                "actor" => result with { LocalActorId = Guid.NewGuid() },
                "origin" => result with { OriginCompanyId = Guid.NewGuid() },
                "operation" => result with { Operation = (GroupSharedMasterOperation)99 },
                "subject" => result with { Subject = (GroupSharedMasterKind)99 },
                _ => result with { AuthorityRevision = "bad\nrevision" },
            };
        });
        using var protector = new ShellCapabilityGenerationProtector();
        await Assert.ThrowsAsync<ShellCapabilityUnavailableException>(() =>
            ProductService(actor, company, protector, authority, CompanyWideScope()).GetSnapshotAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GlobalProductSnapshot_RevocationOrRevisionChangeDuringReadFailsClosed(bool changeRevision)
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        var authority = new ProductAuthority((operation, count) => count == 1 || changeRevision
            ? new(true, GroupSharedMasterKind.Product, operation, actor, company, count == 1 ? "global:1" : "global:2")
            : GroupSharedMasterAuthorizationSnapshot.Denied(GroupSharedMasterKind.Product, operation));
        using var protector = new ShellCapabilityGenerationProtector();
        await Assert.ThrowsAsync<ShellCapabilityUnavailableException>(() =>
            ProductService(actor, company, protector, authority, CompanyWideScope()).GetSnapshotAsync());
    }

    [Fact]
    public async Task GlobalProductSnapshot_GenerationBindsGlobalRevisionEvenWhenCapabilitiesAreUnchanged()
    {
        var actor = Guid.NewGuid();
        var company = Guid.NewGuid();
        var revision = "global:1";
        var authority = new ProductAuthority((operation, _) => new(true, GroupSharedMasterKind.Product, operation, actor, company, revision));
        using var protector = new ShellCapabilityGenerationProtector();
        var service = ProductService(actor, company, protector, authority, CompanyWideScope());
        var first = await service.GetSnapshotAsync();
        revision = "global:2";
        var second = await service.GetSnapshotAsync();
        Assert.Equal(first.Capabilities, second.Capabilities);
        Assert.NotEqual(first.Generation, second.Generation);
    }

    private static ShellCapabilityService ProductService(Guid actor, Guid company,
        ShellCapabilityGenerationProtector protector, IGroupSharedMasterAuthorizationService? authority,
        PermissionScope companyScope, bool selectOrigin = true)
    {
        var context = new CompanyContext();
        if (selectOrigin) context.Set(company);
        return new(AuthorizedRegistry([Provider("PRODUCT", ProductDeclarations())]),
            new FakeSessionBindingResolver("product-fixture-session"), protector,
            new FakeCurrentUser(actor, company), context,
            new FakeAuthorizationService(companyScope, company), new FakeOrganizationService(company), authority);
    }

    private sealed class ProductAuthority(Func<GroupSharedMasterOperation, int, GroupSharedMasterAuthorizationSnapshot> resolve)
        : IGroupSharedMasterAuthorizationService
    {
        public Dictionary<GroupSharedMasterOperation, int> Calls { get; } = [];
        public Task<GroupSharedMasterAuthorizationSnapshot> GetSnapshotAsync(GroupSharedMasterKind subject,
            GroupSharedMasterOperation operation, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(GroupSharedMasterKind.Product, subject);
            Calls[operation] = Calls.GetValueOrDefault(operation) + 1;
            return Task.FromResult(resolve(operation, Calls[operation]));
        }
    }
}
