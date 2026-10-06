using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Persistence-only commercial-context evidence against real SQL Server. It opens no customer
/// admission path: plan, revision and order snapshots are built directly through the store.
/// </summary>
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    private const string BeforeCommercialContexts = "20261001200000_AddManufacturingPlanTechnicalSnapshots";

    [SqlServerFact]
    public async Task Commercial_context_round_trips_exactly_through_plan_revision_and_order_while_legacy_stays_null()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var context = CommercialContext();
        var plan = new ProductionPlan(Guid.NewGuid(), CompanyA, workCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, Kg(10_000m), 7, commercialContext: context)]);
        var orderId = Guid.NewGuid();

        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
            await store.Revisions.RevisePlanAsync(plan.Id, 1, workCenter, ManufacturingProcess.Slitting,
                [new PlannedInput(RollA, Kg(9_000m), 8, commercialContext: context)], null,
                "ticari bağlam aynı kayıt", Metadata(run));
            await store.Plans.ApprovePlanAsync(plan.Id, 2, run.Actor, At, Metadata(run));
            await store.Orders.CreateOrderAsync(orderId, plan.Id, Metadata(run));
        }

        await using (var store = run.Store(CompanyA))
        {
            Assert.Equal(context, Assert.Single((await store.Plans.FindPlanAsync(plan.Id))!.Inputs).CommercialContext);
            var history = (await store.Revisions.ListPlanRevisionsAsync(plan.Id))!;
            Assert.Equal(context, Assert.Single(history[0].Inputs).CommercialContext);
            Assert.Equal(context, Assert.Single(history[1].Inputs).CommercialContext);
            Assert.Equal(context, Assert.Single((await store.Orders.FindOrderAsync(orderId))!.PlannedInputs).CommercialContext);
        }

        Assert.Equal((context.PartyVersion, context.AccountVersion),
            (await run.ScalarAsync<string>(
                "SELECT CommercialPartyVersion FROM manufacturing.OrderInputs WHERE OrderId = @id", ("@id", orderId)),
             await run.ScalarAsync<string>(
                "SELECT CommercialAccountVersion FROM manufacturing.OrderInputs WHERE OrderId = @id", ("@id", orderId))));

        var legacy = new ProductionPlan(Guid.NewGuid(), CompanyA, workCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollB, Kg(1_000m), 1)]);
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(legacy, Metadata(run));
        }
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanInputs WHERE PlanId = @id "
            + "AND CommercialPartyId IS NULL AND CommercialCompanyPartyAccountId IS NULL "
            + "AND CommercialPartyVersion IS NULL AND CommercialAccountVersion IS NULL", ("@id", legacy.Id)));
    }

    [SqlServerFact]
    public async Task Partial_empty_or_zero_commercial_context_is_rejected_in_all_three_tables_without_mutation()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var context = CommercialContext();
        var plan = new ProductionPlan(Guid.NewGuid(), CompanyA, workCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, Kg(10_000m), 7, commercialContext: context)]);
        var orderId = Guid.NewGuid();
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
            await store.Revisions.RevisePlanAsync(plan.Id, 1, workCenter, ManufacturingProcess.Slitting,
                [new PlannedInput(RollA, Kg(9_000m), 8, commercialContext: context)], null,
                "constraint", Metadata(run));
            await store.Plans.ApprovePlanAsync(plan.Id, 2, run.Actor, At, Metadata(run));
            await store.Orders.CreateOrderAsync(orderId, plan.Id, Metadata(run));
        }

        await using var db = run.Context(CompanyA);
        await Refused("UPDATE manufacturing.PlanInputs SET CommercialPartyVersion = NULL WHERE PlanId = {0}", plan.Id,
            "CK_PlanInputs_CommercialContext");
        await Refused("UPDATE manufacturing.PlanRevisionInputs SET CommercialAccountVersion = N'' WHERE PlanId = {0}",
            plan.Id, "CK_PlanRevisionInputs_CommercialContext");
        await Refused("UPDATE manufacturing.OrderInputs SET CommercialPartyId = '00000000-0000-0000-0000-000000000000' "
            + "WHERE OrderId = {0}", orderId, "CK_OrderInputs_CommercialContext");

        Assert.Equal((context.PartyId, context.PartyVersion, context.AccountVersion),
            (await run.ScalarAsync<Guid>(
                "SELECT CommercialPartyId FROM manufacturing.PlanInputs WHERE PlanId = @id", ("@id", plan.Id)),
             await run.ScalarAsync<string>(
                "SELECT CommercialPartyVersion FROM manufacturing.PlanRevisionInputs WHERE PlanId = @id AND RevisionNo = 1",
                ("@id", plan.Id)),
             await run.ScalarAsync<string>(
                "SELECT CommercialAccountVersion FROM manufacturing.OrderInputs WHERE OrderId = @id", ("@id", orderId))));

        async Task Refused(string sql, Guid id, string constraint)
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql, id));
            Assert.Equal(547, error.Number);
            Assert.Contains(constraint, error.Message, StringComparison.Ordinal);
        }
    }

    [SqlServerFact]
    public async Task Commercial_context_migration_round_trips_only_when_all_history_is_empty()
    {
        await using var run = await Run.StartAsync();
        await using var context = run.Context(CompanyA);
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeCommercialContexts);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id IN "
            + "(OBJECT_ID(N'manufacturing.PlanInputs'), OBJECT_ID(N'manufacturing.PlanRevisionInputs'), "
            + "OBJECT_ID(N'manufacturing.OrderInputs')) AND [name] = N'CommercialPartyId'"));

        await context.Database.MigrateAsync();
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.check_constraints WHERE [name] IN "
            + "(N'CK_PlanInputs_CommercialContext', N'CK_PlanRevisionInputs_CommercialContext', "
            + "N'CK_OrderInputs_CommercialContext')"));
    }

    [SqlServerFact]
    public async Task Commercial_context_migration_refuses_down_before_losing_any_plan_revision_or_order_history()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var snapshot = CommercialContext();
        var plan = new ProductionPlan(Guid.NewGuid(), CompanyA, workCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(RollA, Kg(10_000m), 7, commercialContext: snapshot)]);
        var orderId = Guid.NewGuid();
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
            await store.Revisions.RevisePlanAsync(plan.Id, 1, workCenter, ManufacturingProcess.Slitting,
                [new PlannedInput(RollA, Kg(9_000m), 8, commercialContext: snapshot)], null,
                "downgrade refusal", Metadata(run));
            await store.Plans.ApprovePlanAsync(plan.Id, 2, run.Actor, At, Metadata(run));
            await store.Orders.CreateOrderAsync(orderId, plan.Id, Metadata(run));
        }

        await using var context = run.Context(CompanyA);
        var error = await Assert.ThrowsAsync<SqlException>(
            () => context.GetService<IMigrator>().MigrateAsync(BeforeCommercialContexts));

        Assert.Equal(51047, error.Number);
        Assert.EndsWith("_AddManufacturingCommercialContexts",
            (await context.Database.GetAppliedMigrationsAsync()).Last(), StringComparison.Ordinal);
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT (SELECT COUNT(*) FROM manufacturing.PlanInputs WHERE CommercialPartyId IS NOT NULL) "
            + "+ (SELECT COUNT(*) FROM manufacturing.PlanRevisionInputs WHERE CommercialPartyId IS NOT NULL) "
            + "+ (SELECT COUNT(*) FROM manufacturing.OrderInputs WHERE CommercialPartyId IS NOT NULL)"));
    }

    private static PlanCommercialContextSnapshotV1 CommercialContext() => new(
        Guid.Parse("72000000-0000-4000-8000-000000000001"),
        Guid.Parse("72000000-0000-4000-8000-000000000002"),
        " party:v01 ",
        "account:v02/α");
}
