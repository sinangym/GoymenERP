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
/// ACC-V0c: the Accounting shell provider joins the host set with its own reviewed normal-host authority,
/// publishing six of its seven declarations and leaving the journal detail staged. The disposable
/// acc-staged profile that carried these screens before the flip is retired, and the test below pins
/// what its value means now: nothing, failing closed. The permission names themselves were registered
/// with ACC-V0a and are pinned by <c>AccountingPermissionRegistrationTests</c>.
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

        var actual = ShellCapabilityRegistry.ComputeCandidateRevision(providers);
        Assert.True(
            string.Equals(ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV11ProductV10PublishedSalesV11PublishedFinanceV12PublishedManufacturingV12PublishedLogisticsV10PublishedPurchasingV13AndPublishedAccountingV12, actual,
                StringComparison.Ordinal),
            $"Accounting normal-host authority drifted. Actual: {actual}");
    }

    [Fact]
    public void The_normal_host_with_Accounting_gets_its_authority_and_the_staged_screens_grant_nothing()
    {
        var services = NormalHost();

        var authority = C01E2eShellCapabilityActivation.Configure(services, new TestingEnvironment(),
            new ConfigurationBuilder().Build());

        Assert.Equal(ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV11ProductV10PublishedSalesV11PublishedFinanceV12PublishedManufacturingV12PublishedLogisticsV10PublishedPurchasingV13AndPublishedAccountingV12,
            authority);
        using var container = services.BuildServiceProvider();
        var registry = new ShellCapabilityRegistry(container.GetServices<IShellCapabilityProvider>(),
            new ShellCapabilityAuthorityCatalog([authority]));
        var accounting = registry.Registrations
            .Where(item => item.Owner == AccountingShellCapabilityProvider.OwnerId)
            .ToArray();
        // M-07 and its propose screen, the approve and close controls of that list, M-04 and its detail, M-01:
        // Eleven declarations: the seven ACC-V0 routes/controls, three normal-draft routes and staged M-06.
        Assert.Equal(11, accounting.Length);
        // Named rather than counted, and asserted as a SET: "all staged" was true only until the flip, while
        // "these six and no others" stays true and fails loudly if a seventh is ever published without an
        // acceptance to stand on.
        string[] published =
        [
            "accounting.journals.list", "accounting.normal-journal-drafts.approve",
            "accounting.normal-journal-drafts.create", "accounting.normal-journal-drafts.detail",
            "accounting.periods.approve", "accounting.periods.close",
            "accounting.periods.list", "accounting.periods.propose", "accounting.posting-queues",
        ];
        Assert.Equal(published, accounting.Where(item => item.Declaration.ImplementationAvailable)
            .Select(item => item.Declaration.CapabilityId).Order(StringComparer.Ordinal).ToArray());
        // M-04-DETAIL stays staged on purpose: acc01 never opened a journal — its seed writes none and no
        // flow produces one before ACC-V1 — so that route has no acceptance to stand on, and publishing it
        // would claim a screen nobody walked.
        var journalDetail = Assert.Single(accounting,
            item => item.Declaration.CapabilityId == AccountingShellCapabilityProvider.JournalDetailCapabilityId);
        Assert.False(journalDetail.Declaration.ImplementationAvailable);
        var trialBalance = Assert.Single(accounting,
            item => item.Declaration.CapabilityId == AccountingShellCapabilityProvider.TrialBalanceCapabilityId);
        Assert.False(trialBalance.Declaration.ImplementationAvailable);
        string[] expected =
        [
            "accounting.journals.detail", "accounting.journals.list", "accounting.normal-journal-drafts.approve",
            "accounting.normal-journal-drafts.create", "accounting.normal-journal-drafts.detail", "accounting.periods.approve",
            "accounting.periods.close", "accounting.periods.list", "accounting.periods.propose",
            "accounting.posting-queues", "accounting.trial-balance",
        ];
        Assert.Equal(expected,
            accounting.Select(item => item.Declaration.CapabilityId).Order(StringComparer.Ordinal).ToArray());
    }

    // ACC-V0c: the disposable acc-staged profile ("accounting-periods-and-journals", ACC-V0b) is retired.
    // Its value is no profile any more: inside the C-01 envelope it fails closed at startup, as any unknown
    // one. The string is kept HERE rather than deleted with the case arm, so that the day someone puts it
    // back the failure names it; a retirement that leaves no test leaves no way to notice the return.
    [Fact]
    public void The_retired_acc_staged_profile_value_fails_closed()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IShellCapabilityProvider, CounterpartyShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, AccountingShellCapabilityProvider>();
        var envelope = new Dictionary<string, string?>(C01E2eShellCapabilityActivationTests.ExactEnvelope())
        {
            [C01E2eShellCapabilityActivation.ProfileConfigurationKey] = "accounting-periods-and-journals",
        };

        var error = Assert.Throws<InvalidOperationException>(() => C01E2eShellCapabilityActivation.Configure(services,
            new TestingEnvironment(), new ConfigurationBuilder().AddInMemoryCollection(envelope).Build()));

        // The TYPE alone would not have proved the refusal: this minimal two-provider collection can
        // raise InvalidOperationException for reasons of its own, so "it threw" and "it refused the
        // profile" are different claims. Assert the default arm's own message.
        Assert.Contains("profili tanınmıyor", error.Message, StringComparison.Ordinal);
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
