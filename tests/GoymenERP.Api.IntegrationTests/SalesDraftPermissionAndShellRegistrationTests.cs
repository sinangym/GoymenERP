using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using GoymenERP.Modules.Product.Infrastructure.Shell;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Shell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SALES-API (#321): the Sales owner's permission names are registered centrally with the same
/// strings (company-wide only, not Critical, no grant), and the staged Sales shell provider joins the
/// module table with its own reviewed normal-host authority.
/// </summary>
public sealed class SalesDraftPermissionAndShellRegistrationTests
{
    [Theory]
    [InlineData(SalesPermissionManifestV1.DraftsCreate, ErpPermissions.SalesDraftsCreate)]
    [InlineData(SalesPermissionManifestV1.DraftsRead, ErpPermissions.SalesDraftsRead)]
    // SALES-A1: the approval name. Company-wide, not Critical (the reason is on ErpPermissions.SalesOrdersApprove).
    [InlineData(SalesPermissionManifestV1.OrdersApprove, ErpPermissions.SalesOrdersApprove)]
    // SALES-P: the price/discount write. Its own name, so drafts.create does not confer it; company-wide
    // and not Critical (the reason is on ErpPermissions.SalesOrdersPriceWrite). The row matters because the
    // set pin below would accept the name registered with the WRONG scope or flagged Critical.
    [InlineData(SalesPermissionManifestV1.OrdersPriceWrite, ErpPermissions.SalesOrdersPriceWrite)]
    // SALES-P2: the price/discount READ. Its own name because drafts.read already shows the row — without a
    // second name there is no way to show the line and withhold its price. Company-wide is not decoration
    // here: the module's disclosure check requires a grant that IsCompanyWide, so a site-scoped grant would
    // be accepted at grant time and then never disclose; RequiresCompanyWideScope below is what stops the
    // catalogue accepting what the code will not honour. Not Critical (the reason is on
    // ErpPermissions.SalesOrdersPriceRead).
    [InlineData(SalesPermissionManifestV1.OrdersPriceRead, ErpPermissions.SalesOrdersPriceRead)]
    // DEC-SAL-003: the delivery-date update. Its own name, given to the planning/shipping role; create, approve and the
    // price names do not confer it. Company-wide and not Critical (the reason is on
    // ErpPermissions.SalesOrdersDeliveryScheduleWrite).
    [InlineData(SalesPermissionManifestV1.OrdersDeliveryScheduleWrite, ErpPermissions.SalesOrdersDeliveryScheduleWrite)]
    // DEC-SAL-005: cancelling an approved order. Its own name, company-wide and not Critical (the reason is on
    // ErpPermissions.SalesOrdersCancel).
    [InlineData(SalesPermissionManifestV1.OrdersCancel, ErpPermissions.SalesOrdersCancel)]
    // DEC-SAL-002: the quantity revision of an approved order. Its own name, company-wide and not Critical (the reason is
    // on ErpPermissions.SalesOrdersQuantityWrite).
    [InlineData(SalesPermissionManifestV1.OrdersQuantityWrite, ErpPermissions.SalesOrdersQuantityWrite)]
    // Q9 customer acceptance: registered company-wide, not Critical and not granted by this catalogue.
    [InlineData(SalesPermissionManifestV1.OrdersCustomerAcceptanceRecord, ErpPermissions.SalesOrdersCustomerAcceptanceRecord)]
    public void The_owner_name_is_the_registered_central_name(string ownerName, string centralName)
    {
        Assert.Equal(ownerName, centralName);
        Assert.True(ErpPermissions.IsKnown(ownerName));
        Assert.Single(ErpPermissions.All, known => known == ownerName);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ownerName));
        Assert.False(ErpPermissions.IsCritical(ownerName));
    }

    [Fact]
    public void Every_owner_name_is_registered_and_nothing_else_under_sales()
    {
        Assert.Equal(SalesPermissionManifestV1.Names.Order(StringComparer.Ordinal),
            ErpPermissions.All.Where(name => name.StartsWith("sales.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_Sales_authority_is_the_registry_revision_of_the_four_real_providers()
    {
        IShellCapabilityProvider[] providers =
        [
            new CounterpartyShellCapabilityProvider(), new OrganizationShellCapabilityProvider(),
            new ProductShellCapabilityProvider(), new SalesShellCapabilityProvider(),
        ];

        // The complete value is part of the failure so the accepted revision is never guessed from a
        // console-truncated Assert.Equal diagnostic.
        var actual = ShellCapabilityRegistry.ComputeCandidateRevision(providers);
        Assert.True(
            string.Equals(
                ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12AndPublishedSalesV15,
                actual,
                StringComparison.Ordinal),
            $"Sales normal-host authority drifted. Actual: {actual}");
    }

    [Fact]
    public void The_normal_host_with_Sales_gets_its_authority_and_the_published_screens_are_available()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IShellCapabilityProvider, CounterpartyShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, OrganizationShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, ProductShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, SalesShellCapabilityProvider>();

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12AndPublishedSalesV15, authority);
        using var container = services.BuildServiceProvider();
        var registry = new ShellCapabilityRegistry(container.GetServices<IShellCapabilityProvider>(),
            new ShellCapabilityAuthorityCatalog([authority]));
        var sales = registry.Registrations.Where(item => item.Owner == SalesShellCapabilityProvider.OwnerId).ToArray();
        // Three screens plus the SALES-A1 approve, SALES-P commercial-revision, AT-021 floor-exception approval,
        // DEC-SAL-003 delivery-schedule, DEC-SAL-005 cancel and DEC-SAL-002 quantity-revision controls.
        Assert.Equal(10, sales.Length);
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId,
            Assert.Single(sales, item => item.Declaration.CapabilityId == SalesShellCapabilityProvider.QuantityWriteCapabilityId)
                .Declaration.ParentCapabilityId);
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId,
            Assert.Single(sales, item => item.Declaration.CapabilityId == SalesShellCapabilityProvider.CancelCapabilityId)
                .Declaration.ParentCapabilityId);
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId,
            Assert.Single(sales, item => item.Declaration.CapabilityId == SalesShellCapabilityProvider.ApproveCapabilityId)
                .Declaration.ParentCapabilityId);
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId,
            Assert.Single(sales, item => item.Declaration.CapabilityId == SalesShellCapabilityProvider.PriceWriteCapabilityId)
                .Declaration.ParentCapabilityId);
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId,
            Assert.Single(sales, item => item.Declaration.CapabilityId == SalesShellCapabilityProvider.PriceFloorExceptionApproveCapabilityId)
                .Declaration.ParentCapabilityId);
        Assert.Equal(SalesShellCapabilityProvider.DetailCapabilityId,
            Assert.Single(sales, item => item.Declaration.CapabilityId == SalesShellCapabilityProvider.CustomerAcceptanceRecordCapabilityId)
                .Declaration.ParentCapabilityId);
        // Published (S03-c, #328): available; each still opens only for an actor holding its Sales permission
        // in the context company (SalesShellCapabilitySqlServerRelationalAcceptanceTests).
        Assert.All(sales, item => Assert.True(item.Declaration.ImplementationAvailable));
    }

    [Fact]
    public void The_C01_envelope_removes_the_Sales_provider_too()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IShellCapabilityProvider, CounterpartyShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, SalesShellCapabilityProvider>();

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().AddInMemoryCollection(C01E2eShellCapabilityActivationTests.ExactEnvelope()).Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.C01DisposableListOnlyV1, authority);
        Assert.Equal(typeof(C01E2eShellCapabilityProvider),
            Assert.Single(services, item => item.ServiceType == typeof(IShellCapabilityProvider)).ImplementationType);
    }

    private sealed class TestingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
