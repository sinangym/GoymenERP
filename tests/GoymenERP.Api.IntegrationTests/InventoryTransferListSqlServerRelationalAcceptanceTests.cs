using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Transfers;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Transfer_list_is_tenant_scoped_bounded_and_returns_the_recorded_dispatch_time()
    {
        await using var run = await Run.StartAsync();
        var own = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-TRANSFER-LIST-A", 10m);
        var foreign = await SeedFreePhysicalSourceAsync(run, CompanyB, "SYN-TRANSFER-LIST-B", 10m);
        var location = await run.LocationOfAsync(CompanyA);
        var destination = await TransferLocationAsync(run, CompanyA, 1);
        var earlier = TransferPlan(own, location, destination, "10");
        var later = TransferPlan(own, location, destination, "10") with { PlannedAtUtc = At.AddMinutes(1) };
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(earlier, Metadata(run), Authorized);
            await scope.Store.PlanAsync(later, Metadata(run), Authorized);
            var dispatch = TransferDispatch();
            await scope.Store.DispatchAsync(later.TransferId, dispatch, EffectiveOutgoing(later, dispatch),
                InventoryTransferSourceScaleSetting.Off, Metadata(run), Authorized);
        }
        var foreignLocation = await run.LocationOfAsync(CompanyB);
        await using (var scope = run.Store(CompanyB))
        {
            await scope.Store.PlanAsync(new(Guid.NewGuid(), foreignLocation, "Company", CompanyB,
                foreignLocation, "Company", Guid.NewGuid(), At.AddDays(1),
                [new(Guid.NewGuid(), foreign.LabelId, foreign.LabelVersion.ToString(), "10")]), Metadata(run), Authorized);
        }
        await using (var scope = run.Store(CompanyA))
        {
            var page = await scope.Transfers.ListAsync(1);
            Assert.Equal(CompanyA, page.CompanyId);
            Assert.True(page.WindowFull);
            var item = Assert.Single(page.Items);
            Assert.Equal((later.TransferId, CompanyA, "InTransit", At.AddHours(1)),
                (item.TransferId, item.CompanyId, item.Status, item.DispatchedAtUtc));
            var all = await scope.Transfers.ListAsync(50);
            Assert.Equal(2, all.Items.Count);
            Assert.False(all.WindowFull);
            Assert.All(all.Items, row => Assert.Equal(CompanyA, row.CompanyId));
        }
    }
}
