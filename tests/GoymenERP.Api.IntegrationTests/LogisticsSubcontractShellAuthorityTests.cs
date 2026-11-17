using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Shell;

namespace GoymenERP.Api.IntegrationTests;

public sealed class LogisticsSubcontractShellAuthorityTests
{
    [Fact]
    public void Subcontract_delivery_screens_and_actions_preserve_current_permissions_without_publishing_acceptance()
    {
        var declarations = new LogisticsShellCapabilityProvider().GetDeclarations()
            .Where(item => item.CapabilityId.StartsWith("logistics.subcontract-deliveries.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(8, declarations.Length);
        Assert.All(declarations, item =>
        {
            Assert.False(item.ImplementationAvailable);
            Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, item.ContextRequirement);
            Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, item.ScopeRequirement);
            Assert.All(item.RequiredPermissions, permission => Assert.True(ErpPermissions.IsKnown(permission)));
        });
        Assert.Equal(3, declarations.Count(item => item.ParentCapabilityId is null));
        var create = declarations.Single(item => item.CapabilityId == LogisticsShellCapabilityProvider.SubcontractCreateCapabilityId);
        Assert.Equal([LogisticsPermissionManifestV1.InstructionsCreate, ErpPermissions.InventoryPhysicalAllocationWrite], create.RequiredPermissions);
        var correction = declarations.Single(item => item.CapabilityId == "logistics.subcontract-deliveries.weighing-correct");
        Assert.Equal(LogisticsShellCapabilityProvider.SubcontractDetailCapabilityId, correction.ParentCapabilityId);
        Assert.Equal([LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.WeighingsCorrect], correction.RequiredPermissions);
        var ship = declarations.Single(item => item.CapabilityId == "logistics.subcontract-deliveries.ship");
        Assert.Equal([LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.ShipmentsRecord, ErpPermissions.InventoryPhysicalAllocationWrite], ship.RequiredPermissions);
    }
}
