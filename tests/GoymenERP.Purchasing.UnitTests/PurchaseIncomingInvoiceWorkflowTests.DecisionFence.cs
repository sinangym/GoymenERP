using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchaseIncomingInvoiceWorkflowTests
{
    [Fact]
    public async Task Variance_approval_rechecks_deciding_permission_after_the_price_owner_await_before_staging()
    {
        var world = new World(withVariance: true);
        var armed = false;
        world.Store.BeforeMutation = () => armed = true;
        world.Authority.AfterMeasure = permission =>
        {
            if (armed && permission == PurchasingPermissionManifestV1.OrdersPriceRead)
                world.Authority.Denied = PurchaseIncomingInvoicePermissionNamesV1.ApproveVariance;
        };
        var error = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ApproveVarianceAsync(world.Id,
            new("2", "SENTETIK insan gerekçesi"), Metadata("late-price-read")));
        Assert.Equal(403, error.Status);
        Assert.Null(world.Store.Current.VarianceDecision);
        Assert.Equal(2L, world.Store.Current.Version);
        Assert.Empty(world.Store.Committed);
    }

    [Fact]
    public async Task Variance_approval_replay_rechecks_deciding_permission_after_its_last_price_owner_await()
    {
        var world = new World(withVariance: true);
        var request = new PurchaseIncomingInvoiceVarianceApprovalRequestV1("2", "SENTETIK insan gerekçesi");
        await world.Service().ApproveVarianceAsync(world.Id, request, Metadata("replay-price-read"));
        var armed = false;
        world.Store.BeforeReplayAnswer = () => armed = true;
        world.Authority.AfterMeasure = permission =>
        {
            if (armed && permission == PurchasingPermissionManifestV1.OrdersPriceRead)
                world.Authority.Denied = PurchaseIncomingInvoicePermissionNamesV1.ApproveVariance;
        };
        Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ApproveVarianceAsync(world.Id,
            request, Metadata("replay-price-read")))).Status);
        Assert.Single(world.Store.Committed);
        Assert.Equal(3L, world.Store.Current.Version);
    }
}
