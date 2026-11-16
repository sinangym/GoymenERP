using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Persisted_priority_orders_equal_due_dates_and_a_stale_plan_change_appends_no_priority_or_audit()
    {
        await using var run = await Run.StartAsync();
        var (first, _) = await run.ApprovedOrderAsync(CompanyA);
        var (second, _) = await run.ApprovedOrderAsync(CompanyA);
        var target = NewPlan(CompanyA, first.WorkCenterId);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Plans.SavePlanAsync(target, Metadata(run));
            await ((IManufacturingPlanPriorityStore)scope.Corrections).ChangePlanPriorityAsync(second.Id, second.Version, 0, 10,
                "Synthetic priority first", Metadata(run));
        }
        await using var fresh = run.Store(CompanyA);
        var queue = await ((IManufacturingWorkCenterQueueStore)fresh.Corrections).ReadWorkCenterQueueAsync(first.WorkCenterId, target.Id);
        var ordered = ProductionWorkCenterQueueOrderV1.Order(queue.Queued);
        Assert.True(ordered.ToList().FindIndex(entry => entry.PlanId == second.Id) < ordered.ToList().FindIndex(entry => entry.PlanId == first.Id));
        Assert.Equal(1L, ordered.Single(entry => entry.PlanId == second.Id).PriorityRevision);
        var failed = await Assert.ThrowsAsync<ManufacturingVersionConflictException>(() =>
            ((IManufacturingPlanPriorityStore)fresh.Corrections).ChangePlanPriorityAsync(second.Id, second.Version - 1, 1, 12,
                "Synthetic stale content", Metadata(run)));
        Assert.Equal(second.Version, failed.CurrentVersion);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanPriorityVersions WHERE CompanyId=@company AND PlanId=@plan",
            ("@company", CompanyA), ("@plan", second.Id)));
    }

    [SqlServerFact]
    public async Task The_priority_Down_guard_refuses_without_losing_recorded_priority_history()
    {
        await using var run = await Run.StartAsync();
        var type = typeof(ManufacturingDbContext).Assembly.GetTypes().Single(type => typeof(Migration).IsAssignableFrom(type)
            && type.Name == "AddManufacturingPlanPriorities");
        var migration = (Migration)Activator.CreateInstance(type)!;
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Equal(1, await run.ScalarAsync<int>(guard + "; SELECT 1"));
        var (plan, _) = await run.ApprovedOrderAsync(CompanyA);
        await using (var scope = run.Store(CompanyA))
            await ((IManufacturingPlanPriorityStore)scope.Corrections).ChangePlanPriorityAsync(plan.Id, plan.Version, 0, 4,
                "Synthetic guard proof", Metadata(run));
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "; SELECT 1"));
        Assert.Equal(51943, refused.Number);
        Assert.Equal(4, await run.ScalarAsync<int>("SELECT Score FROM manufacturing.PlanPriorityVersions WHERE CompanyId=@company AND PlanId=@plan AND Revision=1",
            ("@company", CompanyA), ("@plan", plan.Id)));
    }
}
