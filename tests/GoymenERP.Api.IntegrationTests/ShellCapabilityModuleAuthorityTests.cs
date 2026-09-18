using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using GoymenERP.Modules.Product.Infrastructure.Shell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// The C-01 activation's module table (#316): each accepted set of module shell providers maps to
/// its own reviewed authority in the normal host, and the disposable C-01 envelope removes every
/// known module provider and stays list-only. A module that joins the shell is one row and one
/// constant; these cases are what a new row must keep true.
/// </summary>
public sealed class ShellCapabilityModuleAuthorityTests
{
    [Fact]
    public void The_Product_authority_is_the_registry_revision_of_the_three_real_providers()
    {
        IShellCapabilityProvider[] providers =
            [new CounterpartyShellCapabilityProvider(), new OrganizationShellCapabilityProvider(), new ProductShellCapabilityProvider()];

        // On failure xUnit prints the actual revision: the constant is never guessed.
        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15OrganizationV10AndProductV10,
            ShellCapabilityRegistry.ComputeCandidateRevision(providers));
    }

    [Theory]
    [InlineData("counterparty")]
    [InlineData("counterparty+organization")]
    [InlineData("counterparty+organization+product")]
    public void The_normal_host_gets_the_authority_of_exactly_the_providers_it_registers(string set)
    {
        var services = Services(set);

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().Build());

        using var container = services.BuildServiceProvider();
        var registry = new ShellCapabilityRegistry(container.GetServices<IShellCapabilityProvider>(),
            new ShellCapabilityAuthorityCatalog([authority]));
        Assert.Equal(authority, registry.Revision);
        Assert.Equal(set switch
        {
            "counterparty" => ShellCapabilityRegistryAuthorities.CounterpartyListCreateLinkAndDetailV15,
            "counterparty+organization" => ShellCapabilityRegistryAuthorities.CounterpartyV15AndOrganizationV10,
            _ => ShellCapabilityRegistryAuthorities.CounterpartyV15OrganizationV10AndProductV10,
        }, authority);
    }

    [Fact]
    public void An_unlisted_provider_set_fails_closed_at_the_registry()
    {
        // Product without Organization is no accepted set: the activation keeps the Counterparty-only
        // authority, and the registry refuses the providers it actually holds.
        var services = Services("counterparty+product");
        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().Build());

        using var container = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => new ShellCapabilityRegistry(
            container.GetServices<IShellCapabilityProvider>(), new ShellCapabilityAuthorityCatalog([authority])));
    }

    [Theory]
    [InlineData("counterparty+organization+product")]
    [InlineData("counterparty+product")]
    public void The_C01_envelope_removes_every_known_module_provider(string set)
    {
        var services = Services(set);

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(), Envelope());

        Assert.Equal(ShellCapabilityRegistryAuthorities.C01DisposableListOnlyV1, authority);
        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(IShellCapabilityProvider));
        Assert.Equal(typeof(C01E2eShellCapabilityProvider), descriptor.ImplementationType);
    }

    [Fact]
    public void The_C01_envelope_refuses_a_duplicated_module_provider()
    {
        var services = Services("counterparty+organization+product");
        services.AddSingleton<IShellCapabilityProvider, ProductShellCapabilityProvider>();

        Assert.Throws<InvalidOperationException>(() =>
            C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(), Envelope()));
    }

    private static ServiceCollection Services(string set)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IShellCapabilityProvider, CounterpartyShellCapabilityProvider>();
        if (set.Contains("organization", StringComparison.Ordinal))
        {
            services.AddSingleton<IShellCapabilityProvider, OrganizationShellCapabilityProvider>();
        }

        if (set.Contains("product", StringComparison.Ordinal))
        {
            services.AddSingleton<IShellCapabilityProvider, ProductShellCapabilityProvider>();
        }

        return services;
    }

    private static IConfiguration Envelope() =>
        new ConfigurationBuilder().AddInMemoryCollection(C01E2eShellCapabilityActivationTests.ExactEnvelope()).Build();

    private sealed class TestingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
