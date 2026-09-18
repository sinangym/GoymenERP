using GoymenERP.Modules.Product.Infrastructure.Fixtures;

namespace GoymenERP.Api.Shell;

/// <summary>
/// Writes an explicit synthetic Product code group through Product's fixture port (coordinator decision
/// W1/C, u02). The sibling of <see cref="C01E2eProductMasterAssignmentFixtureWriter"/>: constructing it
/// outside the gate fails even if someone registers it by hand, and the group's name must name its suite
/// (SENTETIK browser-suite:&lt;suite&gt; …), so every such row is traceable to its fixture. The host registers
/// it only through <see cref="C01E2eFixtureWriterActivation"/> (the central writer's).
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
