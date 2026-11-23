using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Infrastructure.Shell;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ManufacturingPlanReorderPermissionRegistrationTests
{
    [Fact]
    public void The_reorder_name_is_registered_once_company_wide_without_a_grant_and_the_control_uses_the_persisted_queue()
    {
        const string name = "manufacturing.plans.reorder";
        Assert.Equal(name, ErpPermissions.ManufacturingPlansReorder);
        Assert.True(ErpPermissions.IsKnown(name));
        Assert.Single(ErpPermissions.All, item => item == name);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(name));
        Assert.False(ErpPermissions.IsCritical(name));
        Assert.Equal(ErpPermissions.All.Where(item => item.StartsWith("manufacturing.", StringComparison.Ordinal)).Order(),
            ManufacturingPermissionManifestV1.Names.Order());
        var control = Assert.Single(new ManufacturingShellCapabilityProvider().GetDeclarations(),
            item => item.CapabilityId == ManufacturingShellCapabilityProvider.PlanReorderCapabilityId);
        Assert.True(control.ImplementationAvailable);
        Assert.Contains(name, control.RequiredPermissions);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var seed = File.ReadAllText(Path.Combine(directory.FullName, "src/backend/GoymenERP.Api/Seed/identity-seed.json"));
        Assert.DoesNotContain(name, seed, StringComparison.Ordinal);
    }
}
