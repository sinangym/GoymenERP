using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Manual_queue_preserves_existing_order_across_key_changes_and_inserts_a_new_member_before_the_first_later_key()
    {
        await using var run = await Run.StartAsync();
        var (a, aOrder) = await run.ApprovedOrderAsync(CompanyA); var (b, bOrder) = await run.ApprovedOrderAsync(CompanyA);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.OrderDueDates.ChangeOrderDueDateAsync(aOrder, 1, new(2027, 1, 10), TerminToday, Metadata(run));
            await scope.OrderDueDates.ChangeOrderDueDateAsync(bOrder, 1, new(2027, 1, 20), TerminToday, Metadata(run));
            var moved = await scope.Corrections.MoveQueuePlanAsync(a.Id, a.Version, 0, b.Id, a.Id, "Synthetic manual override",
                Metadata(run), _ => Task.FromResult<ManufacturingApiResult<object>?>(null));
            Assert.Equal([b.Id, a.Id], moved.Items.Where(item => item.IsQueued).Select(item => item.PlanId));
            Assert.Equal(1L, moved.QueueRevision); Assert.True(moved.HasManualOrder);
            await scope.OrderDueDates.ChangeOrderDueDateAsync(bOrder, 2, new(2027, 1, 25), TerminToday, Metadata(run));
            await ((IManufacturingPlanPriorityStore)scope.Corrections).ChangePlanPriorityAsync(a.Id, a.Version, 0, 100,
                "Synthetic score change", Metadata(run));
        }
        var c = NewPlan(CompanyA, a.WorkCenterId);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Plans.SavePlanAsync(c, Metadata(run));
            await scope.DueDates.ChangeDueDateAsync(c.Id, 1, 0, new(2027, 1, 15), TerminToday, Metadata(run));
            await scope.Plans.ApprovePlanAsync(c.Id, 1, run.Approver, At, Metadata(run));
        }
        await using var fresh = run.Store(CompanyA);
        var queue = await ((IManufacturingWorkCenterQueueStore)fresh.Corrections).ReadWorkCenterQueueAsync(a.WorkCenterId, a.Id);
        Assert.Equal([c.Id, b.Id, a.Id], queue.StoredOrder);
        Assert.Equal(2L, queue.QueueRevision);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.WorkCenterQueueVersions WHERE CompanyId=@company AND WorkCenterId=@center",
            ("@company", CompanyA), ("@center", a.WorkCenterId)));
    }

    [SqlServerFact]
    public async Task Manual_queue_stale_revisions_foreign_company_and_final_fence_refuse_without_appending_a_snapshot()
    {
        await using var run = await Run.StartAsync(); var (a, _) = await run.ApprovedOrderAsync(CompanyA); var (b, _) = await run.ApprovedOrderAsync(CompanyA);
        await using (var scope = run.Store(CompanyA))
        {
            await Assert.ThrowsAsync<ManufacturingQueueVersionException>(() => scope.Corrections.MoveQueuePlanAsync(a.Id, a.Version, 1,
                b.Id, a.Id, "Synthetic stale queue", Metadata(run), _ => Task.FromResult<ManufacturingApiResult<object>?>(null)));
            await Assert.ThrowsAsync<ManufacturingQueueFenceException>(() => scope.Corrections.MoveQueuePlanAsync(a.Id, a.Version, 0,
                b.Id, a.Id, "Synthetic denied queue", Metadata(run), _ => Task.FromResult<ManufacturingApiResult<object>?>(new(ManufacturingApiStatus.Denied, null, false, null))));
        }
        await using (var foreign = run.Store(CompanyB))
            await Assert.ThrowsAsync<ManufacturingStoreException>(() => foreign.Corrections.MoveQueuePlanAsync(a.Id, a.Version, 0,
                b.Id, a.Id, "Synthetic foreign queue", Metadata(run), _ => Task.FromResult<ManufacturingApiResult<object>?>(null)));
        await using var context = run.Context(CompanyA);
        Assert.Empty(await context.WorkCenterQueueVersions.ToListAsync()); Assert.Empty(await context.WorkCenterQueueEntries.ToListAsync());
    }

    [SqlServerFact]
    public async Task A_membership_failure_rolls_back_the_plan_approval_and_its_queue_snapshot_in_one_owner_transaction()
    {
        await using var run = await Run.StartAsync(); var (a, _) = await run.ApprovedOrderAsync(CompanyA); var (b, _) = await run.ApprovedOrderAsync(CompanyA);
        await using (var scope = run.Store(CompanyA))
            await scope.Corrections.MoveQueuePlanAsync(a.Id, a.Version, 0, b.Id, a.Id, "Synthetic initial queue", Metadata(run),
                _ => Task.FromResult<ManufacturingApiResult<object>?>(null));
        var c = NewPlan(CompanyA, a.WorkCenterId);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Plans.SavePlanAsync(c, Metadata(run));
            await scope.DueDates.ChangeDueDateAsync(c.Id, 1, 0, TerminDay, TerminToday, Metadata(run));
        }
        await run.ScalarAsync<int>("EXEC(N'CREATE TRIGGER manufacturing.SyntheticRejectQueueEntry ON manufacturing.WorkCenterQueueEntries AFTER INSERT AS THROW 50000, ''Synthetic queue failure'', 1;'); SELECT 1");
        var auditBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        try
        {
            await using var scope = run.Store(CompanyA);
            await Assert.ThrowsAnyAsync<Exception>(() => scope.Plans.ApprovePlanAsync(c.Id, 1, run.Approver, At, Metadata(run)));
        }
        finally { await run.ScalarAsync<int>("DROP TRIGGER manufacturing.SyntheticRejectQueueEntry; SELECT 1"); }
        await using var context = run.Context(CompanyA);
        Assert.Equal((int)PlanStatus.Draft, (await context.Plans.SingleAsync(row => row.Id == c.Id)).Status);
        Assert.Equal(1, await context.WorkCenterQueueVersions.CountAsync());
        Assert.Equal(auditBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
    }
}
