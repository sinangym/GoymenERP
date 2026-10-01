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
/// MFG-UI (#323), central: the normal host registers Manufacturing's provider beside Counterparty,
/// Organization, Product, published Sales and published Finance. MFG-UI-F: the order detail and its facts
/// control are published (availability only), and the disposable mfg-staged profile (D4) is retired.
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

        var actual = ShellCapabilityRegistry.ComputeCandidateRevision(providers);
        Assert.True(
            string.Equals(ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV15PublishedFinanceV14AndPublishedManufacturingV15, actual,
                StringComparison.Ordinal),
            $"Manufacturing normal-host authority drifted. Actual: {actual}");
    }

    [Fact]
    public void The_normal_host_with_Manufacturing_gets_its_authority_and_serves_the_published_screen()
    {
        var services = NormalHost();

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV15PublishedFinanceV14AndPublishedManufacturingV15,
            authority);
        using var container = services.BuildServiceProvider();
        var registry = new ShellCapabilityRegistry(container.GetServices<IShellCapabilityProvider>(),
            new ShellCapabilityAuthorityCatalog([authority]));
        var manufacturing = registry.Registrations
            .Where(item => item.Owner == ManufacturingShellCapabilityProvider.OwnerId)
            .ToArray();
        // The order detail, its three controls (facts, normal close and reasoned exception approval) and the MFG-02 order
        // list, published, and (MFG-PLAN-LIST, 1.3) the saved-plan list. A declaration still grants nothing: each
        // route's service measures its own Manufacturing permission afresh, and the close measures its own name on
        // top of the read. MFG-PLAN-EDIT (1.4) adds the plan-detail edit control (plans.read + plans.write).
        Assert.Equal(11, manufacturing.Length);
        string[] expected =
        [
            "manufacturing.orders.close", "manufacturing.orders.close-exception-approve",
            "manufacturing.orders.detail", "manufacturing.orders.facts",
            "manufacturing.orders.list",
            "manufacturing.plans.approve", "manufacturing.plans.create", "manufacturing.plans.detail",
            "manufacturing.plans.edit", "manufacturing.plans.list", "manufacturing.plans.order-create",
        ];
        Assert.Equal(expected,
            manufacturing.Select(item => item.Declaration.CapabilityId).Order(StringComparer.Ordinal).ToArray());
        Assert.All(manufacturing, item => Assert.True(item.Declaration.ImplementationAvailable));
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

        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetail, authority);
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

        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetail, authority);
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

    // MFG-UI-F: the disposable mfg-staged profile ("manufacturing-order-detail", #323 D4) is retired. Its value
    // is no profile any more: inside the C-01 envelope it fails closed at startup, as any unknown one.
    [Fact]
    public void The_retired_mfg_staged_profile_value_fails_closed()
    {
        var services = NormalHost();
        var envelope = new Dictionary<string, string?>(C01E2eShellCapabilityActivationTests.ExactEnvelope())
        {
            [C01E2eShellCapabilityActivation.ProfileConfigurationKey] = "manufacturing-order-detail",
        };

        Assert.Throws<InvalidOperationException>(() => C01E2eShellCapabilityActivation.Configure(services,
            new TestingEnvironment(), new ConfigurationBuilder().AddInMemoryCollection(envelope).Build()));
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
