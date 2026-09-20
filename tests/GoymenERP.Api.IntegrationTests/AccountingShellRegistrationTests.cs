using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Accounting.Infrastructure.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Finance.Infrastructure.Shell;
using GoymenERP.Modules.Logistics.Infrastructure.Shell;
using GoymenERP.Modules.Manufacturing.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using GoymenERP.Modules.Product.Infrastructure.Shell;
using GoymenERP.Modules.Purchasing.Infrastructure.Shell;
using GoymenERP.Modules.Sales.Infrastructure.Shell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ACC-V0b: the staged Accounting shell provider joins the host set with its own reviewed normal-host
/// authority, and the disposable acc-staged profile opens exactly the same declarations — only their
/// availability — beside the list-only C-01 Counterparty set. The permission names themselves were
/// registered with ACC-V0a and are pinned by <c>AccountingPermissionRegistrationTests</c>.
/// </summary>
public sealed class AccountingShellRegistrationTests
{
    [Fact]
    public void The_Accounting_authority_is_the_registry_revision_of_the_nine_real_providers()
    {
        IShellCapabilityProvider[] providers =
        [
            new CounterpartyShellCapabilityProvider(), new OrganizationShellCapabilityProvider(),
            new ProductShellCapabilityProvider(), new SalesShellCapabilityProvider(),
            new FinanceShellCapabilityProvider(), new ManufacturingShellCapabilityProvider(),
            new LogisticsShellCapabilityProvider(), new PurchasingShellCapabilityProvider(),
            new AccountingShellCapabilityProvider(),
        ];

        // On failure xUnit prints the actual revision: the constant is never guessed.
        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15OrganizationV11ProductV10PublishedSalesV10PublishedFinanceV10PublishedManufacturingV10LogisticsV10PurchasingV10AndAccountingV10,
            ShellCapabilityRegistry.ComputeCandidateRevision(providers));
    }

    [Fact]
    public void The_normal_host_with_Accounting_gets_its_authority_and_the_staged_screens_grant_nothing()
    {
        var services = NormalHost();

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15OrganizationV11ProductV10PublishedSalesV10PublishedFinanceV10PublishedManufacturingV10LogisticsV10PurchasingV10AndAccountingV10,
            authority);
        using var container = services.BuildServiceProvider();
        var registry = new ShellCapabilityRegistry(container.GetServices<IShellCapabilityProvider>(),
            new ShellCapabilityAuthorityCatalog([authority]));
        var accounting = registry.Registrations
            .Where(item => item.Owner == AccountingShellCapabilityProvider.OwnerId)
            .ToArray();
        // M-07 and its propose screen, the approve and close controls of that list, M-04 and its detail, M-01:
        // seven declarations, every one staged until the acc01 acceptance flips them in a separate change.
        Assert.Equal(7, accounting.Length);
        Assert.All(accounting, item => Assert.False(item.Declaration.ImplementationAvailable));
        string[] expected =
        [
            "accounting.journals.detail", "accounting.journals.list", "accounting.periods.approve",
            "accounting.periods.close", "accounting.periods.list", "accounting.periods.propose",
            "accounting.posting-queues",
        ];
        Assert.Equal(expected,
            accounting.Select(item => item.Declaration.CapabilityId).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void The_acc_staged_profile_opens_the_owners_own_declarations_and_nothing_else()
    {
        var services = NormalHost();
        var envelope = C01E2eShellCapabilityActivationTests.ExactEnvelope();
        envelope[C01E2eShellCapabilityActivation.ProfileConfigurationKey] =
            C01E2eShellCapabilityActivation.AccountingPeriodsAndJournalsProfile;

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().AddInMemoryCollection(envelope).Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.C01DisposableAccountingPeriodsAndJournalsV1, authority);
        using var container = services.BuildServiceProvider();
        var registry = new ShellCapabilityRegistry(container.GetServices<IShellCapabilityProvider>(),
            new ShellCapabilityAuthorityCatalog([authority]));
        var owner = new AccountingShellCapabilityProvider().GetDeclarations()
            .OrderBy(declaration => declaration.CapabilityId, StringComparer.Ordinal)
            .ToArray();
        var opened = registry.Registrations
            .Where(item => item.Owner == AccountingShellCapabilityProvider.OwnerId)
            .Select(item => item.Declaration)
            .OrderBy(declaration => declaration.CapabilityId, StringComparer.Ordinal)
            .ToArray();

        // Availability is the ONLY difference: ids, route ids, paths, screens, precedence, context, scope,
        // permissions and the two controls' parents stay exactly what the owner declared.
        //
        // Compared member by member, with the permissions as an ORDERED SEQUENCE, and deliberately NOT as
        // whole records: ShellCapabilityDeclaration is a record whose RequiredPermissions is an
        // IReadOnlyCollection<string>, so the synthesized equality compares that member BY REFERENCE. Two
        // structurally identical declarations built from different provider instances are then never equal,
        // and the failure prints two lines that look the same. This is not a weakening — every member is
        // still asserted, and the projection is compared as a whole set, so a declaration that acc-staged
        // added, dropped or altered still fails.
        Assert.Equal(
            owner.Select(declaration => Comparable(declaration with { ImplementationAvailable = true })).ToArray(),
            opened.Select(Comparable).ToArray());
        // The envelope keeps the list-only C-01 Counterparty set; no other module's provider survives it.
        Assert.Equal(
            [AccountingShellCapabilityProvider.OwnerId, CounterpartyShellCapabilityProvider.OwnerId],
            registry.Registrations.Select(item => item.Owner).Distinct().Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Every member of a declaration as one comparable value, with the permissions in order. See the
    /// comment above: the record's own equality compares <c>RequiredPermissions</c> by reference.
    /// </summary>
    private static string Comparable(ShellCapabilityDeclaration declaration) => string.Join(
        '\u001f',
        declaration.CapabilityId,
        declaration.RouteId,
        declaration.RoutePath,
        declaration.ScreenId,
        declaration.RoutePrecedence.ToString(),
        declaration.ContextRequirement.ToString(),
        declaration.ScopeRequirement.ToString(),
        declaration.ImplementationAvailable ? "available" : "staged",
        declaration.GlobalProductOperation?.ToString() ?? "<no-global-operation>",
        declaration.ParentCapabilityId ?? "<no-parent>",
        string.Join(',', declaration.RequiredPermissions));

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
        services.AddSingleton<IShellCapabilityProvider, AccountingShellCapabilityProvider>();
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
