using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Üretim U5-b / SAT-D175 at the store on a REAL SQL Server: Sales's binding question reads Manufacturing's own rows — a linked
/// Draft or Approved plan binds the sales order, a cancelled one no longer does, another company never sees it, and a call for
/// another company than the resolved one throws (deny-first).
/// </summary>
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    private static ManufacturingSalesOrderBindingReadPort BindingReadPort(ManufacturingDbContext context, Guid company)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(company);
        return new ManufacturingSalesOrderBindingReadPort(context, companyContext);
    }

    [SqlServerFact]
    public async Task A_linked_plan_that_is_not_cancelled_binds_the_sales_order_of_its_company_only()
    {
        await using var run = await Run.StartAsync();
        var draft = NewPlan(CompanyA, await run.WorkCenterOfAsync(CompanyA));
        var (approved, _) = await run.ApprovedOrderAsync(CompanyA);
        await using (var context = run.Context(CompanyA))
        {
            Assert.False(await BindingReadPort(context, CompanyA).HasBindingPlanAsync(CompanyA, LinkedSalesOrder));
        }

        await using (var store = run.Store(CompanyA))
        {
            await store.Plans.SavePlanAsync(draft, Metadata(run));
            await store.SalesOrderLinks.ChangeSalesOrderLinkAsync(draft.Id, 1, 0, SalesLink(run, LinkedSalesLine),
                Metadata(run));
        }

        await using (var context = run.Context(CompanyA))
        {
            Assert.True(await BindingReadPort(context, CompanyA).HasBindingPlanAsync(CompanyA, LinkedSalesOrder));
            Assert.False(await BindingReadPort(context, CompanyA).HasBindingPlanAsync(CompanyA, Guid.NewGuid()));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                BindingReadPort(context, CompanyA).HasBindingPlanAsync(CompanyB, LinkedSalesOrder));
        }

        await using (var otherCompany = run.Context(CompanyB))
        {
            Assert.False(await BindingReadPort(otherCompany, CompanyB).HasBindingPlanAsync(CompanyB, LinkedSalesOrder));
        }

        // A cancelled Draft no longer binds; an Approved plan with its order does.
        await using (var store = run.Store(CompanyA))
        {
            await store.Lifecycle.CancelPlanAsync(draft.Id, 1, run.Actor, At, "fason bağı sınaması", Metadata(run));
        }

        await using (var context = run.Context(CompanyA))
        {
            Assert.False(await BindingReadPort(context, CompanyA).HasBindingPlanAsync(CompanyA, LinkedSalesOrder));
        }

        await using (var store = run.Store(CompanyA))
        {
            var withOrder = (await store.Plans.FindPlanAsync(approved.Id))!;
            await store.SalesOrderLinks.ChangeSalesOrderLinkAsync(approved.Id, withOrder.Version, 0,
                SalesLink(run, OtherSalesLine), Metadata(run));
        }

        await using (var context = run.Context(CompanyA))
        {
            Assert.True(await BindingReadPort(context, CompanyA).HasBindingPlanAsync(CompanyA, LinkedSalesOrder));
        }
    }
}
