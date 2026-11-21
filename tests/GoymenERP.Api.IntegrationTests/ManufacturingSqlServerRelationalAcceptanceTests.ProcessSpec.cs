using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Üretim U2-b1 at the store on a REAL SQL Server: the process spec migration travels Down and Up while no plan carries a
/// process value, and refuses Down (51189) once one does, keeping everything; the stored spec rebuilds exactly.
/// </summary>
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    private const string BeforePlanProcessSpecs = "20261102181335_AddManufacturingPlanLifecycle";

    [SqlServerFact]
    public async Task The_process_spec_migration_rolls_back_only_without_process_values()
    {
        await using var run = await Run.StartAsync();

        // An empty set of process values travels Down and Up cleanly.
        await using (var context = run.Context(CompanyA))
        {
            var migrator = context.GetService<IMigrator>();
            var latestHistory = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            await migrator.MigrateAsync(BeforePlanProcessSpecs);
            Assert.Equal(BeforePlanProcessSpecs, (await context.Database.GetAppliedMigrationsAsync()).Last());
            await context.Database.MigrateAsync();
            Assert.Equal(latestHistory, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        }

        // A Tube / Profile plan with its entered values is content the previous schema cannot hold.
        var tube = await run.WorkCenterOfKindAsync(CompanyA, "TubeProfile");
        var spec = new PlanProcessSpec(packageCount: 4, piecesPerPackage: 36, pieceLengthMm: 6_000m, unitKg: 7.2345m);
        var plan = new ProductionPlan(Guid.NewGuid(), CompanyA, tube, ManufacturingProcess.TubeProfile,
            [new PlannedInput(RollA, Kg(1_000m))], processSpec: spec);
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
        }

        await using (var store = run.Store(CompanyA))
        {
            Assert.Equal(spec, (await store.Plans.FindPlanAsync(plan.Id))!.ProcessSpec);
        }

        await using (var context = run.Context(CompanyA))
        {
            var history = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            var proof = await run.ScalarAsync<string>("SELECT * FROM manufacturing.Plans WHERE Id=@p FOR JSON PATH", ("@p", plan.Id));
            var guard = Assert.IsType<SqlOperation>(new AddManufacturingPlanProcessSpecs().DownOperations[0]);
            var error = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard.Sql + "; SELECT 1"));
            Assert.Equal(51189, error.Number);
            Assert.Equal(history, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
            Assert.Equal(proof, await run.ScalarAsync<string>("SELECT * FROM manufacturing.Plans WHERE Id=@p FOR JSON PATH", ("@p", plan.Id)));
        }

        Assert.Equal(7.2345m, await run.ScalarAsync<decimal>(
            "SELECT UnitKg FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id)));
    }
}
