using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Shell;

namespace GoymenERP.Logistics.UnitTests;

public sealed class LogisticsCustomerReturnPurposeCapabilityTests
{
    [Fact]
    public void Return_weighing_control_is_paired_and_uses_existing_weighing_authority_without_stock_or_decision_authority()
    {
        var control = Assert.Single(new LogisticsShellCapabilityProvider().GetDeclarations(),
            item => item.CapabilityId == LogisticsShellCapabilityProvider.CustomerReturnsWeighingRecordCapabilityId);
        Assert.Equal("logistics.customer-returns.record-weighing", control.CapabilityId);
        Assert.True(control.ImplementationAvailable);
        Assert.Equal(LogisticsShellCapabilityProvider.DetailCapabilityId, control.ParentCapabilityId);
        Assert.Equal(LogisticsShellCapabilityProvider.DetailRoutePath, control.RoutePath);
        Assert.Equal(LogisticsShellCapabilityProvider.DetailScreenId, control.ScreenId);
        Assert.Equal(ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead, control.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, control.ScopeRequirement);
        Assert.Equal([LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.CustomerReturnsRead,
            LogisticsPermissionManifestV1.WeighingsRecord], control.RequiredPermissions);
        Assert.DoesNotContain(LogisticsPermissionManifestV1.CustomerReturnsDecision, control.RequiredPermissions);
        Assert.DoesNotContain(ErpPermissions.InventoryPhysicalAllocationWrite, control.RequiredPermissions);
    }

    [Fact]
    public void Customer_return_purpose_is_published_and_uses_only_the_company_wide_native_decision_permission()
    {
        var provider = new LogisticsShellCapabilityProvider();
        Assert.Equal("1.17", provider.ContractVersion);
        var purpose = Assert.Single(provider.GetDeclarations(), item => item.CapabilityId == "logistics.customer-return.work-decision");
        Assert.Equal("/onaylarim/logistics.customer-return.v1/:approvalId", purpose.RoutePath);
        Assert.Equal("L-CUSTOMER-RETURN-WORK-DECISION", purpose.ScreenId);
        Assert.Null(purpose.ParentCapabilityId); Assert.True(purpose.ImplementationAvailable);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, purpose.ScopeRequirement);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, purpose.ContextRequirement);
        Assert.Equal(ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, purpose.RoutePrecedence);
        Assert.Equal([LogisticsPermissionManifestV1.CustomerReturnsDecision], purpose.RequiredPermissions);
        var native = Assert.Single(provider.GetDeclarations(), item => item.CapabilityId == LogisticsShellCapabilityProvider.CustomerReturnsApproveCapabilityId);
        Assert.True(native.ImplementationAvailable);
        Assert.Equal(LogisticsShellCapabilityProvider.DetailCapabilityId, native.ParentCapabilityId);
        Assert.Equal([LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.CustomerReturnsRead,
            LogisticsPermissionManifestV1.CustomerReturnsDecision, ErpPermissions.InventoryPhysicalAllocationWrite], native.RequiredPermissions);
    }
}
