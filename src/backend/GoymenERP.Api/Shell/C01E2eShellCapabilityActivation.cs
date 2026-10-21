using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Accounting.Infrastructure.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Finance.Infrastructure.Shell;
using GoymenERP.Modules.Identity.Infrastructure.Shell;
using GoymenERP.Modules.Inventory.Infrastructure.Shell;
using GoymenERP.Modules.Logistics.Infrastructure.Shell;
using GoymenERP.Modules.Manufacturing.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using GoymenERP.Modules.Purchasing.Infrastructure.Shell;
using GoymenERP.Modules.Product.Infrastructure.Shell;
using GoymenERP.Modules.Sales.Infrastructure.Shell;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.Shell;

internal static class C01E2eShellCapabilityActivation
{
    internal const string EnableConfigurationKey =
        "C01E2e:EnableListCapability";
    internal const string DisposableConfirmationConfigurationKey =
        "GOYMEN_ERP_C01_E2E_DISPOSABLE";
    internal const string TestedCommitConfigurationKey =
        "GOYMEN_ERP_C01_E2E_TESTED_COMMIT";
    internal const string RunIdConfigurationKey =
        "GOYMEN_ERP_C01_E2E_RUN_ID";
    internal const string ComposeProjectConfigurationKey =
        "GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT";

    // BROWSER-CI stage 2: which Counterparty declarations the disposable envelope opens. Absent is
    // the canonical list-only C-01. "cari-list-detail" is the activation step the Cari specs
    // (CARI_C02_LINK_JIT_SPEC_v1, CARI_C02_LINK_ACCEPTANCE_v1) put before any normal-runtime
    // availability: list and detail, still only inside this disposable envelope.
    internal const string ProfileConfigurationKey = "C01E2e:Profile";
    internal const string CariListDetailProfile = "cari-list-detail";
    // MFG-UI D4's mfg-staged profile ("manufacturing-order-detail", #323) is retired: Manufacturing is
    // published on the normal host (MFG-UI-F), so its screen needs no disposable envelope any more.
    // FIN-UI-1b's fin-staged profile ("finance-bank-accounts-and-drafts", #332) is retired: Finance is
    // published on the normal host (FIN-UI-1c), so its screens need no disposable envelope any more.
    // ACC-V0b's acc-staged profile ("accounting-periods-and-journals") is retired: Accounting is
    // published on the normal host (ACC-V0c), so its screens need no disposable envelope any more —
    // the retirement its own comment promised at the flip after the acc01 suite.
    internal static string Configure(
        IServiceCollection services,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!IsActivationRequested(configuration))
        {
            return NormalAuthority(services);
        }

