using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Üretim F-007 (URT-D66 (3)/(4)) at the store on a REAL SQL Server: the order opens with the plan's termin as its own
/// termin (revision 1); a change moves the order's termin and termin revision with one audit event while the order version
/// and the plan's termin stay; a stale termin revision and a day before the Istanbul business day are refused with nothing
/// written; and the order termin migration's Down passes while no order holds a termin and refuses (51466) once one does.
/// </summary>
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    private static readonly DateOnly OtherOrderTermin = new(2027, 2, 1);

    [SqlServerFact]
    public async Task An_order_opens_with_the_plan_termin_and_its_change_moves_only_the_order_termin_with_one_audit()
    {
        await using var run = await Run.StartAsync();
        var (plan, orderId) = await run.ApprovedOrderAsync(CompanyA);

        await using (var store = run.Store(CompanyA))
        {
            var opened = (await store.Orders.FindOrderAsync(orderId))!;
            Assert.Equal((TerminDay, 1L, TerminDay, false),
                (opened.DueDate, opened.DueDateRevision, opened.PlanDueDate, opened.DueDateDiffersFromPlan));
            var versionBefore = opened.Version;

            var changed = await store.OrderDueDates.ChangeOrderDueDateAsync(orderId, 1, OtherOrderTermin, TerminToday,
                Metadata(run));
            Assert.Equal((OtherOrderTermin, 2L, TerminDay, true, versionBefore),
                (changed.DueDate, changed.DueDateRevision, changed.PlanDueDate, changed.DueDateDiffersFromPlan,
                    changed.Version));
        }

        Assert.Equal("2027-02-01", await run.ScalarAsync<string>(
            "SELECT CONVERT(nvarchar(10), [DueDate], 23) FROM manufacturing.Orders WHERE Id = @p", ("@p", orderId)));
        Assert.Equal(2L, await run.ScalarAsync<long>(
            "SELECT [DueDateRevision] FROM manufacturing.Orders WHERE Id = @p", ("@p", orderId)));
        // URT-D66 (4): the plan's termin never follows the order's.
        Assert.Equal("2026-12-31", await run.ScalarAsync<string>(
            "SELECT CONVERT(nvarchar(10), [RequestedDueDate], 23) FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id)));
        Assert.Equal(1L, await run.ScalarAsync<long>(
            "SELECT [DueDateRevision] FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.order.due-date-changed.v1'"));
    }

    [SqlServerFact]
    public async Task A_stale_or_past_order_termin_is_refused_at_the_store_and_nothing_is_written()
    {
        await using var run = await Run.StartAsync();
        var (_, orderId) = await run.ApprovedOrderAsync(CompanyA);

        await using (var store = run.Store(CompanyA))
        {
            Assert.Equal(ManufacturingRuleCodes.OrderDueDateChanged, (await Assert.ThrowsAsync<ManufacturingRuleException>(
                () => store.OrderDueDates.ChangeOrderDueDateAsync(orderId, 0, OtherOrderTermin, TerminToday,
                    Metadata(run)))).Code);
        }

        await using (var store = run.Store(CompanyA))
        {
            Assert.Equal(ManufacturingRuleCodes.DueDateInPast, (await Assert.ThrowsAsync<ManufacturingRuleException>(
                () => store.OrderDueDates.ChangeOrderDueDateAsync(orderId, 1, TerminToday.AddDays(-1), TerminToday,
                    Metadata(run)))).Code);
        }

        Assert.Equal("2026-12-31", await run.ScalarAsync<string>(
            "SELECT CONVERT(nvarchar(10), [DueDate], 23) FROM manufacturing.Orders WHERE Id = @p", ("@p", orderId)));
        Assert.Equal(1L, await run.ScalarAsync<long>(
            "SELECT [DueDateRevision] FROM manufacturing.Orders WHERE Id = @p", ("@p", orderId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.order.due-date-changed.v1'"));
    }

    [SqlServerFact]
    public async Task The_order_termin_migrations_Down_passes_without_a_termin_and_refuses_while_an_order_holds_one()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddManufacturingOrderDueDate().DownOperations[0]).Sql;
        await run.ScalarAsync<int>(guard + "; SELECT 1");

        var (_, orderId) = await run.ApprovedOrderAsync(CompanyA);

        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "; SELECT 1"));
        Assert.Equal(51466, refused.Number);
        Assert.Equal(1L, await run.ScalarAsync<long>(
            "SELECT [DueDateRevision] FROM manufacturing.Orders WHERE Id = @p", ("@p", orderId)));
    }
}
