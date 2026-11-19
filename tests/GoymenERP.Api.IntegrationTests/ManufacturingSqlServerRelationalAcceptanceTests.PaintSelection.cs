using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Paint_owner_references_survive_immutable_revisions_and_down_refuses_to_discard_their_proof()
    {
        await using var run = await Run.StartAsync();
        // Existing non-Paint and legacy free-text plans are harmless to this additive snapshot rollback.
        var guard = Assert.IsType<SqlOperation>(new AddManufacturingPaintSelections().DownOperations[0]).Sql;
        Assert.Equal(1, await run.ScalarAsync<int>(guard + "; SELECT 1"));
        var workCenter = await run.WorkCenterOfKindAsync(CompanyA, "Paint");
        var choice = new PlanPaintSelectionV1(Guid.NewGuid(), "12", "3", "synthetic-import:3",
            Guid.NewGuid(), Guid.NewGuid(), "4", "7");
        var plan = new ProductionPlan(Guid.NewGuid(), CompanyA, workCenter, ManufacturingProcess.Paint,
            [new PlannedInput(RollA, Kg(1000m), 17)], processSpec: new(paintSelection: choice));
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
            await store.Revisions.RevisePlanContentAsync(plan.Id, 1, workCenter, ManufacturingProcess.Paint,
                plan.Inputs, null, null, null, "Boya kaynağı sürümü yenilendi", Metadata(run),
                processSpec: new(paintSelection: choice with { MembershipVersion = "4" }));
        }
        await using (var store = run.Store(CompanyA))
        {
            Assert.Equal(choice with { MembershipVersion = "4" }, (await store.Plans.FindPlanAsync(plan.Id))!.ProcessSpec!.PaintSelection);
            var history = (await store.Revisions.ListPlanRevisionsAsync(plan.Id))!;
            Assert.Equal(choice, history[0].ProcessSpec!.PaintSelection);
            Assert.Equal(choice with { MembershipVersion = "4" }, history[1].ProcessSpec!.PaintSelection);
        }
        await using (var other = run.Store(CompanyB)) Assert.Null(await other.Plans.FindPlanAsync(plan.Id));
        await using (var context = run.Context(CompanyA))
        {
            var migrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            var index = Array.FindIndex(migrations, id => id.EndsWith("_AddManufacturingPaintSelections", StringComparison.Ordinal));
            Assert.True(index > 0);
            // Exercise this historical migration's own populated guard before any newer guard can intercept it.
            var error = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "; SELECT 1"));
            Assert.Equal(51976, error.Number);
            Assert.Equal(migrations, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Plans WHERE Id=@id AND PaintSelectionJson IS NOT NULL", ("@id", plan.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanRevisions WHERE PlanId=@id AND PaintSelectionJson IS NOT NULL", ("@id", plan.Id)));
    }
}
