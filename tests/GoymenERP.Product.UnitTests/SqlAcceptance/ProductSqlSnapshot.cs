using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Product.UnitTests.SqlAcceptance;

internal sealed record CodeGroupState(Guid Id, string Name, string Prefix);
internal sealed record CodeCounterState(string Prefix, long LastSequence);
internal sealed record CodeRegistrationState(string Code, Guid ProductId, bool IsCurrent);

// Observes only Product's four tables. Audit/terminal evidence remains with their
// accepted owner fixture. A constructed snapshot is not evidence of a SQL run.
internal sealed record ProductSqlSnapshot(
    IReadOnlyList<ProductStoredRecord> Products,
    IReadOnlyList<CodeGroupState> Groups,
    IReadOnlyList<CodeCounterState> Counters,
    IReadOnlyList<CodeRegistrationState> Registrations)
{
    public static async Task<ProductSqlSnapshot> ReadAsync(ProductSqlFixture fixture,
        SqlConnection connection, SqlTransaction? transaction, CancellationToken cancellationToken)
    {
        await fixture.VerifyDatabaseAsync(connection, transaction, cancellationToken);
        var options = new DbContextOptionsBuilder<ProductDbContext>()
            .UseSqlServer(connection, contextOwnsConnection: false).Options;
        await using var context = new ProductDbContext(options);
        if (transaction is not null)
            await context.Database.UseTransactionAsync(transaction, cancellationToken);

        // Caller holds the owner transaction or awaits all writers before and
        // after the scenario. These sequential reads are not a concurrency barrier.
        var products = await context.Products.AsNoTracking().ToArrayAsync(cancellationToken);
        var groups = await context.CodeGroups.AsNoTracking().ToArrayAsync(cancellationToken);
        var counters = await context.CodeCounters.AsNoTracking().ToArrayAsync(cancellationToken);
        var registrations = await context.CodeRegistrations.AsNoTracking().ToArrayAsync(cancellationToken);
        return new(products.Select(row => row.ToRecord()).OrderBy(row => row.Id).ToArray(),
            groups.Select(row => new CodeGroupState(row.Id, row.Name, row.Prefix)).OrderBy(row => row.Id).ToArray(),
            counters.Select(row => new CodeCounterState(row.Prefix, row.LastSequence)).OrderBy(row => row.Prefix, StringComparer.Ordinal).ToArray(),
            registrations.Select(row => new CodeRegistrationState(row.Code, row.ProductId, row.IsCurrent)).OrderBy(row => row.Code, StringComparer.Ordinal).ToArray());
    }
}
