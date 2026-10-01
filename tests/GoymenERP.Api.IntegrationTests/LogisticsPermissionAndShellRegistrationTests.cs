using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Finance.Infrastructure.Shell;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Shell;
using GoymenERP.Modules.Manufacturing.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using GoymenERP.Modules.Product.Infrastructure.Shell;
using GoymenERP.Modules.Sales.Infrastructure.Shell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// LOG-4a: the Logistics owner's permission names are registered centrally with the same strings (company-wide
/// only, not Critical, no grant; #66 5743488059), and nothing else is registered under <c>logistics.</c>; the
/// published Logistics shell provider joins the host set with its own reviewed normal-host authority.
/// </summary>
public sealed class LogisticsPermissionAndShellRegistrationTests
{
    [Theory]
    [InlineData(LogisticsPermissionManifestV1.InstructionsCreate, ErpPermissions.LogisticsLoadingInstructionsCreate)]
    [InlineData(LogisticsPermissionManifestV1.InstructionsRead, ErpPermissions.LogisticsLoadingInstructionsRead)]
    [InlineData(LogisticsPermissionManifestV1.LoadsRecord, ErpPermissions.LogisticsLoadsRecord)]
    [InlineData(LogisticsPermissionManifestV1.ShipmentsRecord, ErpPermissions.LogisticsShipmentsRecord)]
    [InlineData(LogisticsPermissionManifestV1.DeliveriesRecord, ErpPermissions.LogisticsDeliveriesRecord)]
    [InlineData(LogisticsPermissionManifestV1.WeighingsRecord, ErpPermissions.LogisticsWeighingsRecord)]
    [InlineData(LogisticsPermissionManifestV1.DispatchNotesCreate, ErpPermissions.LogisticsDispatchNotesCreate)]
    [InlineData(LogisticsPermissionManifestV1.DispatchNotesApprove, ErpPermissions.LogisticsDispatchNotesApprove)]
    [InlineData(LogisticsPermissionManifestV1.DispatchNotesCancel, ErpPermissions.LogisticsDispatchNotesCancel)]
    [InlineData(LogisticsPermissionManifestV1.OperationsSalesWorkRead, ErpPermissions.LogisticsOperationsSalesWorkRead)]
    [InlineData(LogisticsPermissionManifestV1.OperationsPurchaseWorkRead, ErpPermissions.LogisticsOperationsPurchaseWorkRead)]
    public void The_owner_name_is_the_registered_central_name(string ownerName, string centralName)
    {
        Assert.Equal(ownerName, centralName);
        Assert.True(ErpPermissions.IsKnown(ownerName));
        Assert.Single(ErpPermissions.All, known => known == ownerName);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ownerName));
        Assert.False(ErpPermissions.IsCritical(ownerName));
    }

    [Fact]
    public void Every_owner_name_is_registered_and_nothing_else_under_logistics()
    {
        Assert.Equal(LogisticsPermissionManifestV1.Names.Order(StringComparer.Ordinal),
            ErpPermissions.All.Where(name => name.StartsWith("logistics.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_Logistics_authority_is_the_registry_revision_of_the_seven_real_providers()
    {
        IShellCapabilityProvider[] providers =
        [
            new CounterpartyShellCapabilityProvider(), new OrganizationShellCapabilityProvider(),
            new ProductShellCapabilityProvider(), new SalesShellCapabilityProvider(),
            new FinanceShellCapabilityProvider(), new ManufacturingShellCapabilityProvider(),
            new LogisticsShellCapabilityProvider(),
        ];

        var actual = ShellCapabilityRegistry.ComputeCandidateRevision(providers);
        Assert.True(
            string.Equals(ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV15PublishedFinanceV15PublishedManufacturingV15AndPublishedLogisticsV12, actual,
                StringComparison.Ordinal),
            $"Logistics normal-host authority drifted. Actual: {actual}");
    }

    [Fact]
    public void The_normal_host_with_Logistics_gets_its_authority_and_publishes_three_routes_and_two_permission_gated_actions()
    {
        var services = NormalHost();

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV15PublishedFinanceV15PublishedManufacturingV15AndPublishedLogisticsV12, authority);
        using var container = services.BuildServiceProvider();
        var registry = new ShellCapabilityRegistry(container.GetServices<IShellCapabilityProvider>(),
            new ShellCapabilityAuthorityCatalog([authority]));
        var logistics = registry.Registrations
            .Where(item => item.Owner == LogisticsShellCapabilityProvider.OwnerId)
            .ToArray();
        // The list, the new-instruction workspace and the detail are published; each still requires its owner permission.
        // AT-025: plus the operations work list's two separate sections (L-01 sales, L-09 purchase), each behind its own name.
        Assert.Equal(7, logistics.Length);
        Assert.All(logistics, item => Assert.True(item.Declaration.ImplementationAvailable));
        Assert.Equal(2, logistics.Count(item => item.Declaration.ParentCapabilityId == LogisticsShellCapabilityProvider.DetailCapabilityId));
    }

    [Fact]
    public void The_C01_envelope_removes_the_Logistics_provider_too()
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
