using GoymenERP.Modules.Identity.Infrastructure.Fixtures;

namespace GoymenERP.Api.Shell;

/// <summary>
/// BROWSER-CI stage 2 (a2 part 2, coordinator decision W1): the gated fixture writers, for owner write
/// ports that have no endpoint. They are registered only inside the browser-suite fixture runner's
/// in-process host, which alone sets the BrowserSuiteFixture:* keys (as in-memory configuration; the
/// validator never allows them in a compose file): Testing, Host=CONFIRMED, the exact tested commit, the
/// numeric runner id and the goymen-c01-&lt;run&gt;-&lt;attempt&gt; project, the C-01 identity chain's own
/// rules. No key present: nothing is registered. Any key present but the gate not holding: startup fails.
/// </summary>
internal static class C01E2eFixtureWriterActivation
{
    internal const string HostConfigurationKey = "BrowserSuiteFixture:Host";
    internal const string TestedCommitConfigurationKey = "BrowserSuiteFixture:TestedCommit";
    internal const string RunIdConfigurationKey = "BrowserSuiteFixture:RunId";
    internal const string ComposeProjectConfigurationKey = "BrowserSuiteFixture:ComposeProject";
    internal const string ConfirmedValue = "CONFIRMED";

    private static readonly string[] ConfigurationKeys =
    [
        HostConfigurationKey,
        TestedCommitConfigurationKey,
        RunIdConfigurationKey,
        ComposeProjectConfigurationKey,
    ];

    /// <summary>Registers the writers when the gate holds; returns whether it did.</summary>
    internal static bool Configure(
        IServiceCollection services,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!ConfigurationKeys.Any(key => configuration[key] is not null))
        {
            return false;
        }

        RequireGate(environment, configuration);
        services.AddProductMasterAssignmentFixtureStore();
        services.AddScoped<C01E2eProductMasterAssignmentFixtureWriter>();
        return true;
    }

    /// <summary>The gate itself; throws unless every condition holds.</summary>
    internal static void RequireGate(IHostEnvironment environment, IConfiguration configuration)
    {
        if (!environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException(
                "Browser suite fixture writers exist only in the Testing environment.");
        }

        if (!string.Equals(configuration[HostConfigurationKey], ConfirmedValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Browser suite fixture writers require BrowserSuiteFixture:Host=CONFIRMED.");
        }

        var testedCommit = configuration[TestedCommitConfigurationKey] ?? string.Empty;
        if (testedCommit.Length != 40
            || testedCommit.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new InvalidOperationException(
                "Browser suite fixture writers require the exact lowercase tested commit.");
        }

        var runId = configuration[RunIdConfigurationKey] ?? string.Empty;
        if (runId.Length == 0 || runId.Any(character => character is < '0' or > '9'))
        {
            throw new InvalidOperationException(
                "Browser suite fixture writers require a numeric runner id.");
        }

        var composeProject = configuration[ComposeProjectConfigurationKey] ?? string.Empty;
        var expectedPrefix = $"goymen-c01-{runId}-";
        var attempt = composeProject.StartsWith(expectedPrefix, StringComparison.Ordinal)
            ? composeProject[expectedPrefix.Length..]
            : string.Empty;
        if (attempt.Length == 0 || attempt.Any(character => character is < '0' or > '9'))
        {
            throw new InvalidOperationException(
                "Browser suite fixture writers require the runner-bound compose project.");
        }
    }
}

/// <summary>
/// Writes an explicit synthetic ProductMasterAssignment through Identity's fixture port. Constructing it
/// outside the gate fails even if someone registers it by hand. The decision reference must name the
/// suite (SENTETIK browser-suite:&lt;suite&gt; …), so every such row is traceable to its fixture.
/// </summary>
public sealed class C01E2eProductMasterAssignmentFixtureWriter
{
    public const string DecisionReferencePrefix = "SENTETIK browser-suite:";

    private readonly IProductMasterAssignmentFixtureStore _store;

    public C01E2eProductMasterAssignmentFixtureWriter(
        IHostEnvironment environment,
        IConfiguration configuration,
        IProductMasterAssignmentFixtureStore store)
    {
        C01E2eFixtureWriterActivation.RequireGate(environment, configuration);
        _store = store;
    }

    public Task EnsureAsync(ProductMasterAssignmentFixtureRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.DecisionReference.StartsWith(DecisionReferencePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"A fixture decision reference must start '{DecisionReferencePrefix}'.");
        }

        if (!Enum.IsDefined(row.Operation))
        {
            throw new InvalidOperationException("Unknown Product master operation.");
        }

        return _store.EnsureAsync(row, cancellationToken);
    }
}
