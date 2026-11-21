using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Shell;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SettingsManufacturingProfileShellAuthorityTests
{
    [Fact]
    public void Manufacturing_profile_controls_require_their_read_and_command_names_without_an_approval_control()
    {
        var declarations = new SettingsShellCapabilityProvider().GetDeclarations();
        foreach (var (read, prepare, publish) in new[]
        {
            (ManufacturingParameterProfileActionsV1.Read, ManufacturingParameterProfileActionsV1.Prepare,
                ManufacturingParameterProfileActionsV1.Publish),
            (ManufacturingToleranceProfileReadActionV1.Read, ManufacturingToleranceProfileActionsV1.Prepare,
                ManufacturingToleranceProfileActionsV1.Publish),
        })
        {
            var route = Assert.Single(declarations, declaration => declaration.CapabilityId == read);
            Assert.Null(route.ParentCapabilityId);
            Assert.Equal([read], route.RequiredPermissions);
            Assert.True(route.ImplementationAvailable);
            foreach (var command in new[] { prepare, publish })
            {
                var control = Assert.Single(declarations, declaration => declaration.CapabilityId == command);
                Assert.Equal(read, control.ParentCapabilityId);
                Assert.Equal(route.RoutePath, control.RoutePath);
                Assert.Equal([read, command], control.RequiredPermissions);
            }
        }

        Assert.DoesNotContain(declarations, declaration => declaration.CapabilityId == ManufacturingParameterProfileActionsV1.Approve);
        Assert.DoesNotContain(declarations, declaration => declaration.CapabilityId == ManufacturingToleranceProfileActionsV1.Approve);
    }
}
