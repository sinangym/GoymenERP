using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Product.Infrastructure.Fixtures;

/// <summary>
/// Product's write port for an explicit synthetic code group (coordinator decision C, W1: a gated fixture
/// writer for an owner write port that has no endpoint — Product has no code-group management route and
/// "no default group or fake reference is seeded"). Product does not know the gate: the port is added to DI
/// only by the host's gated fixture-writer activation, and no route or seeder reaches it. It writes no
/// AuditEvent (decision C: fixture data is not business activity), and a code group is not a Product card.
/// </summary>
public interface IProductCodeGroupFixtureStore
{
    /// <summary>
    /// Adds the code group, or accepts an identical one already present (a re-run of the same fixture);
    /// a different group under the same id fails.
    /// </summary>
    Task EnsureAsync(ProductCodeGroupFixtureRow row, CancellationToken cancellationToken);
}

/// <summary>One explicit synthetic code group; <see cref="ProductCodeGroupRow.Define"/> validates every field.</summary>
public sealed record ProductCodeGroupFixtureRow(Guid Id, string Name, string Prefix);

public static class ProductCodeGroupFixtureStoreRegistration
{
    public static IServiceCollection AddProductCodeGroupFixtureStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IProductCodeGroupFixtureStore, ProductCodeGroupFixtureStore>();
        return services;
    }
}

internal sealed class ProductCodeGroupFixtureStore(ProductDbContext dbContext) : IProductCodeGroupFixtureStore
{
    public async Task EnsureAsync(ProductCodeGroupFixtureRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        var group = ProductCodeGroupRow.Define(row.Id, row.Name, row.Prefix);
        var existing = await dbContext.CodeGroups.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == group.Id, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.Name, group.Name, StringComparison.Ordinal)
                || !string.Equals(existing.Prefix, group.Prefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A different Product code group already uses this fixture id.");
            }

            return;
        }

        dbContext.CodeGroups.Add(group);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
