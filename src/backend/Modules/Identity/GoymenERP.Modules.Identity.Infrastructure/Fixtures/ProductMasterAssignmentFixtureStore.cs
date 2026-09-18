using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Identity.Infrastructure.Fixtures;

/// <summary>
/// Identity's write port for an explicit synthetic ProductMasterAssignment (README.GroupSharedMaster
/// "Explicit Identity assignment": an isolated fixture may use explicit synthetic decision identities only
/// behind an exact source/run/database host gate). Identity does not know that gate: the port is added to
/// DI only by the host's gated fixture-writer activation (a boundary test pins the one call site), and
/// no route, grant API or seeder reaches it. It writes no AuditEvent (coordinator decision C: fixture data,
/// like the seeded actors, is not business activity).
/// </summary>
public interface IProductMasterAssignmentFixtureStore
{
    /// <summary>
    /// Adds the assignment, or accepts an identical one already present (a re-run of the same fixture);
    /// a different row under the same id fails.
    /// </summary>
    Task EnsureAsync(ProductMasterAssignmentFixtureRow row, CancellationToken cancellationToken);
}

/// <summary>One explicit synthetic assignment; the domain constructor validates every field.</summary>
public sealed record ProductMasterAssignmentFixtureRow(
    Guid Id,
    Guid UserId,
    Guid RoleId,
    ProductMasterOperation Operation,
    Guid IssuerUserId,
    Guid IssuerExternalIdentityId,
    Guid DecisionId,
    string DecisionReference,
    DateTimeOffset ValidFromUtc);

public static class ProductMasterAssignmentFixtureStoreRegistration
{
    public static IServiceCollection AddProductMasterAssignmentFixtureStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IProductMasterAssignmentFixtureStore, ProductMasterAssignmentFixtureStore>();
        return services;
    }
}

internal sealed class ProductMasterAssignmentFixtureStore(IdentityDbContext dbContext)
    : IProductMasterAssignmentFixtureStore
{
    public async Task EnsureAsync(ProductMasterAssignmentFixtureRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        var assignment = new ProductMasterAssignment(
            row.Id,
            row.UserId,
            row.RoleId,
            row.Operation,
            row.IssuerUserId,
            row.IssuerExternalIdentityId,
            row.DecisionId,
            row.DecisionReference,
            row.ValidFromUtc,
            validUntilUtc: null);
        var existing = await dbContext.Set<ProductMasterAssignment>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == row.Id, cancellationToken);
        if (existing is not null)
        {
            if (existing.UserId != row.UserId
                || existing.RoleId != row.RoleId
                || existing.Operation != row.Operation
                || existing.IssuerUserId != row.IssuerUserId
                || existing.IssuerExternalIdentityId != row.IssuerExternalIdentityId
                || existing.DecisionId != row.DecisionId
                || !string.Equals(existing.DecisionReference, row.DecisionReference, StringComparison.Ordinal)
                || existing.ValidFromUtc != row.ValidFromUtc
                || existing.ValidUntilUtc is not null
                || !existing.IsActive)
            {
                throw new InvalidOperationException(
                    "A different Product master assignment already uses this fixture id.");
            }

            return;
        }

        dbContext.Set<ProductMasterAssignment>().Add(assignment);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
