using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Üretim F-010a (URT-D29) at the store on a REAL SQL Server: the hand-entered termin is stored as a calendar day with its
/// termin revision and one audit event while the plan version stays; a stale termin revision and a closed order are refused
/// with nothing written; and the termin migration's Down passes while no plan holds or held a termin and refuses (51281)
/// once one did.
/// </summary>
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    private static readonly DateOnly EnteredDueDate = new(2026, 12, 31);
    private static readonly DateOnly OtherDueDate = new(2027, 1, 15);

    [SqlServerFact]
    public async Task A_hand_entered_termin_is_stored_as_a_day_with_its_revision_and_audit_and_the_plan_version_stays()
    {
        await using var run = await Run.StartAsync();
        var plan = NewPlan(CompanyA, await run.WorkCenterOfAsync(CompanyA));
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
            var entered = await store.DueDates.ChangeDueDateAsync(plan.Id, 1, 0, EnteredDueDate, Metadata(run));
            Assert.Equal((EnteredDueDate, 1L, 1L), (entered.RequestedDueDate, entered.DueDateRevision, entered.Version));
        }

        await using (var store = run.Store(CompanyA))
        {
            var stored = (await store.Plans.FindPlanAsync(plan.Id))!;
            Assert.Equal((EnteredDueDate, 1L, 1L), (stored.RequestedDueDate, stored.DueDateRevision, stored.Version));

            // A stale termin revision: refused, nothing written.
            Assert.Equal(ManufacturingRuleCodes.PlanDueDateChanged, (await Assert.ThrowsAsync<ManufacturingRuleException>(
                () => store.DueDates.ChangeDueDateAsync(plan.Id, 1, 0, OtherDueDate, Metadata(run)))).Code);
        }

        Assert.Equal("2026-12-31", await run.ScalarAsync<string>(
            "SELECT CONVERT(nvarchar(10), [RequestedDueDate], 23) FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id)));
        Assert.Equal(1L, await run.ScalarAsync<long>(
            "SELECT [DueDateRevision] FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id)));
        Assert.Equal(1L, await run.ScalarAsync<long>(
            "SELECT [Version] FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.plan.due-date-changed.v1'"));
    }

    [SqlServerFact]
    public async Task A_termin_change_under_a_closed_order_is_refused_and_nothing_is_written()
    {
        await using var run = await Run.StartAsync();
        var (plan, orderId) = await run.ApprovedOrderAsync(CompanyA);

        // The same fully accounted facts as the close test: roll A issued and produced, roll B produced with WIP left.
        await using (var a = run.Store(CompanyA))
        {
            await a.Orders.IssueInputAsync(orderId, 1, Guid.NewGuid(), RollA, Kg(10_000m), At, Metadata(run));
            await a.Orders.IssueInputAsync(orderId, 2, Guid.NewGuid(), RollB, Kg(5_000m), At.AddMinutes(1), Metadata(run));
            await a.Orders.DeclareOutputAsync(orderId, 3, Guid.NewGuid(), RollA, OutputClass.GoodProduct, Kg(9_700.125m),
                At.AddHours(1), Metadata(run));
            await a.Orders.DeclareOutputAsync(orderId, 4, Guid.NewGuid(), RollA, OutputClass.Scrap, Kg(299.875m),
                At.AddHours(1).AddMinutes(1), Metadata(run));
            await a.Orders.DeclareOutputAsync(orderId, 5, Guid.NewGuid(), RollB, OutputClass.GoodProduct, Kg(3_000m),
                At.AddHours(2), Metadata(run));
            await a.Orders.RecordWorkInProgressAsync(orderId, 6, Guid.NewGuid(), RollB, Kg(2_000m), At.AddHours(3),
                Metadata(run));
        }

        await using (var a = run.Store(CompanyA))
        {
            await a.Orders.CloseOrderAsync(orderId, 7, run.Actor, At.AddHours(4), Metadata(run));
        }

        await using (var a = run.Store(CompanyA))
        {
            var version = (await a.Plans.FindPlanAsync(plan.Id))!.Version;
            Assert.Equal(ManufacturingRuleCodes.PlanDueDateOrderClosed,
                (await Assert.ThrowsAsync<ManufacturingRuleException>(
                    () => a.DueDates.ChangeDueDateAsync(plan.Id, version, 0, EnteredDueDate, Metadata(run)))).Code);
        }

        Assert.Equal(0L, await run.ScalarAsync<long>(
            "SELECT [DueDateRevision] FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Plans WHERE Id = @p AND [RequestedDueDate] IS NOT NULL", ("@p", plan.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.plan.due-date-changed.v1'"));
    }

    [SqlServerFact]
    public async Task The_due_date_migrations_Down_passes_without_a_termin_and_refuses_while_a_plan_held_one()
    {
        await using var run = await Run.StartAsync();
        var plan = NewPlan(CompanyA, await run.WorkCenterOfAsync(CompanyA));
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
        }

        var guard = Assert.IsType<SqlOperation>(new AddManufacturingPlanDueDate().DownOperations[0]).Sql;
        await run.ScalarAsync<int>(guard + "; SELECT 1");

        // Entering then removing still leaves a moved termin revision: the earlier schema cannot say a termin was entered.
        await using (var store = run.Store(CompanyA))
        {
            await store.DueDates.ChangeDueDateAsync(plan.Id, 1, 0, EnteredDueDate, Metadata(run));
            await store.DueDates.ChangeDueDateAsync(plan.Id, 1, 1, null, Metadata(run));
        }

        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "; SELECT 1"));
        Assert.Equal(51281, refused.Number);
        Assert.Equal(2L, await run.ScalarAsync<long>(
            "SELECT [DueDateRevision] FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id)));
    }
}
