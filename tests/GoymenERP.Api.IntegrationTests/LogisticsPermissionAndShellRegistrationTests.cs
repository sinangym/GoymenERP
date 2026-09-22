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
/// staged Logistics shell provider joins the host set with its own reviewed normal-host authority.
/// </summary>
public sealed class LogisticsPermissionAndShellRegistrationTests
{
    [Theory]
    [InlineData(LogisticsPermissionManifestV1.InstructionsCreate, ErpPermissions.LogisticsLoadingInstructionsCreate)]
    [InlineData(LogisticsPermissionManifestV1.InstructionsRead, ErpPermissions.LogisticsLoadingInstructionsRead)]
    [InlineData(LogisticsPermissionManifestV1.LoadsRecord, ErpPermissions.LogisticsLoadsRecord)]
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
            string.Equals(ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV11ProductV10PublishedSalesV11PublishedFinanceV11PublishedManufacturingV10AndLogisticsV10, actual,
                StringComparison.Ordinal),
            $"Logistics normal-host authority drifted. Actual: {actual}");
    }

    [Fact]
    public void The_normal_host_with_Logistics_gets_its_authority_and_the_staged_screens_grant_nothing()
    {
        var services = NormalHost();

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV11ProductV10PublishedSalesV11PublishedFinanceV11PublishedManufacturingV10AndLogisticsV10, authority);
        using var container = services.BuildServiceProvider();
        var registry = new ShellCapabilityRegistry(container.GetServices<IShellCapabilityProvider>(),
            new ShellCapabilityAuthorityCatalog([authority]));
        var logistics = registry.Registrations
            .Where(item => item.Owner == LogisticsShellCapabilityProvider.OwnerId)
            .ToArray();
        // The list, the new-instruction workspace and the detail; staged until LOG-4b's screens and their suite.
        Assert.Equal(3, logistics.Length);
        Assert.All(logistics, item => Assert.False(item.Declaration.ImplementationAvailable));
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

    // LOG-4b (log-staged): the disposable profile opens exactly Logistics's own three declarations beside the list-only
    // C-01 set; the constant is pinned against ComputeCandidateRevision.
    [Fact]
    public void The_log_staged_profile_opens_only_Logisticss_own_declarations_beside_the_C01_list()
    {
        var services = NormalHost();
        var envelope = new Dictionary<string, string?>(C01E2eShellCapabilityActivationTests.ExactEnvelope())
        {
            [C01E2eShellCapabilityActivation.ProfileConfigurationKey] =
                C01E2eShellCapabilityActivation.LogisticsInstructionsProfile,
        };

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().AddInMemoryCollection(envelope).Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.C01DisposableLogisticsInstructionsV1, authority);
        Assert.Equal(
            [typeof(C01E2eShellCapabilityProvider), typeof(C01E2eLogisticsInstructionsShellCapabilityProvider)],
            services.Where(item => item.ServiceType == typeof(IShellCapabilityProvider))
                .Select(item => item.ImplementationType));
        using var container = services.BuildServiceProvider();
        var providers = container.GetServices<IShellCapabilityProvider>().ToArray();
        // On failure xUnit prints the actual revision: the constant is never guessed.
        Assert.Equal(authority, ShellCapabilityRegistry.ComputeCandidateRevision(providers));
        var registry = new ShellCapabilityRegistry(providers, new ShellCapabilityAuthorityCatalog([authority]));

        // Same ids, routes, access as the owner's declarations; only availability differs.
        var owner = new LogisticsShellCapabilityProvider().GetDeclarations()
            .ToDictionary(declaration => declaration.CapabilityId);
        var opened = registry.Registrations
            .Where(item => item.Owner == LogisticsShellCapabilityProvider.OwnerId)
            .ToArray();
        Assert.Equal(3, opened.Length);
        Assert.Equal(owner.Keys.Order(), opened.Select(item => item.Declaration.CapabilityId).Order());
        Assert.All(opened, item =>
        {
            var expected = owner[item.Declaration.CapabilityId];
            var actual = item.Declaration;
            Assert.True(actual.ImplementationAvailable);
            Assert.Equal(
                (expected.RouteId, expected.RoutePath, expected.ScreenId, expected.RoutePrecedence,
                    expected.ContextRequirement, expected.ScopeRequirement, expected.ParentCapabilityId),
                (actual.RouteId, actual.RoutePath, actual.ScreenId, actual.RoutePrecedence,
                    actual.ContextRequirement, actual.ScopeRequirement, actual.ParentCapabilityId));
            Assert.Equal(expected.RequiredPermissions.Order(), actual.RequiredPermissions.Order());
        });
        Assert.DoesNotContain(registry.Registrations, item => item.Owner == SalesShellCapabilityProvider.OwnerId
                                                              || item.Owner == ManufacturingShellCapabilityProvider.OwnerId);
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
