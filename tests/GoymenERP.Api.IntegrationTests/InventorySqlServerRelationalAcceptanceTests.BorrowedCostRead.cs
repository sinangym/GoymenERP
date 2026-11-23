using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Borrowed_cost_reader_observes_uncommitted_issue_and_preserves_host_handles_for_rollback()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var label = await CostedLabelAsync(run, location, 100m, 10m, 2m);
        var request = IssueRequest(Guid.NewGuid(), label.Id, "2", "10");
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var physical = await InventoryManufacturingRealizationParticipant.EnlistAsync(connection, transaction,
            CompanyA, ManufacturingSettings(run));
        await physical.StageInputIssueAsync(request, ManufacturingMetadata(run,"borrowed-read"));
        var company = new CompanyContext(); company.Set(CompanyA);
        await using (var reader = await InventoryManufacturingCostReadSession.EnlistAsync(connection, transaction, company))
        {
            var actual = await reader.CostMovements.GetAsync(CompanyA, request.ManufacturingOrderId);
            Assert.Equal(InventoryManufacturingCostReadOutcomeV1.Available, actual.Outcome);
            Assert.Equal(request.MovementId, Assert.Single(actual.Issues).MovementId);
            var rate = await reader.FirstReceiptRates.GetAsync(CompanyA, label.Id);
            Assert.Equal(CompanyA,rate.CompanyId);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.CostMovements.GetAsync(CompanyB,request.ManufacturingOrderId));
        }
        Assert.Equal(System.Data.ConnectionState.Open,connection.State);
        Assert.Same(connection,transaction.Connection);
        await transaction.RollbackAsync();
        await using var final = run.Context(CompanyA);
        Assert.False(await final.Movements.AnyAsync(x => x.Id == request.MovementId));
        Assert.Equal(100m,await final.Balances.Where(x => x.LabelId == label.Id).SumAsync(x => x.QuantityKg));
    }
}
