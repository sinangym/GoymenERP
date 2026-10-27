using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Finance.Infrastructure.Shell;
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
/// FIN-UI-1a (#325), central: the normal host registers Finance's provider beside Counterparty,
/// Organization, Product and Sales, and accepts exactly that set's reviewed revision. FIN-UI-1c: the six
/// Finance declarations are published (availability only), and the disposable fin-staged profile is retired.
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

        var actual = ShellCapabilityRegistry.ComputeCandidateRevision(providers);
        Assert.True(
            string.Equals(ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV15AndPublishedFinanceV15, actual,
                StringComparison.Ordinal),
            $"Finance normal-host authority drifted. Actual: {actual}");
    }

    [Fact]
    public void The_normal_host_with_Finance_gets_its_authority_and_serves_the_published_screens()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IShellCapabilityProvider, CounterpartyShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, OrganizationShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, ProductShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, SalesShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, FinanceShellCapabilityProvider>();

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV15AndPublishedFinanceV15,
            authority);
        using var container = services.BuildServiceProvider();
        var registry = new ShellCapabilityRegistry(container.GetServices<IShellCapabilityProvider>(),
            new ShellCapabilityAuthorityCatalog([authority]));
        var finance = registry.Registrations
            .Where(item => item.Owner == FinanceShellCapabilityProvider.OwnerId)
            .ToArray();
        // Fifteen routes plus six controls. A declaration still grants nothing:
        // each route or control service measures its own Finance permission afresh.
        Assert.Equal(21, finance.Length);
        Assert.All(finance, item => Assert.True(item.Declaration.ImplementationAvailable));
        // Sales 1.5: three routes plus seven detail controls. Pin identities, parents and authorities here instead of
        // accepting a blind declaration count: another control, parent or permission must receive its own review.
        var sales = registry.Registrations
            .Where(item => item.Owner == SalesShellCapabilityProvider.OwnerId)
            .Select(item => (
                item.Declaration.CapabilityId,
                item.Declaration.ParentCapabilityId,
                Permissions: string.Join("+", item.Declaration.RequiredPermissions)))
            .ToArray();
        Assert.Equal(
            [
                (SalesShellCapabilityProvider.CreateCapabilityId, (string?)null, SalesPermissionManifestV1.DraftsCreate),
                (SalesShellCapabilityProvider.DetailCapabilityId, null, SalesPermissionManifestV1.DraftsRead),
                (SalesShellCapabilityProvider.ListCapabilityId, null, SalesPermissionManifestV1.DraftsRead),
                (SalesShellCapabilityProvider.ApproveCapabilityId, SalesShellCapabilityProvider.DetailCapabilityId,
                    $"{SalesPermissionManifestV1.DraftsRead}+{SalesPermissionManifestV1.OrdersApprove}"),
                (SalesShellCapabilityProvider.CancelCapabilityId, SalesShellCapabilityProvider.DetailCapabilityId,
                    $"{SalesPermissionManifestV1.DraftsRead}+{SalesPermissionManifestV1.OrdersCancel}"),
                (SalesShellCapabilityProvider.CustomerAcceptanceRecordCapabilityId,
                    SalesShellCapabilityProvider.DetailCapabilityId,
                    $"{SalesPermissionManifestV1.DraftsRead}+{SalesPermissionManifestV1.OrdersCustomerAcceptanceRecord}"),
                (SalesShellCapabilityProvider.DeliveryScheduleWriteCapabilityId, SalesShellCapabilityProvider.DetailCapabilityId,
                    $"{SalesPermissionManifestV1.DraftsRead}+{SalesPermissionManifestV1.OrdersDeliveryScheduleWrite}"),
                // No 30 / SAT-D12: the force-close control of the detail.
                (SalesShellCapabilityProvider.ForceCloseCapabilityId, SalesShellCapabilityProvider.DetailCapabilityId,
                    $"{SalesPermissionManifestV1.DraftsRead}+{SalesPermissionManifestV1.OrdersForceClose}"),
                (SalesShellCapabilityProvider.PriceFloorExceptionApproveCapabilityId,
                    SalesShellCapabilityProvider.DetailCapabilityId,
                    $"{SalesPermissionManifestV1.DraftsRead}+{SalesPermissionManifestV1.OrdersPriceFloorExceptionApprove}"),
                (SalesShellCapabilityProvider.PriceWriteCapabilityId, SalesShellCapabilityProvider.DetailCapabilityId,
                    $"{SalesPermissionManifestV1.DraftsRead}+{SalesPermissionManifestV1.OrdersPriceWrite}"),
                (SalesShellCapabilityProvider.QuantityWriteCapabilityId, SalesShellCapabilityProvider.DetailCapabilityId,
                    $"{SalesPermissionManifestV1.DraftsRead}+{SalesPermissionManifestV1.OrdersQuantityWrite}"),
            ],
            sales);
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

        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetail, authority);
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

    // FIN-UI-1c: the disposable fin-staged profile ("finance-bank-accounts-and-drafts", #332) is retired. Its
    // value is no profile any more: inside the C-01 envelope it fails closed at startup, as any unknown one.
    [Fact]
    public void The_retired_fin_staged_profile_value_fails_closed()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IShellCapabilityProvider, CounterpartyShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, FinanceShellCapabilityProvider>();
        var envelope = new Dictionary<string, string?>(C01E2eShellCapabilityActivationTests.ExactEnvelope())
        {
            [C01E2eShellCapabilityActivation.ProfileConfigurationKey] = "finance-bank-accounts-and-drafts",
        };

        Assert.Throws<InvalidOperationException>(() => C01E2eShellCapabilityActivation.Configure(services,
            new TestingEnvironment(), new ConfigurationBuilder().AddInMemoryCollection(envelope).Build()));
    }

    private sealed class TestingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
