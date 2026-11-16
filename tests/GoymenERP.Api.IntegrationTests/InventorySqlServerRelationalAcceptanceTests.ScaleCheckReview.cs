using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// LOJ-D20 on real rows: a dispatch whose source scale setting could not be read is stored as dispatched (not stopped) with
/// the unreadable setting, and the review list reads exactly those dispatches of the context company — never a dispatch
/// whose setting was read, never another company's.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_scale_check_review_lists_only_dispatches_stored_with_an_unreadable_scale_setting_for_its_company()
    {
        await using var run = await Run.StartAsync();
        var sourceLocation = await run.LocationOfAsync(CompanyA);
        var destinationLocation = await TransferLocationAsync(run, CompanyA, offset: 1);
        var unreadable = await PlanAndDispatchAsync(run, "SYN-SCALE-UNREADABLE", sourceLocation, destinationLocation,
            InventoryTransferSourceScaleSetting.Unreadable);
        var on = await PlanAndDispatchAsync(run, "SYN-SCALE-ON", sourceLocation, destinationLocation,
            InventoryTransferSourceScaleSetting.On);
        var off = await PlanAndDispatchAsync(run, "SYN-SCALE-OFF", sourceLocation, destinationLocation,
            InventoryTransferSourceScaleSetting.Off);

        // The unreadable setting did not stop the dispatch: the transfer is in transit with the setting stored as read.
        Assert.Equal((int)InventoryTransferSourceScaleSetting.Unreadable, await run.ScalarAsync<int>(
            "SELECT SourceScaleSettingAtDispatch FROM inventory.TransferDispatches WHERE CompanyId = @c AND TransferId = @t",
            ("@c", CompanyA), ("@t", unreadable)));
        Assert.Equal((int)InventoryTransferStatus.InTransit, await run.ScalarAsync<int>(
            "SELECT Status FROM inventory.Transfers WHERE CompanyId = @c AND Id = @t", ("@c", CompanyA), ("@t", unreadable)));

        var companyA = new CompanyContext();
        companyA.Set(CompanyA);
        await using (var dbContext = run.Context(CompanyA))
        {
            var page = await new InventoryTransferScaleCheckReviewReader(dbContext, companyA)
                .ListNotCheckedAsync(InventoryTransferScaleCheckReviewApiV1.MaximumLimit);

            Assert.Equal((CompanyA, false), (page.CompanyId, page.HasMore));
            var item = Assert.Single(page.Items);
            Assert.Equal((unreadable, "2", nameof(InventoryTransferStatus.InTransit), sourceLocation, destinationLocation,
                    InventoryTransferScaleCheckStatusV1.NotChecked),
                (item.TransferId, item.TransferVersion, item.TransferStatus, item.SourceLocationId, item.DestinationLocationId,
                    item.ScaleCheckStatus));
            Assert.DoesNotContain(page.Items, listed => listed.TransferId == on || listed.TransferId == off);
        }

        // The read wrote nothing: the three stored dispatches are still the only ones.
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.TransferDispatches WHERE CompanyId = @c", ("@c", CompanyA)));

        var companyB = new CompanyContext();
        companyB.Set(CompanyB);
        await using (var dbContext = run.Context(CompanyB))
        {
            var foreign = await new InventoryTransferScaleCheckReviewReader(dbContext, companyB)
                .ListNotCheckedAsync(InventoryTransferScaleCheckReviewApiV1.MaximumLimit);

            Assert.Equal((CompanyB, false), (foreign.CompanyId, foreign.HasMore));
            Assert.Empty(foreign.Items);
        }
    }

    /// <summary>Plans one whole-label transfer from a fresh free source and dispatches it with the given scale setting.</summary>
    private static async Task<Guid> PlanAndDispatchAsync(Run run, string code, Guid sourceLocation, Guid destinationLocation,
        InventoryTransferSourceScaleSetting scaleSetting)
    {
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, code, 5m);
        var plan = TransferPlan(source, sourceLocation, destinationLocation, "5");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(plan, Metadata(run) with
            {
                IdempotencyKey = $"scale-plan-{Guid.NewGuid():N}",
                CorrelationId = $"scale-plan-{Guid.NewGuid():N}",
            }, Authorized);
        }

        var dispatch = TransferDispatch();
        await using (var scope = run.Store(CompanyA))
        {
            var dispatched = await scope.Store.DispatchAsync(plan.TransferId, dispatch, EffectiveOutgoing(plan, dispatch),
                scaleSetting, Metadata(run) with
                {
                    IdempotencyKey = $"scale-dispatch-{Guid.NewGuid():N}",
                    CorrelationId = $"scale-dispatch-{Guid.NewGuid():N}",
                }, Authorized);
            Assert.Equal(("2", "InTransit"), (dispatched.Transfer.Version, dispatched.Transfer.Status));
        }

        return plan.TransferId;
    }
}
