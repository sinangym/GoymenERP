using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_work_center_queue_reads_remaining_mass_and_ignores_cancelled_order_history_in_the_resolved_company()
    {
        await using var run = await Run.StartAsync();
        var (queued, orderId) = await run.ApprovedOrderAsync(CompanyA);
        var (cancelledPlan, cancelledOrder) = await run.ApprovedOrderAsync(CompanyA);
        var target = NewPlan(CompanyA, queued.WorkCenterId);
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(target, Metadata(run));
            await store.Orders.IssueInputAsync(orderId, 1, Guid.NewGuid(), RollA, Kg(1000m), At, Metadata(run));
            await store.Orders.DeclareOutputAsync(orderId, 2, Guid.NewGuid(), RollA, OutputClass.GoodProduct,
                Kg(600m), At.AddMinutes(1), Metadata(run));
            await store.Corrections.CancelOrderAsync(cancelledOrder, 1, run.Actor, At.AddHours(1),
                "SENTETIK queue cancellation", Metadata(run));
        }

        await using var fresh = run.Store(CompanyA);
        var queue = await ((IManufacturingWorkCenterQueueStore)fresh.Corrections)
            .ReadWorkCenterQueueAsync(queued.WorkCenterId, target.Id);
        var entry = Assert.Single(queue.Queued, entry => entry.PlanId == queued.Id);
        Assert.Equal((15000m, 600m, 14400m), (entry.PlannedInputKg.Value, entry.ProcessedKg.Value,
            ProductionQueueRemainingKgCalculatorV1.Remaining(entry.PlannedInputKg, entry.ProcessedKg).Value));
        Assert.Single(queue.Queued, entry => entry.PlanId == cancelledPlan.Id);
        Assert.Equal(0m, queue.Queued.Single(entry => entry.PlanId == cancelledPlan.Id).ProcessedKg.Value);
        await using var foreign = run.Store(CompanyB);
        var refused = await Assert.ThrowsAsync<ManufacturingStoreException>(() =>
            ((IManufacturingWorkCenterQueueStore)foreign.Corrections).ReadWorkCenterQueueAsync(queued.WorkCenterId,
                target.Id));
        Assert.Equal(ManufacturingStoreCodes.PlanNotFound, refused.Code);
    }
}
