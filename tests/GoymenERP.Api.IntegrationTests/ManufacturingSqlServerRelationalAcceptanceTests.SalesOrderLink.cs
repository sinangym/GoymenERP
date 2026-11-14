using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Üretim U2-b / API-16 at the store on a REAL SQL Server: the link is stored with its snapshot and audit event, Sales's
/// open-supply read answers the company's open plans and orders of the order (narrowed by line) with the planned kg of the
/// plan's current revision, and the link migration's
/// Down passes while no plan holds or held a link and refuses (51261) once one did.
/// </summary>
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    private static readonly Guid LinkedSalesOrder = Guid.Parse("50000000-0000-0000-0000-00000000b001");
    private static readonly Guid LinkedSalesLine = Guid.Parse("50000000-0000-0000-0000-00000000b002");
    private static readonly Guid OtherSalesLine = Guid.Parse("50000000-0000-0000-0000-00000000b003");

    private static PlanSalesOrderLink SalesLink(Run run, Guid line) => new(LinkedSalesOrder, line,
        Guid.Parse("50000000-0000-0000-0000-00000000b010"), Guid.Parse("50000000-0000-0000-0000-00000000b011"),
        Guid.Parse("50000000-0000-0000-0000-00000000b012"), 2, run.Actor.ToString("D"), At);

    private static ManufacturingSalesOrderSupplyReadPort SupplyReadPort(ManufacturingDbContext context, Guid company)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(company);
        return new ManufacturingSalesOrderSupplyReadPort(context, companyContext);
    }

    [SqlServerFact]
    public async Task Linked_plans_and_open_orders_are_read_for_the_sales_order_of_the_company_only()
    {
        await using var run = await Run.StartAsync();
        var draft = NewPlan(CompanyA, await run.WorkCenterOfAsync(CompanyA));
        var (approved, orderId) = await run.ApprovedOrderAsync(CompanyA);
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(draft, Metadata(run));
            var linked = await store.SalesOrderLinks.ChangeSalesOrderLinkAsync(draft.Id, 1, 0,
                SalesLink(run, LinkedSalesLine), Metadata(run));
            Assert.Equal((1L, 1L), (linked.Version, linked.SalesOrderLinkRevision));
            var withOrder = (await store.Plans.FindPlanAsync(approved.Id))!;
            await store.SalesOrderLinks.ChangeSalesOrderLinkAsync(approved.Id, withOrder.Version, 0,
                SalesLink(run, OtherSalesLine), Metadata(run));
        }

        await using (var context = run.Context(CompanyA))
        {
            var all = await SupplyReadPort(context, CompanyA).ListOpenForSalesOrderAsync(CompanyA, LinkedSalesOrder, null);
            Assert.Equal(
                new[] { (draft.Id, "Draft", (Guid?)null, (string?)null, LinkedSalesLine, 15_000m),
                        (approved.Id, "Approved", (Guid?)orderId, (string?)"Open", OtherSalesLine, 15_000m) }
                    .OrderBy(item => item.Item1),
                all.Select(item => (item.PlanId, item.PlanStatus, item.OrderId, item.OrderStatus, item.SalesOrderLineId,
                    item.PlannedKg)).OrderBy(item => item.PlanId));
            Assert.All(all, item => Assert.Null(item.ActualKg));

            var narrowed = await SupplyReadPort(context, CompanyA).ListOpenForSalesOrderAsync(CompanyA, LinkedSalesOrder,
                [OtherSalesLine]);
            Assert.Equal([approved.Id], narrowed.Select(item => item.PlanId));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SupplyReadPort(context, CompanyA).ListOpenForSalesOrderAsync(CompanyB, LinkedSalesOrder, null));
        }

        await using (var otherCompany = run.Context(CompanyB))
        {
            Assert.Empty(await SupplyReadPort(otherCompany, CompanyB).ListOpenForSalesOrderAsync(CompanyB,
                LinkedSalesOrder, null));
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.plan.sales-order-linked.v1'"));
    }

    [SqlServerFact]
    public async Task A_revised_linked_plan_is_read_with_the_planned_kg_of_its_current_revision()
    {
        await using var run = await Run.StartAsync();
        var workCenter = await run.WorkCenterOfAsync(CompanyA);
        var plan = NewPlan(CompanyA, workCenter);
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
            await store.SalesOrderLinks.ChangeSalesOrderLinkAsync(plan.Id, 1, 0, SalesLink(run, LinkedSalesLine),
                Metadata(run));
        }

        await using (var context = run.Context(CompanyA))
        {
            Assert.Equal(15_000m, Assert.Single(await SupplyReadPort(context, CompanyA)
                .ListOpenForSalesOrderAsync(CompanyA, LinkedSalesOrder, null)).PlannedKg);
        }

        await using (var store = run.Store(CompanyA))
        {
            await store.Revisions.RevisePlanAsync(plan.Id, 1, workCenter, ManufacturingProcess.Slitting,
                [new PlannedInput(RollB, Kg(4_000m), 5), new PlannedInput(RollA, Kg(2_500m), 6)], null,
                "bağlı plan revize", Metadata(run));
        }

        await using (var context = run.Context(CompanyA))
        {
            Assert.Equal(6_500m, Assert.Single(await SupplyReadPort(context, CompanyA)
                .ListOpenForSalesOrderAsync(CompanyA, LinkedSalesOrder, null)).PlannedKg);
        }
    }

    [SqlServerFact]
    public async Task The_sales_order_link_migrations_Down_passes_without_a_link_and_refuses_while_a_plan_held_one()
    {
        await using var run = await Run.StartAsync();
        var plan = NewPlan(CompanyA, await run.WorkCenterOfAsync(CompanyA));
        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(plan, Metadata(run));
        }

        var guard = Assert.IsType<SqlOperation>(new AddManufacturingPlanSalesOrderLinks().DownOperations[0]).Sql;
        await run.ScalarAsync<int>(guard + "; SELECT 1");

        // Linking then removing still leaves a moved link revision: the earlier schema cannot say it was ever linked.
        await using (var store = run.Store(CompanyA))
        {
            await store.SalesOrderLinks.ChangeSalesOrderLinkAsync(plan.Id, 1, 0, SalesLink(run, LinkedSalesLine),
                Metadata(run));
            await store.SalesOrderLinks.ChangeSalesOrderLinkAsync(plan.Id, 1, 1, null, Metadata(run));
        }

        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "; SELECT 1"));
        Assert.Equal(51261, refused.Number);
        Assert.Equal(2L, await run.ScalarAsync<long>(
            "SELECT [SalesOrderLinkRevision] FROM manufacturing.Plans WHERE Id = @p", ("@p", plan.Id)));
    }
}
