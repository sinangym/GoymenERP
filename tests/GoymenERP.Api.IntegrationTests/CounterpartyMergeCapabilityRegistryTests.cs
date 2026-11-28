using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyMergeCapabilityRegistryTests
{
    [Fact]
    public void Real_candidate_registry_validates_merge_control_parent_without_repinning_the_normal_host()
    {
        var provider = new CounterpartyShellCapabilityProvider();
        var registry = new ShellCapabilityRegistry([provider], new ShellCapabilityAuthorityCatalog([ShellCapabilityRegistry.ComputeCandidateRevision([provider])]));
        Assert.Equal("1.9", provider.ContractVersion); Assert.Equal(9, registry.Registrations.Count);
        var control = Assert.Single(registry.Registrations, x => x.Declaration.CapabilityId == CounterpartyShellCapabilityProvider.PartyMergeCapabilityId).Declaration;
        var native = Assert.Single(registry.Registrations, x => x.Declaration.CapabilityId == CounterpartyShellCapabilityProvider.DetailCapabilityId).Declaration;
        Assert.Equal(native.CapabilityId, control.ParentCapabilityId); Assert.Equal(native.RoutePath, control.RoutePath);
        Assert.Equal(native.ContextRequirement, control.ContextRequirement); Assert.Equal(native.ScreenId, control.ScreenId);
        Assert.Equal([ErpPermissions.CounterpartyPartiesRead, ErpPermissions.CounterpartyPartiesMergeApprove], control.RequiredPermissions);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, control.ScopeRequirement);
        Assert.Equal("/cari/:partyId", native.RoutePath);
        Assert.Equal([ErpPermissions.CounterpartyPartiesRead], native.RequiredPermissions);
        var list = Assert.Single(registry.Registrations, x => x.Declaration.CapabilityId == CounterpartyShellCapabilityProvider.ListCapabilityId).Declaration;
        Assert.Equal("/cari", list.RoutePath); Assert.Equal([ErpPermissions.CounterpartyPartiesRead], list.RequiredPermissions);
    }
}
