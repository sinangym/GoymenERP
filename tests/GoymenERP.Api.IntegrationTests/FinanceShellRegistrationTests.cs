using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Finance.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using GoymenERP.Modules.Product.Infrastructure.Shell;
using GoymenERP.Modules.Sales.Infrastructure.Shell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// FIN-UI-1a (#325), central: the normal host registers Finance's staged provider beside Counterparty,
/// Organization, Product and Sales, and accepts exactly that set's reviewed revision.
/// </summary>
public sealed class FinanceShellRegistrationTests
{
    [Fact]
    public void The_Finance_authority_is_the_registry_revision_of_the_five_real_providers()
    {
        IShellCapabilityProvider[] providers =
        [
            new CounterpartyShellCapabilityProvider(), new OrganizationShellCapabilityProvider(),
            new ProductShellCapabilityProvider(), new SalesShellCapabilityProvider(),
            new FinanceShellCapabilityProvider(),
        ];

        // On failure xUnit prints the actual revision: the constant is never guessed.
        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15OrganizationV10ProductV10PublishedSalesV10AndFinanceV10,
            ShellCapabilityRegistry.ComputeCandidateRevision(providers));
    }

    [Fact]
    public void The_normal_host_with_Finance_gets_its_authority_and_the_staged_screens_grant_nothing()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IShellCapabilityProvider, CounterpartyShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, OrganizationShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, ProductShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, SalesShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, FinanceShellCapabilityProvider>();

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15OrganizationV10ProductV10PublishedSalesV10AndFinanceV10,
            authority);
        using var container = services.BuildServiceProvider();
        var registry = new ShellCapabilityRegistry(container.GetServices<IShellCapabilityProvider>(),
            new ShellCapabilityAuthorityCatalog([authority]));
        var finance = registry.Registrations
            .Where(item => item.Owner == FinanceShellCapabilityProvider.OwnerId)
            .ToArray();
        // Five routes and the approval control; staged until the fin01 acceptance and a separate change.
        Assert.Equal(6, finance.Length);
        Assert.All(finance, item => Assert.False(item.Declaration.ImplementationAvailable));
        // Three Sales screens plus the SALES-A1 approve control.
        Assert.Equal(4, registry.Registrations.Count(item => item.Owner == SalesShellCapabilityProvider.OwnerId));
    }

    [Fact]
    public void Finance_without_Sales_is_not_a_reviewed_set_and_keeps_the_Counterparty_only_authority()
    {
        // Every optional provider set the host may serve has its own reviewed row; an unreviewed set keeps the
        // Counterparty-only authority, which the registry then refuses for the providers it holds.
        var services = new ServiceCollection();
        services.AddSingleton<IShellCapabilityProvider, CounterpartyShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, OrganizationShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, ProductShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, FinanceShellCapabilityProvider>();

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyListCreateLinkAndDetailV15, authority);
        using var container = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => new ShellCapabilityRegistry(
            container.GetServices<IShellCapabilityProvider>(), new ShellCapabilityAuthorityCatalog([authority])));
    }

    [Fact]
    public void The_C01_envelope_removes_the_Finance_provider_too()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IShellCapabilityProvider, CounterpartyShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, SalesShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, FinanceShellCapabilityProvider>();

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
