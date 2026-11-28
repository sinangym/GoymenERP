using GoymenERP.Api.Shell;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Accounting.Infrastructure.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Finance.Infrastructure.Shell;
using GoymenERP.Modules.Identity.Infrastructure.Shell;
using GoymenERP.Modules.Inventory.Infrastructure.Shell;
using GoymenERP.Modules.Logistics.Infrastructure.Shell;
using GoymenERP.Modules.Manufacturing.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using GoymenERP.Modules.Product.Infrastructure.Shell;
using GoymenERP.Modules.Purchasing.Infrastructure.Shell;
using GoymenERP.Modules.Sales.Infrastructure.Shell;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>SY-01/SY-02 and UI-K02 (SY-07 audit log): the normal host with Identity 1.1 beside every published module.</summary>
public sealed class IdentityShellAuthorityTests
{
    [Fact]
    public void Access_grant_purpose_is_a_standalone_company_wide_approve_declaration_in_the_real_candidate_registry()
    {
        var provider = new IdentityShellCapabilityProvider();
        var registry = new ShellCapabilityRegistry([provider],
            new ShellCapabilityAuthorityCatalog([ShellCapabilityRegistry.ComputeCandidateRevision([provider])]));
        Assert.Equal(7, registry.Registrations.Count);
        var purpose = Assert.Single(registry.Registrations, row => row.Declaration.CapabilityId == IdentityShellCapabilityProvider.AccessGrantWorkDecisionCapabilityId).Declaration;
        Assert.Equal("1.4", provider.ContractVersion);
        Assert.Equal("identity-access-grant-request-work-decision", purpose.RouteId);
        Assert.Equal("/onaylarim/identity.access-grant-request.v1/:approvalId", purpose.RoutePath);
        Assert.Equal("SY-ACCESS-GRANT-WORK-DECISION", purpose.ScreenId);
        Assert.Null(purpose.ParentCapabilityId); Assert.True(purpose.ImplementationAvailable);
        Assert.Equal(ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, purpose.RoutePrecedence);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, purpose.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, purpose.ScopeRequirement);
        Assert.Equal([IdentityPermissionManifestV1.AccessApprove], purpose.RequiredPermissions);
        var nativeControl = Assert.Single(registry.Registrations, row => row.Declaration.CapabilityId == IdentityPermissionManifestV1.AccessApprove).Declaration;
        Assert.Equal(IdentityShellCapabilityProvider.AccessListCapabilityId, nativeControl.ParentCapabilityId);
        Assert.Equal([IdentityPermissionManifestV1.AssignmentsRead, IdentityPermissionManifestV1.AccessApprove], nativeControl.RequiredPermissions);
    }

    [Fact]
    public void Normal_host_revision_matches_the_exact_integrated_provider_tuple()
    {
        IShellCapabilityProvider[] providers =
        [
            new CounterpartyShellCapabilityProvider(),
            new OrganizationShellCapabilityProvider(),
            new ProductShellCapabilityProvider(),
            new SalesShellCapabilityProvider(),
            new FinanceShellCapabilityProvider(),
            new ManufacturingShellCapabilityProvider(),
            new LogisticsShellCapabilityProvider(),
            new PurchasingShellCapabilityProvider(),
            new AccountingShellCapabilityProvider(),
            new InventoryShellCapabilityProvider(),
            new IdentityShellCapabilityProvider(),
        ];
        var actual = ShellCapabilityRegistry.ComputeCandidateRevision(providers);

        Assert.True(string.Equals(
                ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV21PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV13PublishedPurchasingV14PublishedAccountingV13PublishedInventoryV17AndPublishedIdentityV10,
                actual,
                StringComparison.Ordinal),
            $"Identity normal-host authority drifted. Actual: {actual}");
    }
}
