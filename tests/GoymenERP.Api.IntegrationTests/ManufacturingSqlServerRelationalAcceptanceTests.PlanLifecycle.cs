using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Üretim U2-a at the store on a REAL SQL Server: the rejection is append-only and the plan is rebuilt through
/// interleaved revisions and rejections; the lifecycle migration travels Down and Up while no lifecycle content exists,
/// refuses Down (51187) once a plan is cancelled, and refuses Up (51188) while a plan already has two orders.
/// </summary>
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    private const string BeforePlanLifecycle = "20261102121844_AddManufacturingPlanParameterSnapshots";

    [SqlServerFact]
    public async Task Rejections_and_revisions_interleave_and_the_plan_is_rebuilt_through_both()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var plan = NewPlan(CompanyA, workCenter);
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
            await store.Lifecycle.RejectPlanAsync(plan.Id, 1, run.Actor, At, "bir", Metadata(run));
            await store.Revisions.RevisePlanAsync(plan.Id, 2, workCenter, ManufacturingProcess.Slitting,
                [new PlannedInput(RollB, Kg(4_000m), 5)], null, "düzeltildi", Metadata(run));
            await store.Lifecycle.RejectPlanAsync(plan.Id, 3, run.Actor, At, "iki", Metadata(run));
        }

        await using (var store = run.Store(CompanyA))
        {
            var stored = (await store.Plans.FindPlanAsync(plan.Id))!;
            Assert.Equal((4L, PlanStatus.Draft, 1), (stored.Version, stored.Status, stored.RevisionCount));
            Assert.Equal([(1, "bir", 2L), (2, "iki", 4L)],
                stored.Rejections.Select(rejection => (rejection.Number, rejection.Reason, rejection.PlanVersion)));
            var approved = await store.Plans.ApprovePlanAsync(plan.Id, 4, run.Actor, At, Metadata(run));
            Assert.Equal(5L, approved.Version);
        }

        // A rejection row is append-only: the write guard refuses any change of it.
        await using (var context = run.Context(CompanyA))
        {
            var row = await context.PlanRejections.FirstAsync(rejection => rejection.PlanId == plan.Id);
            row.Reason = "değişti";
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.plan.rejected.v1' AND EntityId = @p",
            ("@p", plan.Id.ToString("D"))));
    }

    [SqlServerFact]
    public async Task The_lifecycle_migration_rolls_back_only_without_lifecycle_content_and_refuses_two_orders_per_plan()
    {
        await using var run = await Run.StartAsync();

        // An approved plan with its one order travels Down and Up cleanly.
        var (plan, orderId) = await run.ApprovedOrderAsync(CompanyA);
        await using (var context = run.Context(CompanyA))
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(BeforePlanLifecycle);
            Assert.Equal(BeforePlanLifecycle, (await context.Database.GetAppliedMigrationsAsync()).Last());

            // Before U2-a a plan could open a second order; the upgrade then refuses and moves nothing.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO [manufacturing].[Orders] (Id, CompanyId, PlanId, WorkCenterId, Process, Status, Version, CreatedAtUtc, UpdatedAtUtc)
                SELECT NEWID(), CompanyId, PlanId, WorkCenterId, Process, Status, 1, CreatedAtUtc, UpdatedAtUtc
                FROM [manufacturing].[Orders] WHERE [Id] = {orderId}
                """);
            var refused = await Assert.ThrowsAsync<SqlException>(() => context.Database.MigrateAsync());
            Assert.Equal(51188, refused.Number);
            Assert.Equal(BeforePlanLifecycle, (await context.Database.GetAppliedMigrationsAsync()).Last());

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [manufacturing].[Orders] WHERE [PlanId] = {plan.Id} AND [Id] <> {orderId}");
            await context.Database.MigrateAsync();
            Assert.Contains(await context.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddManufacturingPlanLifecycle", StringComparison.Ordinal));
        }

        // A cancelled plan is content the previous schema cannot hold: Down refuses and keeps everything.
        var draft = NewPlan(CompanyA, await run.WorkCenterOfAsync(CompanyA));
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(draft, Metadata(run));
            await store.Lifecycle.CancelPlanAsync(draft.Id, 1, run.Actor, At, "geri alma sınaması", Metadata(run));
        }

        await using (var context = run.Context(CompanyA))
        {
            var error = await Assert.ThrowsAsync<SqlException>(
                () => context.GetService<IMigrator>().MigrateAsync(BeforePlanLifecycle));
            Assert.Equal(51187, error.Number);
            Assert.Contains(await context.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddManufacturingPlanLifecycle", StringComparison.Ordinal));
        }

        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT [Status] FROM manufacturing.Plans WHERE Id = @p", ("@p", draft.Id)));
    }

    [SqlServerFact]
    public async Task A_cancelled_plan_opens_no_order_and_a_plan_with_an_order_is_not_cancelled_at_the_store()
    {
        await using var run = await Run.StartAsync();
        var (plan, _) = await run.ApprovedOrderAsync(CompanyA);
        await using (var store = run.Store(CompanyA))
        {
            var hasOrder = await Assert.ThrowsAsync<ManufacturingRuleException>(() =>
                store.Lifecycle.CancelPlanAsync(plan.Id, plan.Version, run.Actor, At, "iptal", Metadata(run)));
            Assert.Equal(ManufacturingRuleCodes.PlanHasOrder, hasOrder.Code);
            var second = await Assert.ThrowsAsync<ManufacturingRuleException>(() =>
                store.Orders.CreateOrderAsync(Guid.NewGuid(), plan.Id, Metadata(run)));
            Assert.Equal(ManufacturingRuleCodes.PlanOrderExists, second.Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Orders WHERE PlanId = @p", ("@p", plan.Id)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT [Status] FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id)));
    }
}
