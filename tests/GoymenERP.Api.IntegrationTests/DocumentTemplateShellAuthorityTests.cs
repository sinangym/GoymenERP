using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Settings.Infrastructure.Shell;

namespace GoymenERP.Api.IntegrationTests;

public sealed class DocumentTemplateShellAuthorityTests
{
    [Fact]
    public void Company_branding_and_each_module_duty_are_separately_registered_without_grants()
    {
        var declarations = new SettingsShellCapabilityProvider().GetDeclarations()
            .Where(item => item.CapabilityId.StartsWith("documents.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(7, declarations.Length);
        Assert.Equal(DocumentBrandingPermissionsV1.Names.Order(StringComparer.Ordinal),
            declarations.Select(item => item.CapabilityId).Order(StringComparer.Ordinal));
        foreach (var item in declarations)
        {
            Assert.Equal(item.CapabilityId, Assert.Single(item.RequiredPermissions));
            Assert.True(ErpPermissions.IsKnown(item.CapabilityId));
            Assert.True(ErpPermissions.RequiresCompanyWideScope(item.CapabilityId));
            Assert.False(ErpPermissions.IsCritical(item.CapabilityId));
            Assert.False(item.ImplementationAvailable);
        }
        Assert.Equal(4, declarations.Count(item => item.ParentCapabilityId is null));
        foreach (var module in Enum.GetValues<DocumentTemplateModuleV1>())
        {
            var prepare = Assert.Single(declarations, item => item.CapabilityId == DocumentBrandingPermissionsV1.Prepare(module));
            var publish = Assert.Single(declarations, item => item.CapabilityId == DocumentBrandingPermissionsV1.Publish(module));
            Assert.Null(prepare.ParentCapabilityId);
            Assert.Equal(prepare.CapabilityId, publish.ParentCapabilityId);
            Assert.Equal((prepare.RouteId, prepare.RoutePath, prepare.ScreenId), (publish.RouteId, publish.RoutePath, publish.ScreenId));
            Assert.DoesNotContain(prepare.CapabilityId, publish.RequiredPermissions);
        }
    }
}
