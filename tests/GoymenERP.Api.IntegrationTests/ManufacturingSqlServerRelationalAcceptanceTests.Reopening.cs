using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_reopening_joins_the_owner_transaction_and_a_rollback_preserves_the_original_closed_header_and_audit()
    {
        await using var run = await Run.StartAsync(); var (_, orderId) = await run.ApprovedOrderAsync(CompanyA);
        await using (var scope = run.Store(CompanyA))
            await scope.Orders.CloseOrderAsync(orderId, 1, run.Actor, At, Metadata(run));
        var auditBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        await using (var scope = run.Store(CompanyA))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Corrections.ReopenOrderAsync(orderId, 2, run.Actor,
                At.AddMinutes(1), "Synthetic missing host transaction", Metadata(run), (_, _) => Task.FromResult<ManufacturingApiResult<object>?>(null)));
            await using var transaction = await scope.Context.Database.BeginTransactionAsync();
            var open = await scope.Corrections.ReopenOrderAsync(orderId, 2, run.Actor, At.AddMinutes(1), "Synthetic host rollback",
                Metadata(run), (_, _) => Task.FromResult<ManufacturingApiResult<object>?>(null));
            Assert.Equal(OrderStatus.Open, open.Status); Assert.Single(open.Reopenings);
            await transaction.RollbackAsync();
        }
        await using var fresh = run.Store(CompanyA); var order = (await fresh.Orders.FindOrderAsync(orderId))!;
        Assert.Equal((OrderStatus.Closed, 2L, At), (order.Status, order.Version, order.ClosedAtUtc!.Value));
        Assert.Empty(order.Reopenings); Assert.Empty(await fresh.Context.OrderReopenings.ToListAsync());
        Assert.Equal(auditBefore, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
    }

    [SqlServerFact]
    public async Task A_second_policy_close_appends_another_decision_and_fresh_reads_keep_the_first_close_and_reasoned_reopening()
    {
        await using var run = await Run.StartAsync(); var (_, orderId) = await run.ApprovedOrderAsync(CompanyA);
        var policy = new ClosureTolerancePolicySnapshotV1(CompanyA, Guid.NewGuid(), 7, ManufacturingProcess.Slitting, 5m, true,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), DateOnly.FromDateTime(At.UtcDateTime));
        var authority = new ManufacturingPersistenceAuthorityFenceV1(CompanyA, run.Actor, _ => Task.FromResult(true));
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Orders.IssueInputAsync(orderId, 1, Guid.NewGuid(), RollA, Kg(100m), At, Metadata(run));
            await scope.Orders.DeclareOutputAsync(orderId, 2, Guid.NewGuid(), RollA, OutputClass.GoodProduct, Kg(100m), At, Metadata(run));
            await scope.Orders.CloseOrderWithPolicyAsync(orderId, 3, run.Actor, At.AddMinutes(1), policy, null, authority, Metadata(run));
        }
        await using (var scope = run.Store(CompanyA))
        {
            await using var transaction = await scope.Context.Database.BeginTransactionAsync();
            await scope.Corrections.ReopenOrderAsync(orderId, 4, run.Actor, At.AddMinutes(2), "Synthetic generation correction", Metadata(run),
                (_, _) => Task.FromResult<ManufacturingApiResult<object>?>(null));
            await transaction.CommitAsync();
        }
        await using (var scope = run.Store(CompanyA))
            await scope.Orders.CloseOrderWithPolicyAsync(orderId, 5, run.Actor, At.AddMinutes(3), policy, null, authority, Metadata(run));
        await using var fresh = run.Store(CompanyA); var order = (await fresh.Orders.FindOrderAsync(orderId))!;
        Assert.Equal((OrderStatus.Closed, 6L), (order.Status, order.Version));
        Assert.Equal((4L, 5L, At.AddMinutes(1)), (order.Reopenings[0].ClosedOrderVersion, order.Reopenings[0].ReopenedOrderVersion, order.Reopenings[0].ClosedAtUtc));
        var decisions = await fresh.Context.OrderClosureDecisions.OrderBy(row => row.OrderVersion).ToListAsync();
        Assert.Equal([3L, 5L], decisions.Select(row => row.OrderVersion));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'manufacturing.order.closed.v1'"));
    }
}
