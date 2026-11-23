using System.Data;
using GoymenERP.BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Extends the existing owner SQL facts; it does not introduce a new discovered test or change a database option.</summary>
internal static class OutboxReadIsolationAssertions
{
    internal static async Task VerifyAsync(DbContext database, IWorkOutboxEventSourceV1 source, string owner)
    {
        Assert.Null(System.Transactions.Transaction.Current);
        Assert.Null(database.Database.CurrentTransaction);
        var expected = await source.ReadAfterAsync(null, 100, default);
        await database.Database.OpenConnectionAsync();
        try
        {
            // A retained session setting must not leak into the source's owned read boundary.
            await database.Database.ExecuteSqlRawAsync("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE");
            var owned = await source.ReadAfterAsync(null, 100, default);
            Assert.Equal(expected.Select(x => (x.EventId, x.Position)), owned.Select(x => (x.EventId, x.Position)));
            Assert.Null(database.Database.CurrentTransaction);

            await using (var host = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
            {
                var borrowed = await source.ReadAfterAsync(null, 100, default);
                Assert.Equal(expected.Select(x => (x.EventId, x.Position)), borrowed.Select(x => (x.EventId, x.Position)));
                Assert.Same(host, database.Database.CurrentTransaction);
                Assert.NotNull(host.GetDbTransaction().Connection);
                await host.RollbackAsync();
            }

            foreach (var isolation in new[] { IsolationLevel.Serializable, IsolationLevel.Snapshot })
            {
                await using var host = await database.Database.BeginTransactionAsync(isolation);
                // The reader must refuse BEFORE its first data query; this probe never changes a database option.
                var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadAfterAsync(null, 100, default));
                Assert.Equal(owner + " outbox reads require a READ COMMITTED transaction.", refused.Message);
                Assert.Same(host, database.Database.CurrentTransaction);
                Assert.Equal(isolation, host.GetDbTransaction().IsolationLevel);
                Assert.NotNull(host.GetDbTransaction().Connection);
                await host.RollbackAsync();
            }

            using (var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
            {
                var host = System.Transactions.Transaction.Current;
                var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadAfterAsync(null, 100, default));
                Assert.Equal(owner + " outbox reads cannot borrow an ambient transaction.", refused.Message);
                Assert.Same(host, System.Transactions.Transaction.Current);
                Assert.Null(database.Database.CurrentTransaction);
            }
        }
        finally
        {
            // Test-only session setup never escapes into the next pooled fixture connection.
            await database.Database.ExecuteSqlRawAsync("SET TRANSACTION ISOLATION LEVEL READ COMMITTED");
            await database.Database.CloseConnectionAsync();
        }
    }
}
