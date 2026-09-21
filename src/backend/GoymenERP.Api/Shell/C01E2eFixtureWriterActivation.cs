using GoymenERP.Modules.Accounting.Infrastructure.Fixtures;
using GoymenERP.Modules.Identity.Infrastructure.Fixtures;
using GoymenERP.Modules.Logistics.Infrastructure.Fixtures;
using GoymenERP.Modules.Product.Infrastructure.Fixtures;
using GoymenERP.Modules.Sales.Infrastructure.Fixtures;

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
        // u02 (#331): Product's code-group port (ÜRÜN's), behind the same gate.
        services.AddProductCodeGroupFixtureStore();
        services.AddScoped<C01E2eProductCodeGroupFixtureWriter>();
        // u02 technical-card continuation: Product's fixed synthetic family/template/dictionary fixture,
        // behind the same complete runner-bound gate. The port accepts no caller-selected values.
        services.AddProductTechnicalCardFixtureStore();
        services.AddScoped<C01E2eProductTechnicalCardFixtureWriter>();
        // s04 (SALES-A1 browser acceptance): Sales's draft port (SİPARİŞ's), behind the same gate.
        services.AddSalesDraftFixtureStore();
        services.AddScoped<C01E2eSalesDraftFixtureWriter>();
        // l04 (LOG-4b browser acceptance): Logistics's line-block port (SİPARİŞ's), behind the same gate.
        services.AddLogisticsLineBlockFixtureStore();
        services.AddScoped<C01E2eLogisticsLineBlockFixtureWriter>();
        // fin01 FIN-T05/T06: one fixed synthetic Open Accounting period. No API grant or caller-selected row.
        services.AddAccountingOpenPeriodFixtureStore();
        services.AddScoped<C01E2eAccountingOpenPeriodFixtureWriter>();
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

/// <summary>
/// Writes an explicit synthetic Product code group through Product's fixture port (coordinator decision
/// W1/C, u02). The sibling of <see cref="C01E2eProductMasterAssignmentFixtureWriter"/>: constructing it
/// outside the gate fails even if someone registers it by hand, and the group's name must name its suite
/// (SENTETIK browser-suite:&lt;suite&gt; …), so every such row is traceable to its fixture. Registered only by
/// <see cref="C01E2eFixtureWriterActivation"/>; written by ÜRÜN (#331), kept in this file by CARİ so the
/// activation stays named in this file and Program.cs only.
/// </summary>
public sealed class C01E2eProductCodeGroupFixtureWriter
{
    public const string NamePrefix = "SENTETIK browser-suite:";

    private readonly IProductCodeGroupFixtureStore _store;

    public C01E2eProductCodeGroupFixtureWriter(
        IHostEnvironment environment,
        IConfiguration configuration,
        IProductCodeGroupFixtureStore store)
    {
        C01E2eFixtureWriterActivation.RequireGate(environment, configuration);
        _store = store;
    }

    public Task EnsureAsync(ProductCodeGroupFixtureRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.Name.StartsWith(NamePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"A fixture code group name must start '{NamePrefix}'.");
        }

        return _store.EnsureAsync(row, cancellationToken);
    }
}

/// <summary>
/// Exposes Product's fixed u02 technical-card fixture only inside the browser runner's complete gate.
/// The Product-owned port has no input, so the host cannot turn this into a general data writer.
/// </summary>
public sealed class C01E2eProductTechnicalCardFixtureWriter
{
    private readonly IProductTechnicalCardFixtureStore _store;

    public C01E2eProductTechnicalCardFixtureWriter(
        IHostEnvironment environment,
        IConfiguration configuration,
        IProductTechnicalCardFixtureStore store)
    {
        C01E2eFixtureWriterActivation.RequireGate(environment, configuration);
        _store = store;
    }

    public Task EnsureAsync(CancellationToken cancellationToken) =>
        _store.EnsureAsync(cancellationToken);
}

/// <summary>
/// Writes ONE explicit synthetic Preparing sales order draft through Sales's fixture port (coordinator
/// decision W1/C, s04). The sibling of <see cref="C01E2eProductCodeGroupFixtureWriter"/>: constructing it
/// outside the gate fails even if someone registers it by hand, and the request's label must name its suite
/// (SENTETIK browser-suite:&lt;suite&gt; …), which the port requires on the document number and every product
/// name. Registered only by <see cref="C01E2eFixtureWriterActivation"/>; written by SİPARİŞ (s04), kept in this
/// file by CARİ so the activation stays named in this file and Program.cs only.
/// </summary>
public sealed class C01E2eSalesDraftFixtureWriter
{
    public const string LabelPrefix = "SENTETIK browser-suite:";

    private readonly ISalesDraftFixtureStore _store;

    public C01E2eSalesDraftFixtureWriter(
        IHostEnvironment environment,
        IConfiguration configuration,
        ISalesDraftFixtureStore store)
    {
        C01E2eFixtureWriterActivation.RequireGate(environment, configuration);
        _store = store;
    }

    public Task<SalesDraftFixtureOutcome> EnsureAsync(SalesDraftFixtureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Label.StartsWith(LabelPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"A fixture draft label must start '{LabelPrefix}'.");
        }

        return _store.EnsureAsync(request, cancellationToken);
    }
}

/// <summary>
/// Blocks ONE line of an ISSUED synthetic loading instruction through Logistics's fixture port (decision W1/C, l04):
/// the only suite state no LOG-4a route produces. The sibling of <see cref="C01E2eSalesDraftFixtureWriter"/>: constructing
/// it outside the gate fails, and the request's label must name its suite. Registered only by
/// <see cref="C01E2eFixtureWriterActivation"/>; written by SİPARİŞ (l04), kept in this file by CARİ.
/// </summary>
public sealed class C01E2eLogisticsLineBlockFixtureWriter
{
    public const string LabelPrefix = "SENTETIK browser-suite:";

    private readonly ILogisticsLineBlockFixtureStore _store;

    public C01E2eLogisticsLineBlockFixtureWriter(
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogisticsLineBlockFixtureStore store)
    {
        C01E2eFixtureWriterActivation.RequireGate(environment, configuration);
        _store = store;
    }

    public Task<LogisticsLineBlockFixtureOutcome> BlockAsync(LogisticsLineBlockFixtureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Label.StartsWith(LabelPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"A fixture block label must start '{LabelPrefix}'.");
        }

        return _store.BlockAsync(request, cancellationToken);
    }
}

/// <summary>
/// Exposes Accounting's fixed fin01 Open-period fixture only inside the browser runner's complete gate.
/// The owner port accepts no caller-selected identity, date, status or actor.
/// </summary>
public sealed class C01E2eAccountingOpenPeriodFixtureWriter
{
    private readonly IAccountingOpenPeriodFixtureStore _store;

    public C01E2eAccountingOpenPeriodFixtureWriter(
        IHostEnvironment environment,
        IConfiguration configuration,
        IAccountingOpenPeriodFixtureStore store)
    {
        C01E2eFixtureWriterActivation.RequireGate(environment, configuration);
        _store = store;
    }

    public Task EnsureAsync(CancellationToken cancellationToken) =>
        _store.EnsureAsync(cancellationToken);
}
