using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Finance.Infrastructure.Shell;
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
/// MFG-UI (#323), central: the normal host registers Manufacturing's staged provider beside Counterparty,
/// Organization, Product, published Sales and staged Finance; the disposable mfg-staged profile (D4) opens exactly Manufacturing's own
/// declarations beside the list-only C-01 set.
/// </summary>
public sealed class ManufacturingShellRegistrationTests
{
    [Fact]
    public void The_Manufacturing_authority_is_the_registry_revision_of_the_six_real_providers()
    {
        IShellCapabilityProvider[] providers =
        [
            new CounterpartyShellCapabilityProvider(), new OrganizationShellCapabilityProvider(),
            new ProductShellCapabilityProvider(), new SalesShellCapabilityProvider(),
            new FinanceShellCapabilityProvider(), new ManufacturingShellCapabilityProvider(),
        ];

        // On failure xUnit prints the actual revision: the constant is never guessed.
        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15OrganizationV10ProductV10PublishedSalesV10FinanceV10AndManufacturingV10,
            ShellCapabilityRegistry.ComputeCandidateRevision(providers));
    }

    [Fact]
    public void The_normal_host_with_Manufacturing_gets_its_authority_and_the_staged_screen_grants_nothing()
    {
        var services = NormalHost();

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15OrganizationV10ProductV10PublishedSalesV10FinanceV10AndManufacturingV10,
            authority);
        using var container = services.BuildServiceProvider();
        var registry = new ShellCapabilityRegistry(container.GetServices<IShellCapabilityProvider>(),
            new ShellCapabilityAuthorityCatalog([authority]));
        var manufacturing = registry.Registrations
            .Where(item => item.Owner == ManufacturingShellCapabilityProvider.OwnerId)
            .ToArray();
        Assert.Equal(2, manufacturing.Length);
        Assert.All(manufacturing, item => Assert.False(item.Declaration.ImplementationAvailable));
    }

    // Since Finance is host-wired (#325) the host always registers it: the earlier set without Finance
    // (#323's first row) is no reviewed set any more and keeps the Counterparty-only authority.
    [Fact]
    public void Manufacturing_without_Finance_is_not_a_reviewed_set()
    {
        var services = NormalHost();
        services.Remove(Assert.Single(services, item => item.ImplementationType == typeof(FinanceShellCapabilityProvider)));

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyListCreateLinkAndDetailV15, authority);
        using var container = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => new ShellCapabilityRegistry(
            container.GetServices<IShellCapabilityProvider>(), new ShellCapabilityAuthorityCatalog([authority])));
    }

    [Fact]
    public void Manufacturing_without_Sales_is_not_a_reviewed_set()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IShellCapabilityProvider, CounterpartyShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, OrganizationShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, ProductShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, ManufacturingShellCapabilityProvider>();

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyListCreateLinkAndDetailV15, authority);
        using var container = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => new ShellCapabilityRegistry(
            container.GetServices<IShellCapabilityProvider>(), new ShellCapabilityAuthorityCatalog([authority])));
    }

    [Fact]
    public void The_C01_envelope_removes_the_Manufacturing_provider_too()
    {
        var services = NormalHost();

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().AddInMemoryCollection(C01E2eShellCapabilityActivationTests.ExactEnvelope()).Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.C01DisposableListOnlyV1, authority);
        Assert.Equal(typeof(C01E2eShellCapabilityProvider),
            Assert.Single(services, item => item.ServiceType == typeof(IShellCapabilityProvider)).ImplementationType);
    }

    [Fact]
    public void The_mfg_staged_profile_opens_only_Manufacturings_own_declarations_beside_the_C01_list()
    {
        var services = NormalHost();
        var envelope = new Dictionary<string, string?>(C01E2eShellCapabilityActivationTests.ExactEnvelope())
        {
            [C01E2eShellCapabilityActivation.ProfileConfigurationKey] =
                C01E2eShellCapabilityActivation.ManufacturingOrderDetailProfile,
        };

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().AddInMemoryCollection(envelope).Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.C01DisposableManufacturingOrderDetailV1, authority);
        Assert.Equal(
            [typeof(C01E2eShellCapabilityProvider), typeof(C01E2eManufacturingOrderDetailShellCapabilityProvider)],
            services.Where(item => item.ServiceType == typeof(IShellCapabilityProvider))
                .Select(item => item.ImplementationType));
        using var container = services.BuildServiceProvider();
        var providers = container.GetServices<IShellCapabilityProvider>().ToArray();
        Assert.Equal(authority, ShellCapabilityRegistry.ComputeCandidateRevision(providers));
        var registry = new ShellCapabilityRegistry(providers, new ShellCapabilityAuthorityCatalog([authority]));

        // Same ids, route, access and parent as the owner's declarations; only availability differs.
        var owner = new ManufacturingShellCapabilityProvider().GetDeclarations()
            .ToDictionary(declaration => declaration.CapabilityId);
        var opened = registry.Registrations
            .Where(item => item.Owner == ManufacturingShellCapabilityProvider.OwnerId)
            .ToArray();
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
                                                              || item.Owner == ProductShellCapabilityProvider.OwnerId);
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
