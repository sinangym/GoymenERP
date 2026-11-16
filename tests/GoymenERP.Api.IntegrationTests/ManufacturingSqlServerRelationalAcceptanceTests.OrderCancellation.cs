using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_order_cancellation_Down_passes_with_open_orders_and_refuses_without_losing_a_cancelled_order()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddManufacturingOrderCancellation().DownOperations[0]).Sql;
        Assert.Equal(1, await run.ScalarAsync<int>(guard + "; SELECT 1"));
        var (plan, orderId) = await run.ApprovedOrderAsync(CompanyA);
        Assert.Equal(1, await run.ScalarAsync<int>(guard + "; SELECT 1"));
        await using (var store = run.Store(CompanyA))
        {
            await store.Corrections.CancelOrderAsync(orderId, 1, run.Actor, At.AddHours(1),
                "SENTETIK cancellation Down guard", Metadata(run));
        }

        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "; SELECT 1"));
        Assert.Equal(51870, refused.Number);
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT Status FROM manufacturing.Orders WHERE Id = @id", ("@id", orderId)));
        await using var fresh = run.Store(CompanyA);
        var cancelled = (await fresh.Orders.FindOrderAsync(orderId))!;
        Assert.Equal("SENTETIK cancellation Down guard", cancelled.CancellationReason);
        var released = (await fresh.Plans.FindPlanAsync(plan.Id))!;
        Assert.Null(released.OrderId);
        Assert.True(released.RequiresReapproval);
        Assert.Equal(2L, cancelled.Version);
    }
}