        ValidateActivationEnvelope(environment, configuration);
        switch (configuration[ProfileConfigurationKey])
        {
            case null:
                ReplaceExactCounterpartyProvider<C01E2eShellCapabilityProvider>(services);
                return ShellCapabilityRegistryAuthorities.C01DisposableListOnlyV1;
            case CariListDetailProfile:
                ReplaceExactCounterpartyProvider<C01E2eCariListDetailShellCapabilityProvider>(services);
                return ShellCapabilityRegistryAuthorities.C01DisposableCariListDetailV1;
            default:
                throw new InvalidOperationException(
                    "C-01 disposable capability profili tanınmıyor.");
        }
    }

    // The module providers the normal host may register beside the one required Counterparty
    // provider, each at most once. A module that joins the shell adds one row here and one accepted
    // authority below; the C-01 envelope removes all of them and stays list-only.
    private static readonly Type[] OptionalModuleProviders =
    [
        typeof(OrganizationShellCapabilityProvider),
        typeof(ProductShellCapabilityProvider),
        typeof(SalesShellCapabilityProvider),
        typeof(FinanceShellCapabilityProvider),
        typeof(ManufacturingShellCapabilityProvider),
        typeof(LogisticsShellCapabilityProvider),
        typeof(PurchasingShellCapabilityProvider),
        typeof(AccountingShellCapabilityProvider),
        typeof(InventoryShellCapabilityProvider),
        typeof(IdentityShellCapabilityProvider),
        typeof(ProductPriceListShellCapabilityProvider),
    ];

    // The accepted normal-host authority for each set of optional module providers present (in the
    // order above). An unlisted set keeps the Counterparty-only authority, which the registry then
    // refuses for the providers it actually holds: startup fails closed, nothing unreviewed is served.
    private static readonly (Type[] Providers, string Authority)[] NormalAuthorities =
    [
        (Type.EmptyTypes, ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetail),
        (new[] { typeof(OrganizationShellCapabilityProvider) },
            ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailAndOrganizationV13),
        (new[] { typeof(OrganizationShellCapabilityProvider), typeof(ProductShellCapabilityProvider) },
            ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13AndProductV12),
        (new[]
            {
                typeof(OrganizationShellCapabilityProvider), typeof(ProductShellCapabilityProvider),
                typeof(SalesShellCapabilityProvider),
            },
            ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12AndPublishedSalesV15),
        (new[]
            {
                typeof(OrganizationShellCapabilityProvider), typeof(ProductShellCapabilityProvider),
                typeof(SalesShellCapabilityProvider), typeof(FinanceShellCapabilityProvider),
            },
                ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV15AndPublishedFinanceV15),
        (new[]
            {
                typeof(OrganizationShellCapabilityProvider), typeof(ProductShellCapabilityProvider),
                typeof(SalesShellCapabilityProvider), typeof(FinanceShellCapabilityProvider),
                typeof(ManufacturingShellCapabilityProvider),
            },
                ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV15PublishedFinanceV15AndPublishedManufacturingV15),
        // LOG-PUBLISH: Logistics's loading-instruction screens are published on the normal host.
        (new[]
            {
                typeof(OrganizationShellCapabilityProvider), typeof(ProductShellCapabilityProvider),
                typeof(SalesShellCapabilityProvider), typeof(FinanceShellCapabilityProvider),
                typeof(ManufacturingShellCapabilityProvider), typeof(LogisticsShellCapabilityProvider),
            },
                ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV16PublishedFinanceV15PublishedManufacturingV15AndPublishedLogisticsV13),
        // P01: Purchasing 1.3 publishes request list/create/detail/submit beside order list/create/detail/approve.
        (new[]
            {
                typeof(OrganizationShellCapabilityProvider), typeof(ProductShellCapabilityProvider),
                typeof(SalesShellCapabilityProvider), typeof(FinanceShellCapabilityProvider),
                typeof(ManufacturingShellCapabilityProvider), typeof(LogisticsShellCapabilityProvider),
                typeof(PurchasingShellCapabilityProvider),
            },
                ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV16PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV13AndPublishedPurchasingV14),
        // ACC-V0b: Accounting's (staged) period, journal and posting-queue screens join the host set.
        (new[]
            {
                typeof(OrganizationShellCapabilityProvider), typeof(ProductShellCapabilityProvider),
                typeof(SalesShellCapabilityProvider), typeof(FinanceShellCapabilityProvider),
                typeof(ManufacturingShellCapabilityProvider), typeof(LogisticsShellCapabilityProvider),
                typeof(PurchasingShellCapabilityProvider), typeof(AccountingShellCapabilityProvider),
            },
                ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV16PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV13PublishedPurchasingV14AndPublishedAccountingV13),
        (new[]
            {
                typeof(OrganizationShellCapabilityProvider), typeof(ProductShellCapabilityProvider),
                typeof(SalesShellCapabilityProvider), typeof(FinanceShellCapabilityProvider),
                typeof(ManufacturingShellCapabilityProvider), typeof(LogisticsShellCapabilityProvider),
                typeof(PurchasingShellCapabilityProvider), typeof(AccountingShellCapabilityProvider),
                typeof(InventoryShellCapabilityProvider),
            },
                ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV16PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV13PublishedPurchasingV14PublishedAccountingV13AndInventoryV18WithDormantAllocationDetail),
        // SY-01/SY-02: Identity's read-only users-and-permissions view joins the normal host.
        (new[]
            {
                typeof(OrganizationShellCapabilityProvider), typeof(ProductShellCapabilityProvider),
                typeof(SalesShellCapabilityProvider), typeof(FinanceShellCapabilityProvider),
                typeof(ManufacturingShellCapabilityProvider), typeof(LogisticsShellCapabilityProvider),
                typeof(PurchasingShellCapabilityProvider), typeof(AccountingShellCapabilityProvider),
                typeof(InventoryShellCapabilityProvider), typeof(IdentityShellCapabilityProvider),
            },
                ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetailOrganizationV13ProductV12PublishedSalesV16PublishedFinanceV15PublishedManufacturingV15PublishedLogisticsV13PublishedPurchasingV14PublishedAccountingV13InventoryV18WithDormantAllocationDetailAndPublishedIdentityV10),
        // AT-018 / AT-021: company-scoped Product price lists and Sales floor-approval join only the full normal host.
        (new[]
            {
                typeof(OrganizationShellCapabilityProvider), typeof(ProductShellCapabilityProvider),
                typeof(SalesShellCapabilityProvider), typeof(FinanceShellCapabilityProvider),
                typeof(ManufacturingShellCapabilityProvider), typeof(LogisticsShellCapabilityProvider),
                typeof(PurchasingShellCapabilityProvider), typeof(AccountingShellCapabilityProvider),
                typeof(InventoryShellCapabilityProvider), typeof(IdentityShellCapabilityProvider),
                typeof(ProductPriceListShellCapabilityProvider),
            },
                ShellCapabilityRegistryAuthorities.FullNormalHostWithProductPriceListsSalesFloorApprovalAndInventoryV18WithDormantAllocationDetail),
    ];

    private static string NormalAuthority(IServiceCollection services)
    {
        var present = OptionalModuleProviders
            .Where(type => services.Any(descriptor =>
                descriptor.ServiceType == typeof(IShellCapabilityProvider)
                && descriptor.ImplementationType == type))
            .ToArray();
        foreach (var (providers, authority) in NormalAuthorities)
        {
            if (providers.SequenceEqual(present))
            {
                return authority;
            }
        }

        return ShellCapabilityRegistryAuthorities.CounterpartyV15PublishedListCreateAndDetail;
    }

    private static bool IsActivationRequested(IConfiguration configuration) =>
        ConfigurationKeys.Any(key => configuration[key] is not null);

    private static void ValidateActivationEnvelope(
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        if (!environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException(
                "C-01 disposable capability yalnız Testing ortamında etkinleştirilebilir.");
        }

        RequireExact(configuration, EnableConfigurationKey, "true");
        RequireExact(
            configuration,
            DisposableConfirmationConfigurationKey,
            "CONFIRMED");

        var testedCommit = Required(configuration, TestedCommitConfigurationKey);
        if (testedCommit.Length != 40
            || testedCommit.Any(character =>
                character is not (>= '0' and <= '9')
                and not (>= 'a' and <= 'f')))
        {
            throw new InvalidOperationException(
                "C-01 disposable capability exact lowercase Git SHA gerektirir.");
        }

        var runId = Required(configuration, RunIdConfigurationKey);
        if (runId.Any(character => character is < '0' or > '9'))
        {
            throw new InvalidOperationException(
                "C-01 disposable capability sayısal runner kimliği gerektirir.");
        }

        var composeProject = Required(configuration, ComposeProjectConfigurationKey);
        var expectedPrefix = $"goymen-c01-{runId}-";
        var attempt = composeProject.StartsWith(expectedPrefix, StringComparison.Ordinal)
            ? composeProject[expectedPrefix.Length..]
            : string.Empty;
        if (attempt.Length == 0
            || attempt.Any(character => character is < '0' or > '9'))
        {
            throw new InvalidOperationException(
                "C-01 disposable capability runner ile bağlı compose project kimliği gerektirir.");
        }
    }

    private static void ReplaceExactCounterpartyProvider<TDisposable>(IServiceCollection services)
        where TDisposable : class, IShellCapabilityProvider
    {
        var providers = services
            .Where(descriptor => descriptor.ServiceType == typeof(IShellCapabilityProvider))
            .ToArray();
        var counterpartyCount = providers.Count(descriptor =>
            descriptor.ImplementationType == typeof(CounterpartyShellCapabilityProvider));
        var optionalCounts = OptionalModuleProviders
            .Select(type => providers.Count(descriptor => descriptor.ImplementationType == type))
            .ToArray();
        if (counterpartyCount != 1
            || optionalCounts.Any(count => count > 1)
            || providers.Length != counterpartyCount + optionalCounts.Sum()
            || providers.Any(descriptor => descriptor.Lifetime != ServiceLifetime.Singleton))
        {
            throw new InvalidOperationException(
                "C-01 disposable capability beklenmeyen shell provider setini değiştiremez.");
        }

        // The disposable C-01 envelope remains strictly list-only, even when
        // the normal host has additional accepted module declarations.
        foreach (var provider in providers)
        {
            services.Remove(provider);
        }
        services.AddSingleton<IShellCapabilityProvider, TDisposable>();
    }

    private static void RequireExact(
        IConfiguration configuration,
        string key,
        string expected)
    {
        if (!string.Equals(configuration[key], expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"C-01 disposable capability '{key}' exact değerini gerektirir.");
        }
    }

    private static string Required(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrEmpty(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"C-01 disposable capability '{key}' değerini gerektirir.");
        }

        return value;
    }

    private static readonly string[] ConfigurationKeys =
    [
        EnableConfigurationKey,
        DisposableConfirmationConfigurationKey,
        TestedCommitConfigurationKey,
        RunIdConfigurationKey,
        ComposeProjectConfigurationKey,
        ProfileConfigurationKey,
    ];
}

