using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

public sealed class C01E2eShellCapabilityActivationTests
{
    [Theory]
    [InlineData(10, ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV16PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV13PublishedPurchasingV14PublishedAccountingV13AndInventoryV18WithDormantAllocationDetail)]
    [InlineData(11, ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV16PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV13PublishedPurchasingV14PublishedAccountingV13InventoryV18WithDormantAllocationDetailAndPublishedIdentityV10)]
    [InlineData(12, ShellCapabilityRegistryAuthorities.FullNormalHostWithProductPriceListsSalesFloorApprovalAndInventoryV18WithDormantAllocationDetail)]
    public void Normal_host_selects_each_existing_inventory18_tuple_without_a_disposable_envelope(
        int providerCount,
        string expectedAuthority)
    {
        var services = new ServiceCollection();
        foreach (var provider in InventoryShellAuthorityTests.CreateNormalHostProviders(providerCount))
        {
            services.AddSingleton(typeof(IShellCapabilityProvider), provider.GetType());
        }

        var authority = C01E2eShellCapabilityActivation.Configure(
            services,
            new TestHostEnvironment("Testing"),
            Configuration([]));
        Assert.Equal(expectedAuthority, authority);
        using var serviceProvider = services.BuildServiceProvider();
        var providers = serviceProvider.GetServices<IShellCapabilityProvider>().ToArray();
        Assert.Equal(providerCount, providers.Length);
        var registry = new ShellCapabilityRegistry(providers, new ShellCapabilityAuthorityCatalog([authority]));
        Assert.Equal(authority, registry.Revision);
        var detail = Assert.Single(registry.Registrations,
            item => item.Declaration.CapabilityId == "inventory.allocations.detail");
        Assert.False(detail.Declaration.ImplementationAvailable);
    }

    [Fact]
    public void MissingActivationEnvelope_KeepsCanonicalProviderAndAuthority()
    {
        var services = CreateCanonicalServices();

        var authority = C01E2eShellCapabilityActivation.Configure(
            services,
            new TestHostEnvironment("Testing"),
            Configuration([]));

        Assert.Equal(
            ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetail,
            authority);
        var descriptor = Assert.Single(
            services,
            item => item.ServiceType == typeof(IShellCapabilityProvider));
        Assert.Equal(typeof(CounterpartyShellCapabilityProvider), descriptor.ImplementationType);
    }

    [Fact]
    public void ExactTestingEnvelope_UsesPinnedListOnlyProviderAndAuthority()
    {
        var services = CreateCanonicalServices();

        var authority = C01E2eShellCapabilityActivation.Configure(
            services,
            new TestHostEnvironment("Testing"),
            Configuration(ExactEnvelope()));

        Assert.Equal(ShellCapabilityRegistryAuthorities.C01DisposableListOnlyV1, authority);
        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<IShellCapabilityProvider>();
        Assert.IsType<C01E2eShellCapabilityProvider>(provider);
        var declarations = provider.GetDeclarations();
        Assert.Equal(4, declarations.Count);
        var list = Assert.Single(
            declarations,
            item => item.CapabilityId
                == CounterpartyShellCapabilityProvider.ListCapabilityId);
        Assert.True(list.ImplementationAvailable);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, list.ContextRequirement);
        Assert.All(
            declarations.Where(item => item.CapabilityId != list.CapabilityId),
            declaration => Assert.False(declaration.ImplementationAvailable));

        var registry = new ShellCapabilityRegistry(
            [provider],
            new ShellCapabilityAuthorityCatalog([authority]));
        Assert.Equal(authority, registry.Revision);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void NonTestingEnvironment_WithExactEnvelope_FailsStartup(string environmentName)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            C01E2eShellCapabilityActivation.Configure(
                CreateCanonicalServices(),
                new TestHostEnvironment(environmentName),
                Configuration(ExactEnvelope())));

        Assert.Contains("yalnız Testing", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidTestingEnvelopes))]
    public void PartialOrMalformedTestingEnvelope_FailsStartup(
        bool partialEnvelope, string key, string invalidValue)
    {
        var values = partialEnvelope ? new Dictionary<string, string?>() : ExactEnvelope();
        values[key] = invalidValue;

        Assert.Throws<InvalidOperationException>(() =>
            C01E2eShellCapabilityActivation.Configure(
                CreateCanonicalServices(),
                new TestHostEnvironment("Testing"),
                Configuration(values)));
    }

    [Fact]
    public void ExactEnvelope_WithUnexpectedProviderSet_FailsStartup()
    {
        var missingProvider = new ServiceCollection();
        var duplicateProvider = CreateCanonicalServices();
        duplicateProvider.AddSingleton<IShellCapabilityProvider, UnexpectedProvider>();

        Assert.Throws<InvalidOperationException>(() =>
            C01E2eShellCapabilityActivation.Configure(
                missingProvider,
                new TestHostEnvironment("Testing"),
                Configuration(ExactEnvelope())));
        Assert.Throws<InvalidOperationException>(() =>
            C01E2eShellCapabilityActivation.Configure(
                duplicateProvider,
                new TestHostEnvironment("Testing"),
                Configuration(ExactEnvelope())));
    }

    public static IEnumerable<object[]> InvalidTestingEnvelopes()
    {
        // xUnit v2 discovers primitive data rows separately. Construct the same
        // six dictionaries during execution so CI can verify exact case counts.
        yield return [true, C01E2eShellCapabilityActivation.EnableConfigurationKey, "true"];

        foreach (var (key, invalidValue) in new[]
                 {
                     (C01E2eShellCapabilityActivation.EnableConfigurationKey, "True"),
                     (C01E2eShellCapabilityActivation.DisposableConfirmationConfigurationKey, "confirmed"),
                     (C01E2eShellCapabilityActivation.TestedCommitConfigurationKey, new string('A', 40)),
                     (C01E2eShellCapabilityActivation.RunIdConfigurationKey, "run-100"),
                     (C01E2eShellCapabilityActivation.ComposeProjectConfigurationKey, "goymen-c01-101-1"),
                 })
        {
            yield return [false, key, invalidValue];
        }
    }

    internal static Dictionary<string, string?> ExactEnvelope() => new()
    {
        [C01E2eShellCapabilityActivation.EnableConfigurationKey] = "true",
        [C01E2eShellCapabilityActivation.DisposableConfirmationConfigurationKey] = "CONFIRMED",
        [C01E2eShellCapabilityActivation.TestedCommitConfigurationKey] = new string('a', 40),
        [C01E2eShellCapabilityActivation.RunIdConfigurationKey] = "100",
        [C01E2eShellCapabilityActivation.ComposeProjectConfigurationKey] = "goymen-c01-100-1",
    };

    private static ServiceCollection CreateCanonicalServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IShellCapabilityProvider, CounterpartyShellCapabilityProvider>();
        return services;
    }

    private static IConfiguration Configuration(
        IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class UnexpectedProvider : IShellCapabilityProvider
    {
        public string Owner => "TEST-UNEXPECTED";

        public string ContractVersion => "1.0";

        public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() => [];
    }
}
