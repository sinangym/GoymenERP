using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ShellControlCapabilityRegistryTests
{
    [Fact]
    public void ExistingRouteOnlyAuthority_RemainsByteIdentical()
    {
        Assert.Equal(
            ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetail,
            ShellCapabilityRegistry.ComputeCandidateRevision([new CounterpartyShellCapabilityProvider()]));
    }

    [Fact]
    public void Control_ReusesOnlyItsRealParentTupleAndHasIndependentPermission()
    {
        var parent = Route();
        var control = Control(parent);
        var provider = new Provider("TEST-CONTROLS", [parent, control]);
        var revision = ShellCapabilityRegistry.ComputeCandidateRevision([provider]);
        var registry = new ShellCapabilityRegistry(
            [provider], new ShellCapabilityAuthorityCatalog([revision]));

        Assert.Equal(2, registry.Registrations.Count);
        Assert.Single(registry.Registrations, item => item.Declaration.ParentCapabilityId is null);
        Assert.Equal(parent.CapabilityId, control.ParentCapabilityId);
        Assert.Contains(ErpPermissions.OrganizationLocationsStatusUpdate, control.RequiredPermissions);
        Assert.DoesNotContain(ErpPermissions.OrganizationLocationsStatusUpdate, parent.RequiredPermissions);
        Assert.NotEqual(revision, ShellCapabilityRegistry.ComputeCandidateRevision(
            [new Provider("TEST-CONTROLS", [parent])]));
        Assert.Throws<InvalidOperationException>(() => new ShellCapabilityRegistry(
            [provider], new ShellCapabilityAuthorityCatalog([])));
    }

    [Theory]
    [InlineData("orphan")]
    [InlineData("self")]
    [InlineData("empty-parent")]
    [InlineData("route-id")]
    [InlineData("route-path")]
    [InlineData("screen")]
    [InlineData("context")]
    [InlineData("scope")]
    [InlineData("weaker-permission")]
    [InlineData("duplicate-id")]
    [InlineData("fake-empty-route")]
    public void InvalidControl_CannotBypassRouteValidation(string defect)
    {
        var parent = Route();
        var control = Control(parent);
        control = defect switch
        {
            "orphan" => control with { ParentCapabilityId = "test.missing" },
            "self" => control with { ParentCapabilityId = control.CapabilityId },
            "empty-parent" => control with { ParentCapabilityId = "" },
            "route-id" => control with { RouteId = "test.other" },
            "route-path" => control with { RoutePath = "/other" },
            "screen" => control with { ScreenId = "TEST-OTHER" },
            "context" => control with { ContextRequirement = ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead },
            "scope" => control with { ScopeRequirement = ShellCapabilityScopeRequirement.CompanyWide },
            "weaker-permission" => control with { RequiredPermissions = [ErpPermissions.OrganizationLocationsStatusUpdate] },
            "duplicate-id" => control with { CapabilityId = parent.CapabilityId },
            "fake-empty-route" => control with { RoutePath = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        Assert.Throws<InvalidOperationException>(() => ShellCapabilityRegistry.ComputeCandidateRevision(
            [new Provider("TEST-CONTROLS", [parent, control])]));
    }

    [Fact]
    public void Controls_CannotCrossOwnersFormChainsOrActivateAnUnavailableParent()
    {
        var parent = Route();
        var control = Control(parent);
        Assert.Throws<InvalidOperationException>(() => ShellCapabilityRegistry.ComputeCandidateRevision(
            [new Provider("TEST-PARENT", [parent]), new Provider("TEST-CONTROLS", [control])]));
        Assert.Throws<InvalidOperationException>(() => ShellCapabilityRegistry.ComputeCandidateRevision(
            [new Provider("TEST-CONTROLS", [parent, control,
                control with { CapabilityId = "test.child", ParentCapabilityId = control.CapabilityId }])]));
        Assert.Throws<InvalidOperationException>(() => ShellCapabilityRegistry.ComputeCandidateRevision(
            [new Provider("TEST-CONTROLS", [parent with { ImplementationAvailable = false }, control])]));
        Assert.Throws<InvalidOperationException>(() => ShellCapabilityRegistry.ComputeCandidateRevision(
            [new Provider("TEST-CONTROLS", [parent with { ParentCapabilityId = control.CapabilityId }, control])]));
    }

    [Fact]
    public void OrdinaryDuplicateRoutes_AreStillRejected()
    {
        var parent = Route();
        Assert.Throws<InvalidOperationException>(() => ShellCapabilityRegistry.ComputeCandidateRevision(
            [new Provider("TEST-CONTROLS", [parent, parent with { CapabilityId = "test.second" }])]));
    }

    private static ShellCapabilityDeclaration Route() => new(
        "test.locations", "test.locations", "/test-locations", "TEST-LOCATIONS",
        ShellCapabilityRoutePrecedence.Static,
        ShellCapabilityContextRequirement.SingleCompany,
        ShellCapabilityScopeRequirement.AnyGrantedScope,
        [ErpPermissions.OrganizationLocationsRead], true);

    private static ShellCapabilityDeclaration Control(ShellCapabilityDeclaration parent) => parent with
    {
        CapabilityId = "test.locations.status",
        ParentCapabilityId = parent.CapabilityId,
        RequiredPermissions = [ErpPermissions.OrganizationLocationsRead, ErpPermissions.OrganizationLocationsStatusUpdate],
    };

    private sealed class Provider(string owner, IReadOnlyCollection<ShellCapabilityDeclaration> declarations)
        : IShellCapabilityProvider
    {
        public string Owner => owner;
        public string ContractVersion => "1.0";
        public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() => declarations;
    }
}
