using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Real SQL rowversion ordering and committed horizon. No dispatcher, notification or accounting activation. NOT_RUN.</summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Inventory_outbox_cursor_never_passes_an_earlier_open_producer_and_pages_only_the_ambient_company()
    {
        await using var run = await Run.StartAsync();
        await using var firstConnection = new SqlConnection(run.Factory.DatabaseConnectionString); await firstConnection.OpenAsync();
        await using var firstTransaction = (SqlTransaction)await firstConnection.BeginTransactionAsync();
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var other = Guid.NewGuid();
        await InsertOutboxSourceEventAsync(firstConnection, firstTransaction, first, CompanyA,
            InventoryFutureAllocationReleasedOutboxV1.EventType);
        await using (var later = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await later.OpenAsync();
            await InsertOutboxSourceEventAsync(later, null, second, CompanyA, InventoryOutboxContractV1.ManufacturingCostDifferenceProposed);
            await InsertOutboxSourceEventAsync(later, null, other, CompanyB, InventoryFutureAllocationReleasedOutboxV1.EventType);
        }
        await using var context = run.Context(CompanyA);
        var source = new InventoryOutboxEventSource(context, Company(CompanyA));
        // The later row is committed, but returning it would skip the earlier producer after its eventual commit.
        Assert.Empty(await source.ReadAfterAsync(null, 10, CancellationToken.None));
        await firstTransaction.CommitAsync();
        var page = await source.ReadAfterAsync(null, 1, CancellationToken.None);
        Assert.Equal(first, Assert.Single(page).EventId);
        Assert.Equal(20, page[0].Position.Length);
        var next = await source.ReadAfterAsync(page[0].Position, 10, CancellationToken.None);
        Assert.Equal(second, Assert.Single(next).EventId);
        Assert.All(page.Concat(next), row => Assert.Equal(CompanyA, row.CompanyId));
        Assert.Empty(await source.ReadAfterAsync(next[0].Position, 10, CancellationToken.None));
        await using var otherContext = run.Context(CompanyB);
        Assert.Equal(other, Assert.Single(await new InventoryOutboxEventSource(otherContext, Company(CompanyB))
            .ReadAfterAsync(null, 10, CancellationToken.None)).EventId);
    }

    [SqlServerFact]
    public async Task Rolling_back_the_earlier_outbox_producer_releases_the_committed_horizon_without_an_invented_event()
    {
        await using var run = await Run.StartAsync();
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString); await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await InsertOutboxSourceEventAsync(connection, transaction, Guid.NewGuid(), CompanyA,
            InventoryFutureAllocationReleasedOutboxV1.EventType);
        var committed = Guid.NewGuid();
        await using (var later = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await later.OpenAsync();
            await InsertOutboxSourceEventAsync(later, null, committed, CompanyA, InventoryOutboxContractV1.ManufacturingCostDifferenceProposed);
            await InsertOutboxSourceEventAsync(later, null, Guid.NewGuid(), CompanyA, "synthetic.unregistered-event.v1");
        }
        await transaction.RollbackAsync();
        await using var context = run.Context(CompanyA);
        var result = await new InventoryOutboxEventSource(context, Company(CompanyA)).ReadAfterAsync(null, 10, CancellationToken.None);
        Assert.Equal(committed, Assert.Single(result).EventId);
    }

    private static async Task InsertOutboxSourceEventAsync(SqlConnection connection, SqlTransaction? transaction,
        Guid id, Guid company, string eventType)
    {
        await using var command = new SqlCommand("""
            INSERT INTO inventory.OutboxMessages
                (Id,CompanyId,EventType,SchemaVersion,SourceOperation,AggregateType,AggregateId,AggregateVersion,Sequence,
                 PayloadJson,OccurredAtUtc,CorrelationId,CausationId,RecordedAtUtc)
            VALUES (@id,@company,@type,1,'synthetic.outbox','Synthetic.Outbox',@id,1,1,
                '{}',SYSUTCDATETIME(),'SENTETIK-outbox',NULL,SYSUTCDATETIME());
            """, connection, transaction);
        command.Parameters.AddWithValue("@id", id); command.Parameters.AddWithValue("@company", company);
        command.Parameters.AddWithValue("@type", eventType); await command.ExecuteNonQueryAsync();
    }
}
