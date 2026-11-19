using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Purchasing_outbox_cursor_never_passes_an_earlier_uncommitted_revival()
    {
        await using var run = await Run.StartAsync();
        var first = await SeedApprovedAcceptanceOrderAsync(run);
        var second = await SeedApprovedAcceptanceOrderAsync(run);
        var firstAcceptance = Guid.NewGuid(); var secondAcceptance = Guid.NewGuid();
        await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(first, firstAcceptance, "4"), Envelope("horizon-source-a", "source-a"));
        await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(second, secondAcceptance, "4"), Envelope("horizon-source-b", "source-b"));
        foreach (var seed in new[] { first, second })
        {
            await using var stored = run.Store(CompanyA);
            var key = "horizon-cancel-" + seed.OrderId.ToString("N");
            await stored.Store.MutateOrderAsync(run.Envelope(key, "cancel"), AcceptanceHash(key), PurchasingCommands.OrderCancelV1, seed.OrderId,
                (order, now) => order.Cancel(order.Version, "canceller", "Cancel remainder", now),
                new PurchasingMutationAudit("purchasing.purchase-order.cancelled.v1", "Cancel", "Cancel remainder"));
        }
        await using var firstScope = run.Factory.Services.CreateAsyncScope();
        firstScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        await using var firstConnection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await firstConnection.OpenAsync();
        await using var firstTransaction = (DbTransaction)await firstConnection.BeginTransactionAsync();
        await using var firstOwner = await PurchaseReceiptAdjustmentParticipant.EnlistAsync(firstConnection, firstTransaction, CompanyA,
            firstScope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), firstScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
        await firstOwner.StageAsync(new(CompanyA, Guid.NewGuid(), first.OrderId, firstAcceptance, "5", "1", false, false,
            "Earlier open transaction", AcceptanceTime.AddMinutes(10), PurchaseReceiptAdjustmentCodesV1.CommandName, AcceptanceHash("horizon-a"),
            NotificationSnapshots: [new(CompanyA, first.OrderId, first.LineId, "5", [])]),
            Envelope("horizon-a", "revival-a"));
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var owner = await PurchaseReceiptAdjustmentParticipant.EnlistAsync(connection, transaction, CompanyA,
                scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
            await owner.StageAsync(new(CompanyA, Guid.NewGuid(), second.OrderId, secondAcceptance, "5", "1", false, false,
                "Later committed transaction", AcceptanceTime.AddMinutes(11), PurchaseReceiptAdjustmentCodesV1.CommandName, AcceptanceHash("horizon-b"),
                NotificationSnapshots: [new(CompanyA, second.OrderId, second.LineId, "5", [])]),
                Envelope("horizon-b", "revival-b"));
            await transaction.CommitAsync();
        }
        // A timestamp or identity cursor could publish B now and lose A permanently when A later commits.
        Assert.Empty(await ReadPurchasingOutboxAsync(run, CompanyA, verifyReadIsolation: true));
        await firstTransaction.CommitAsync();
        var published = await ReadPurchasingOutboxAsync(run, CompanyA);
        Assert.Equal(2, published.Count);
        Assert.Equal(new[] { "revival-a", "revival-b" }, published.Select(item => item.CorrelationId));
        Assert.True(StringComparer.Ordinal.Compare(published[0].Position, published[1].Position) < 0);
        var next = Assert.Single(await ReadPurchasingOutboxAsync(run, CompanyA, published[0].Position));
        Assert.Equal(published[1].EventId, next.EventId);
        Assert.Empty(await ReadPurchasingOutboxAsync(run, CompanyA, published[1].Position));
        Assert.Empty(await ReadPurchasingOutboxAsync(run, CompanyB));
    }
    private static async Task<IReadOnlyList<GoymenERP.BuildingBlocks.Messaging.WorkOutboxEventV1>> VerifyOutboxReadIsolationAsync(
        PurchasingDbContext database, ICompanyContext company, string? after, OutboxReadIsolationObserver observer)
    {
        Assert.Null(System.Transactions.Transaction.Current);
        Assert.Null(database.Database.CurrentTransaction);
        await database.Database.OpenConnectionAsync();
        await using (var rcsi = database.Database.GetDbConnection().CreateCommand())
        {
            rcsi.CommandText = "SELECT CONVERT(int, is_read_committed_snapshot_on) FROM sys.databases WHERE database_id = DB_ID()";
            Assert.True(Convert.ToInt32(await rcsi.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) == 1,
                "The outbox cursor probe requires READ_COMMITTED_SNAPSHOT ON in its actual database.");
        }
        // Test setup only: reproduce isolation retained from an earlier producer on this pinned session.
        await database.Database.ExecuteSqlRawAsync("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE");
        await using (var isolation = database.Database.GetDbConnection().CreateCommand())
        {
            isolation.CommandText = "SELECT CONVERT(int, transaction_isolation_level) FROM sys.dm_exec_sessions WHERE session_id = @@SPID";
            Assert.Equal(4, Convert.ToInt32(await isolation.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));
        }
        var source = new PurchasingOutboxEventSource(database, company);
        var result = await source.ReadAfterAsync(after, 100, default);
        Assert.Empty(result);
        observer.AssertObserved();
        Assert.Null(database.Database.CurrentTransaction);

        // A caller-owned compatible transaction stays usable until the caller rolls it back.
        await using (var host = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            Assert.Empty(await source.ReadAfterAsync(after, 100, default));
            Assert.Same(host, database.Database.CurrentTransaction);
            Assert.NotNull(host.GetDbTransaction().Connection);
            await host.RollbackAsync();
        }
        // Reject incompatible ownership before querying or changing the host's transaction.
        await using (var host = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var observed = observer.ObservedCount;
            await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadAfterAsync(after, 100, default));
            Assert.Equal(observed, observer.ObservedCount);
            Assert.Same(host, database.Database.CurrentTransaction);
            Assert.Equal(IsolationLevel.Serializable, host.GetDbTransaction().IsolationLevel);
            Assert.NotNull(host.GetDbTransaction().Connection);
            await host.RollbackAsync();
        }
        using (var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            var observed = observer.ObservedCount;
            await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadAfterAsync(after, 100, default));
            Assert.Equal(observed, observer.ObservedCount);
            Assert.Null(database.Database.CurrentTransaction);
            Assert.NotNull(System.Transactions.Transaction.Current);
        }
        observer.AssertObserved();
        return result;
    }

    private sealed class OutboxReadIsolationObserver : DbCommandInterceptor
    {
        private int horizonCount;
        private int outboxCount;
        public int ObservedCount => horizonCount + outboxCount;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            var horizon = command.CommandText.Contains("MIN_ACTIVE_ROWVERSION()", StringComparison.Ordinal);
            var outbox = command.CommandText.Contains("[purchasing].[OutboxMessages]", StringComparison.Ordinal);
            if (horizon || outbox)
            {
                Assert.NotNull(command.Transaction);
                Assert.Equal(IsolationLevel.ReadCommitted, command.Transaction.IsolationLevel);
                if (horizon) horizonCount++;
                if (outbox) outboxCount++;
            }
            return ValueTask.FromResult(result);
        }

        public void AssertObserved()
        {
            Assert.True(horizonCount > 0, "The actual outbox horizon command must be observed in READ COMMITTED.");
            Assert.True(outboxCount > 0, "The actual outbox row command must be observed in READ COMMITTED.");
        }
    }

}
