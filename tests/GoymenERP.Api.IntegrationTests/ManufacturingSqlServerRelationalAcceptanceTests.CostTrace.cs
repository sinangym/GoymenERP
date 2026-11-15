using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// URT-S19 (B-27, DEC-PRDCT-009) on a REAL SQL Server: the cost trace is built from the order rebuilt out of its stored
/// facts (issues, returns, line-bound declarations and reversals), so the lots, consumption and net outputs are the
/// database's, and another company's store does not see the order at all. The trace stores nothing: no write, no fact,
/// no audit event comes from reading it.
/// </summary>
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_cost_trace_is_built_from_the_stored_facts_of_the_company_order_and_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var (plan, orderId) = await run.ApprovedOrderAsync(CompanyA);
        var good = Guid.NewGuid();
        await using (var store = run.Store(CompanyA))
        {
            await store.Orders.IssueInputAsync(orderId, 1, Guid.NewGuid(), RollA, Kg(6_000m), At, Metadata(run));
            await store.Orders.IssueInputAsync(orderId, 2, Guid.NewGuid(), RollA, Kg(1_500.5m), At.AddMinutes(1),
                Metadata(run));
            await store.Orders.ReturnUnusedInputAsync(orderId, 3, Guid.NewGuid(), RollA, Kg(500.25m),
                At.AddMinutes(2), Metadata(run));
            await store.Corrections.DeclareOutputAsync(orderId, 4, good, RollA, OutputClass.GoodProduct, Kg(6_000m),
                At.AddHours(1), PlanOutputLineReference.ClassLine, Metadata(run));
            await store.Orders.DeclareOutputAsync(orderId, 5, Guid.NewGuid(), RollA, OutputClass.Scrap, Kg(200m),
                At.AddHours(1).AddMinutes(1), Metadata(run));
            await store.Corrections.ReverseOutputAsync(orderId, 6, Guid.NewGuid(), good, Kg(100m), "tartım düzeltmesi",
                At.AddHours(2), Metadata(run));
        }

        var facts = await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Facts");
        var audits = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");

        await using (var store = run.Store(CompanyA))
        {
            var stored = (await store.Orders.FindOrderAsync(orderId))!;
            var trace = ProductionCostTraceBuilderV1.Build(stored);

            Assert.Equal((orderId, CompanyA, plan.Id, 7L, OrderStatus.Open),
                (trace.OrderId, trace.CompanyId, trace.PlanId, trace.Version, trace.Status));
            Assert.Equal([RollA, RollB], trace.Lots.Select(lot => lot.ParentRollId));
            var lotA = trace.Lots[0];
            Assert.Equal((7_500.5m, 500.25m, 7_000.25m, 2, 1),
                (lotA.IssuedKg.Value, lotA.ReturnedKg.Value, lotA.ConsumedKg.Value, lotA.IssueCount, lotA.ReturnCount));
            Assert.Equal((At, At.AddMinutes(1)), (lotA.FirstIssuedAtUtc!.Value, lotA.LastIssuedAtUtc!.Value));
            var lotB = trace.Lots[1];
            Assert.Equal((5_000m, 0m, 0), (lotB.PlannedKg.Value, lotB.ConsumedKg.Value, lotB.IssueCount));
            Assert.Equal(7_000.25m, trace.TotalConsumedKg.Value);
            Assert.Equal(
                [
                    new ProductionCostTraceOutputV1(OutputClass.GoodProduct, Kg(5_900m), 1, 1),
                    new ProductionCostTraceOutputV1(OutputClass.Scrap, Kg(200m), 1, 0),
                ],
                trace.Outputs);
            Assert.Equal((At, At.AddHours(2)), (trace.Timeline.FirstFactAtUtc!.Value, trace.Timeline.LastFactAtUtc!.Value));
            Assert.Equal(ProductionCostTraceCodesV1.TimeRecordsNotRecorded, trace.Timeline.DurationCode);
        }

        await using (var other = run.Store(CompanyB))
        {
            Assert.Null(await other.Orders.FindOrderAsync(orderId));
        }

        Assert.Equal(facts, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Facts"));
        Assert.Equal(audits, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
        Assert.Equal(7L, await run.ScalarAsync<long>("SELECT Version FROM manufacturing.Orders WHERE Id = @id",
            ("@id", orderId)));
    }
}
