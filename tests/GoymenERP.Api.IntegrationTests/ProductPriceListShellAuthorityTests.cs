using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Product.Application.Contracts;
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

/// <summary>AT-018: the normal host with the price-list provider (PRODUCT-PRICE-LISTS 1.2) beside every published module.</summary>
public sealed class ProductPriceListShellAuthorityTests
{
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
            new ProductPriceListShellCapabilityProvider(),
        ];
        var actual = ShellCapabilityRegistry.ComputeCandidateRevision(providers);

        Assert.True(string.Equals(
                ShellCapabilityRegistryAuthorities.FullNormalHostWithProductPriceListsAndSalesReports,
                actual,
                StringComparison.Ordinal),
            $"Price-list normal-host authority drifted. Actual: {actual}");
    }
    [Fact]
    public void Package_standard_purpose_uses_company_authority_beside_the_unchanged_global_Product_routes()
    {
        var provider = new ProductPriceListShellCapabilityProvider();
        Assert.Equal("1.2", provider.ContractVersion);
        IShellCapabilityProvider[] providers = [new ProductShellCapabilityProvider(), provider];
        var registry = new ShellCapabilityRegistry(providers,
            new ShellCapabilityAuthorityCatalog([ShellCapabilityRegistry.ComputeCandidateRevision(providers)]));
        Assert.Equal(8, registry.Registrations.Count(row => row.Owner == ProductShellCapabilityProvider.OwnerId));
        Assert.All(registry.Registrations.Where(row => row.Owner == ProductShellCapabilityProvider.OwnerId),
            row => Assert.NotNull(row.Declaration.GlobalProductOperation));
        Assert.Equal(new[] { "price-lists.create", "price-lists.detail", "price-lists.edit", "price-lists.list",
            "price-lists.package-standard-version.work-decision", "price-lists.price-list-version.work-decision", "price-lists.publish" },
            provider.GetDeclarations().Select(row => row.CapabilityId).Order(StringComparer.Ordinal));
        var purpose = Assert.Single(provider.GetDeclarations(), row => row.CapabilityId == ProductPriceListShellCapabilityProvider.PackageStandardWorkCapabilityId);
        Assert.Equal("/onaylarim/product.package-standard-version.v1/:approvalId", purpose.RoutePath);
        Assert.Equal(purpose.CapabilityId, purpose.RouteId);
        Assert.Equal("PRODUCT-PACKAGE-STANDARD-WORK-DECISION", purpose.ScreenId);
        Assert.Equal(ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, purpose.RoutePrecedence);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, purpose.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, purpose.ScopeRequirement);
        Assert.Equal([ProductPackageStandardPermissionsV3.Approve], purpose.RequiredPermissions);
        Assert.Null(purpose.ParentCapabilityId); Assert.Null(purpose.GlobalProductOperation); Assert.True(purpose.ImplementationAvailable);
        Assert.Equal("/fiyat-listeleri/:priceListId", Assert.Single(provider.GetDeclarations(), row => row.CapabilityId == "price-lists.detail").RoutePath);
        Assert.Equal("/urun/:productId", Assert.Single(new ProductShellCapabilityProvider().GetDeclarations(), row => row.CapabilityId == "product.items.detail").RoutePath);
    }

    [Fact]
    public void Paired_price_publication_is_standalone_Read_and_Publish_company_wide_without_a_global_master_operation()
    {
        var provider = new ProductPriceListShellCapabilityProvider();
        var purpose = Assert.Single(provider.GetDeclarations(), row => row.CapabilityId == ProductPriceListShellCapabilityProvider.PriceListWorkCapabilityId);
        Assert.Equal("price-lists.price-list-version.work-decision", purpose.CapabilityId);
        Assert.Equal(purpose.CapabilityId, purpose.RouteId);
        Assert.Equal("/onaylarim/product.price-list-version.v1/:approvalId", purpose.RoutePath);
        Assert.Equal("PRODUCT-PRICE-LIST-WORK-DECISION", purpose.ScreenId);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, purpose.ScopeRequirement);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, purpose.ContextRequirement);
        Assert.Equal([ProductPriceListPermissionsV1.Read, ProductPriceListPermissionsV1.Publish], purpose.RequiredPermissions);
        Assert.Null(purpose.ParentCapabilityId); Assert.Null(purpose.GlobalProductOperation); Assert.True(purpose.ImplementationAvailable);
        Assert.Equal(8, new ProductShellCapabilityProvider().GetDeclarations().Count);
    }
}
