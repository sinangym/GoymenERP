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
        Assert.Equal(3, registry.Registrations.Count(item => item.Owner == SalesShellCapabilityProvider.OwnerId));
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

    // FIN-UI-1b (#332): the disposable fin-staged profile opens exactly Finance's own six declarations
    // beside the list-only C-01 set; the constant is pinned against ComputeCandidateRevision.
    [Fact]
    public void The_fin_staged_profile_opens_only_Finances_own_declarations_beside_the_C01_list()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IShellCapabilityProvider, CounterpartyShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, OrganizationShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, ProductShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, SalesShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, FinanceShellCapabilityProvider>();
        var envelope = new Dictionary<string, string?>(C01E2eShellCapabilityActivationTests.ExactEnvelope())
        {
            [C01E2eShellCapabilityActivation.ProfileConfigurationKey] =
                C01E2eShellCapabilityActivation.FinanceBankAccountsAndDraftsProfile,
        };

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().AddInMemoryCollection(envelope).Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.C01DisposableFinanceBankAccountsAndDraftsV1, authority);
        Assert.Equal(
            [typeof(C01E2eShellCapabilityProvider), typeof(C01E2eFinanceBankAccountsAndDraftsShellCapabilityProvider)],
            services.Where(item => item.ServiceType == typeof(IShellCapabilityProvider))
                .Select(item => item.ImplementationType));
        using var container = services.BuildServiceProvider();
        var providers = container.GetServices<IShellCapabilityProvider>().ToArray();
        // On failure xUnit prints the actual revision: the constant is never guessed.
        Assert.Equal(authority, ShellCapabilityRegistry.ComputeCandidateRevision(providers));
        var registry = new ShellCapabilityRegistry(providers, new ShellCapabilityAuthorityCatalog([authority]));

        // Same ids, routes, access and parent as the owner's declarations; only availability differs.
        var owner = new FinanceShellCapabilityProvider().GetDeclarations()
            .ToDictionary(declaration => declaration.CapabilityId);
        var opened = registry.Registrations
            .Where(item => item.Owner == FinanceShellCapabilityProvider.OwnerId)
            .ToArray();
        Assert.Equal(6, opened.Length);
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
                                                              || item.Owner == ProductShellCapabilityProvider.OwnerId
                                                              || item.Owner == OrganizationShellCapabilityProvider.OwnerId);
    }

    private sealed class TestingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
