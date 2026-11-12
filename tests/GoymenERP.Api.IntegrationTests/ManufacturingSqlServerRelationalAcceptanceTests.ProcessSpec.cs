using GoymenERP.Modules.Manufacturing.Domain.Plans;
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
            await run.ForgetPlanPreparersBeforeRollbackAsync();
            await migrator.MigrateAsync(BeforePlanProcessSpecs);
            Assert.Equal(BeforePlanProcessSpecs, (await context.Database.GetAppliedMigrationsAsync()).Last());
            await context.Database.MigrateAsync();
            Assert.EndsWith("_AddManufacturingPlanSalesOrderLinks", (await context.Database.GetAppliedMigrationsAsync()).Last(),
                StringComparison.Ordinal);
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
            await run.ForgetPlanPreparersBeforeRollbackAsync();
            var error = await Assert.ThrowsAsync<SqlException>(
                () => context.GetService<IMigrator>().MigrateAsync(BeforePlanProcessSpecs));
            Assert.Equal(51189, error.Number);
            Assert.EndsWith("_AddManufacturingPlanProcessSpecs", (await context.Database.GetAppliedMigrationsAsync()).Last(),
                StringComparison.Ordinal);
        }

        Assert.Equal(7.2345m, await run.ScalarAsync<decimal>(
            "SELECT UnitKg FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id)));
    }
}
