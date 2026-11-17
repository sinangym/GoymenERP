using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Subcontract_source_Down_allows_a_legacy_allocation_and_refuses_51934_once_source_evidence_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddInventorySubcontractDeliverySources().DownOperations[0]).Sql;
        var saved = await SeedAllocationAsync(run, "SENTETIK-SUBCONTRACT-DOWN");
        await run.ScalarAsync<int>(guard + " SELECT 1");
        await using (var context = run.Context(CompanyA))
        {
            // This fact isolates rollback preservation, not admission of an actual customer delivery.
            context.SubcontractAllocationSources.Add(new InventorySubcontractAllocationSourceRow
            {
                CompanyId = CompanyA, AllocationId = saved.AllocationId, LabelId = saved.LabelId,
                CustomerPartyId = Guid.NewGuid(), CustomerAccountId = Guid.NewGuid(), SalesOrderId = Guid.NewGuid(),
                SalesOrderLineId = Guid.NewGuid(), SalesOrderVersion = 1, SalesLineRevision = 0,
                ManufacturingOrderId = Guid.NewGuid(), ManufacturingOrderVersion = 1,
                ManufacturingPlanId = Guid.NewGuid(), ManufacturingPlanVersion = 1, OutputFactId = Guid.NewGuid(),
                OutputNetKg = 80m, SourceJson = "{}", SourceHash = new string('a', 64),
            });
            await context.SaveChangesAsync();
        }
        var refusal = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + " SELECT 1"));
        Assert.Equal(51934, refusal.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.SubcontractAllocationSources WHERE CompanyId=@company AND AllocationId=@id",
            ("@company", CompanyA), ("@id", saved.AllocationId)));
    }
}
