using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Exact_cost_pool_forward_migration_Down_allows_empty_storage_and_refuses_a_real_movement_pool()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddInventoryExactCostPools().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        await CostedLabelAsync(run, await run.LocationOfAsync(CompanyA), 1m, 10m, 2m);
        var refusal = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51968, refusal.Number);
        await using var context = run.Context(CompanyA);
        Assert.Equal(2, await context.ExactCostPools.CountAsync());
        Assert.Equal(2, await context.MovementCostStamps.CountAsync());
        Assert.Single(await context.Movements.ToListAsync());
    }
}
