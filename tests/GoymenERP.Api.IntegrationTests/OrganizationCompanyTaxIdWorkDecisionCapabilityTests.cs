using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.WorkInbox;

namespace GoymenERP.Api.IntegrationTests;

// Real candidate registry, without replacing the normal-host reviewed revision or creating any grant.
public sealed class OrganizationCompanyTaxIdWorkDecisionCapabilityTests
{
    [Fact]
    public void Purpose_is_standalone_companywide_existing_approve_without_native_read_and_keeps_native_screen()
    {
        var provider = new OrganizationShellCapabilityProvider();
        var registry = new ShellCapabilityRegistry([provider],
            new ShellCapabilityAuthorityCatalog([ShellCapabilityRegistry.ComputeCandidateRevision([provider])]));
        Assert.Equal("1.6", provider.ContractVersion);
        Assert.Equal(14, registry.Registrations.Count);
        var purpose = Assert.Single(registry.Registrations, x => x.Declaration.CapabilityId == OrganizationShellCapabilityProvider.CompanyTaxIdWorkDecisionCapabilityId).Declaration;
        Assert.Equal("organization.company-tax-id-change.work-decision", purpose.CapabilityId);
        Assert.Equal("organization-company-tax-id-change-work-decision", purpose.RouteId);
        Assert.Equal("/onaylarim/organization.company-tax-id-change.v1/:approvalId", purpose.RoutePath);
        Assert.Equal("ORG-COMPANY-TAX-ID-CHANGE-WORK-DECISION", purpose.ScreenId);
        Assert.True(purpose.ImplementationAvailable); Assert.Null(purpose.ParentCapabilityId);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, purpose.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, purpose.ScopeRequirement);
        Assert.Equal(ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, purpose.RoutePrecedence);
        Assert.Equal([OrganizationCompanyTaxIdPermissionsV1.Approve], purpose.RequiredPermissions);
        var native = Assert.Single(registry.Registrations, x => x.Declaration.CapabilityId == OrganizationShellCapabilityProvider.CompanyTaxIdCapabilityId).Declaration;
        Assert.Equal("/ayarlar/organizasyon/sirket-vergi-no", native.RoutePath);
        Assert.Equal([ErpPermissions.OrganizationContextRead], native.RequiredPermissions);
        Assert.Equal(native.RoutePath, OrganizationCompanyTaxIdChangeWorkInboxSourceV1.ScreenPath);
    }
}
