using System.Data;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Historical_link_snapshot_reads_uncommitted_owner_links_on_the_borrowed_transaction_without_committing_it()
    {
        await using var run = await Run.StartAsync();
        var ids = FutureSupplyIds.New();
        await run.ExecuteAsync(InsertFutureSupplyLine, ids.Line(CompanyA, 100m, 0m));
        await using var database = run.Context(CompanyA);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var parameters = ids.Allocation(CompanyA, Guid.NewGuid(), 5m, 5m, 1)
            .Select(item => new SqlParameter(item.Name, item.Value)).Cast<object>().ToArray();
        await database.Database.ExecuteSqlRawAsync(InsertFutureSupplyAllocation, parameters);
        InventoryHistoricalSalesLinksAnswerV1 snapshot;
        await using (var reader = await InventoryHistoricalSalesLinksV1.EnlistAsync(database.Database.GetDbConnection(),
            transaction.GetDbTransaction(), CompanyA))
        {
            snapshot = await reader.ReadAsync(ids.PurchaseOrder, ids.PurchaseLine);
            Assert.Equal(InventoryHistoricalSalesLinksOutcomeV1.Available, snapshot.Outcome);
            Assert.Equal(new InventoryHistoricalSalesLineV1(ids.DemandOrder, ids.DemandLine), Assert.Single(snapshot.Lines));
        }
        Assert.NotNull(transaction.GetDbTransaction().Connection);
        await transaction.RollbackAsync();
        await using var after = run.Context(CompanyA);
        var current = await new InventoryHistoricalSalesLinksV1(after, Company(CompanyA)).ReadAsync(ids.PurchaseOrder, ids.PurchaseLine);
        Assert.Empty(current.Lines);
        Assert.Single(snapshot.Lines); // The captured notification evidence does not re-query mutable owner history.
    }

    [SqlServerFact]
    public async Task Historical_sales_links_include_released_and_consumed_rows_without_inventing_or_reactivating_links_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var first = FutureSupplyIds.New();
        var second = first with { DemandLine = Guid.NewGuid() };
        await run.ExecuteAsync(InsertFutureSupplyLine, first.Line(CompanyA, 100m, 2m));
        var released = Guid.NewGuid();
        await run.ExecuteAsync(InsertFutureSupplyAllocation, first.Allocation(CompanyA, released, 5m, 5m, 1));
        await run.ExecuteAsync("""
            UPDATE inventory.FutureSupplyAllocations SET RemainingQuantity=0,ReleasedQuantity=5,Status=3,
                ReleaseReasonCode='purchase-line-closed',ReleaseOrigin='purchasing',ReleaseReason=N'SENTETIK bırakma',ReleasedAtUtc=@at
            WHERE Id=@id AND CompanyId=@company;
            """, ("@at", At), ("@id", released), ("@company", CompanyA));
        await run.ExecuteAsync(InsertFutureSupplyAllocation, first.Allocation(CompanyA, Guid.NewGuid(), 3m, 0m, 2));
        await run.ExecuteAsync(InsertFutureSupplyAllocation, second.Allocation(CompanyA, Guid.NewGuid(), 2m, 2m, 1));
        await using var database = run.Context(CompanyA);
        var port = new InventoryHistoricalSalesLinksV1(database, Company(CompanyA));
        var result = await port.ReadAsync(first.PurchaseOrder, first.PurchaseLine);
        Assert.Equal(InventoryHistoricalSalesLinksOutcomeV1.Available, result.Outcome);
        Assert.Equal((CompanyA, first.PurchaseOrder, first.PurchaseLine), (result.CompanyId, result.PurchaseOrderId, result.PurchaseOrderLineId));
        Assert.Equal(new[] { new InventoryHistoricalSalesLineV1(first.DemandOrder, first.DemandLine), new(first.DemandOrder, second.DemandLine) }
            .OrderBy(row => row.SalesOrderId).ThenBy(row => row.SalesOrderLineId), result.Lines);
        var empty = await port.ReadAsync(Guid.NewGuid(), first.PurchaseLine);
        Assert.Equal(InventoryHistoricalSalesLinksOutcomeV1.Available, empty.Outcome);
        Assert.Empty(empty.Lines);
        await using var foreign = run.Context(CompanyB);
        var other = await new InventoryHistoricalSalesLinksV1(foreign, Company(CompanyB)).ReadAsync(first.PurchaseOrder, first.PurchaseLine);
        Assert.Equal(InventoryHistoricalSalesLinksOutcomeV1.Available, other.Outcome);
        Assert.Empty(other.Lines);
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT [Status] FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", released)));
        Assert.Equal(0m, await run.ScalarAsync<decimal>("SELECT [RemainingQuantity] FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", released)));
    }
}