internal sealed class C01E2eShellCapabilityProvider : IShellCapabilityProvider
{
    private readonly CounterpartyShellCapabilityProvider _source = new();

    public string Owner => _source.Owner;

    public string ContractVersion => _source.ContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
        C01E2eStagedDeclarations.Open(_source, detail: false);
}

/// <summary>The disposable "cari-list-detail" profile: the C-01 list plus the Cari detail (C-03).</summary>
internal sealed class C01E2eCariListDetailShellCapabilityProvider : IShellCapabilityProvider
{
    private readonly CounterpartyShellCapabilityProvider _source = new();

    public string Owner => _source.Owner;

    public string ContractVersion => _source.ContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
        C01E2eStagedDeclarations.Open(_source, detail: true);
}

internal static class C01E2eStagedDeclarations
{
    // The list is opened exactly as C-01 always has (single company); the detail only changes its
    // availability and keeps the canonical declaration's own context, scope and permission.
    //
    // EVERY declaration's availability is STATED here, including the ones this envelope closes.
    // It used to end in `_ => declaration`, which passed create and link through with whatever the
    // real provider happened to carry — correct only for as long as everything else was staged. The
    // moment the normal provider publishes create, an envelope whose entire name is "list-only"
    // would have started offering a create screen, and the two disposable authority constants would
    // have moved with it: a silent widening of an acceptance surface as a side effect of an
    // unrelated flip. An envelope's contents are DECLARED, not inherited — so this switch is
    // exhaustive over the owner's four capabilities and the default arm now refuses an unknown one
    // rather than quietly admitting it with the owner's flag.
    internal static IReadOnlyCollection<ShellCapabilityDeclaration> Open(
        CounterpartyShellCapabilityProvider source, bool detail) =>
        Array.AsReadOnly(source.GetDeclarations()
            .Select(declaration => declaration.CapabilityId switch
            {
                CounterpartyShellCapabilityProvider.ListCapabilityId => declaration with
                {
                    ContextRequirement = ShellCapabilityContextRequirement.SingleCompany,
                    ImplementationAvailable = true,
                },
                CounterpartyShellCapabilityProvider.DetailCapabilityId => declaration with
                {
                    ImplementationAvailable = detail,
                },
                // Closed in both envelopes, independently of what the normal host publishes.
                CounterpartyShellCapabilityProvider.CreateCapabilityId
                    or CounterpartyShellCapabilityProvider.LinkCapabilityId => declaration with
                {
                    ImplementationAvailable = false,
                },
                _ => throw new InvalidOperationException(
                    "C-01 disposable zarfı, Cari sağlayıcısının tanımadığı bir kabiliyetle karşılaştı: "
                    + declaration.CapabilityId),
            })
            .ToArray());
}
