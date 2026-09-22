using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Finance.Infrastructure.Shell;
using GoymenERP.Modules.Logistics.Infrastructure.Shell;
using GoymenERP.Modules.Manufacturing.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using GoymenERP.Modules.Product.Infrastructure.Shell;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Shell;
using GoymenERP.Modules.Sales.Infrastructure.Shell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// PUR-4a: the Purchasing owner's permission names are registered centrally with the same strings (company-wide
/// only, not Critical, no grant; #66 5743488059), and nothing else is registered under <c>purchasing.</c>; the
/// staged Purchasing shell provider joins the host set with its own reviewed normal-host authority.
/// </summary>
public sealed class PurchasingPermissionAndShellRegistrationTests
{
    [Theory]
    [InlineData(PurchasingPermissionManifestV1.OrdersCreate, ErpPermissions.PurchasingOrdersCreate)]
    [InlineData(PurchasingPermissionManifestV1.OrdersRead, ErpPermissions.PurchasingOrdersRead)]
    [InlineData(PurchasingPermissionManifestV1.OrdersApprove, ErpPermissions.PurchasingOrdersApprove)]
    public void The_owner_name_is_the_registered_central_name(string ownerName, string centralName)
    {
        Assert.Equal(ownerName, centralName);
        Assert.True(ErpPermissions.IsKnown(ownerName));
        Assert.Single(ErpPermissions.All, known => known == ownerName);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ownerName));
        Assert.False(ErpPermissions.IsCritical(ownerName));
    }

    [Fact]
    public void Every_owner_name_is_registered_and_nothing_else_under_purchasing()
    {
        Assert.Equal(PurchasingPermissionManifestV1.Names.Order(StringComparer.Ordinal),
            ErpPermissions.All.Where(name => name.StartsWith("purchasing.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_Purchasing_authority_is_the_registry_revision_of_the_eight_real_providers()
    {
        IShellCapabilityProvider[] providers =
        [
            new CounterpartyShellCapabilityProvider(), new OrganizationShellCapabilityProvider(),
            new ProductShellCapabilityProvider(), new SalesShellCapabilityProvider(),
            new FinanceShellCapabilityProvider(), new ManufacturingShellCapabilityProvider(),
            new LogisticsShellCapabilityProvider(), new PurchasingShellCapabilityProvider(),
        ];

        var actual = ShellCapabilityRegistry.ComputeCandidateRevision(providers);
        Assert.True(
            string.Equals(ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV11ProductV10PublishedSalesV11PublishedFinanceV11PublishedManufacturingV10PublishedLogisticsV10AndPurchasingV10, actual,
                StringComparison.Ordinal),
            $"Purchasing normal-host authority drifted. Actual: {actual}");
    }

    [Fact]
    public void The_normal_host_with_Purchasing_gets_its_authority_and_the_staged_screens_grant_nothing()
    {
        var services = NormalHost();

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV11ProductV10PublishedSalesV11PublishedFinanceV11PublishedManufacturingV10PublishedLogisticsV10AndPurchasingV10, authority);
        using var container = services.BuildServiceProvider();
        var registry = new ShellCapabilityRegistry(container.GetServices<IShellCapabilityProvider>(),
            new ShellCapabilityAuthorityCatalog([authority]));
        var purchasing = registry.Registrations
            .Where(item => item.Owner == PurchasingShellCapabilityProvider.OwnerId)
            .ToArray();
        // The order list, the new-order form and the detail; staged until their frontend and its suite.
        Assert.Equal(3, purchasing.Length);
        Assert.All(purchasing, item => Assert.False(item.Declaration.ImplementationAvailable));
    }

    [Fact]
    public void The_C01_envelope_removes_the_Purchasing_provider_too()
    {
        var services = NormalHost();

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().AddInMemoryCollection(C01E2eShellCapabilityActivationTests.ExactEnvelope()).Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.C01DisposableListOnlyV1, authority);
        Assert.Equal(typeof(C01E2eShellCapabilityProvider),
            Assert.Single(services, item => item.ServiceType == typeof(IShellCapabilityProvider)).ImplementationType);
    }

    private static ServiceCollection NormalHost()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IShellCapabilityProvider, CounterpartyShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, OrganizationShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, ProductShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, SalesShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, FinanceShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, ManufacturingShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, LogisticsShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, PurchasingShellCapabilityProvider>();
        return services;
    }

    private sealed class TestingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
