using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
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
            return services.Any(descriptor =>
                descriptor.ServiceType == typeof(IShellCapabilityProvider)
                && descriptor.ImplementationType == typeof(OrganizationShellCapabilityProvider))
                ? ShellCapabilityRegistryAuthorities.CounterpartyV15AndOrganizationV10
                : ShellCapabilityRegistryAuthorities.CounterpartyListCreateLinkAndDetailV15;
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
        var organizationCount = providers.Count(descriptor =>
            descriptor.ImplementationType == typeof(OrganizationShellCapabilityProvider));
        if (counterpartyCount != 1
            || organizationCount > 1
            || providers.Length != counterpartyCount + organizationCount
            || providers.Any(descriptor => descriptor.Lifetime != ServiceLifetime.Singleton))
        {
            throw new InvalidOperationException(
                "C-01 disposable capability beklenmeyen shell provider setini değiştiremez.");
        }

        // The disposable C-01 envelope remains strictly list-only, even when
        // the normal host has additional accepted Organization declarations.
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
                CounterpartyShellCapabilityProvider.DetailCapabilityId when detail => declaration with
                {
                    ImplementationAvailable = true,
                },
                _ => declaration,
            })
            .ToArray());
}
