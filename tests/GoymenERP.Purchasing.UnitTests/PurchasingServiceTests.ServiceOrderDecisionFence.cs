using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    [Theory]
    [InlineData("prestage")]
    [InlineData("postsave")]
    [InlineData("replay")]
    public async Task Service_order_approval_rechecks_authority_after_the_final_price_disclosure_await(string phase)
    {
        var (service, store, authority, user, id) = await ServiceDecisionFixtureAsync();
        var saved = store.Saved;
        var writes = store.Writes;
        var armed = phase == "replay";
        if (phase == "prestage") store.BeforeDecisionFence = () => armed = true;
        if (phase == "postsave") store.AfterSyntheticSave = () => armed = true;
        if (phase == "replay") store.ReplayResult = new(PurchaseServiceOrder.Approve(saved!, 1, Approver, DateTimeOffset.UtcNow),
            null, true, "SENTETIK-original");
        authority.AfterPriceRead = () => { if (armed) authority.ApproveAllowed = false; };
        user.CurrentActor = Approver;
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => service.ApproveAsync(id,
            new("1", "SENTETIK onay"), Metadata("service-decision-approve")));
        Assert.Equal(403, denied.Status);
        Assert.Same(saved, store.Saved);
        Assert.Equal(PurchaseServiceOrderStatus.Draft, store.Saved!.Status);
        Assert.Equal(writes, store.Writes);
        Assert.True(authority.PriceAllowed); // Price visibility never substitutes for approval authority.
    }

    [Fact]
    public async Task Service_order_approval_keeps_price_disclosure_optional_and_returns_the_native_response()
    {
        var (service, store, authority, user, id) = await ServiceDecisionFixtureAsync();
        authority.PriceAllowed = false;
        user.CurrentActor = Approver;
        var accepted = await service.ApproveAsync(id, new("1", "SENTETIK onay"), Metadata("service-withheld-approve"));
        Assert.Equal((id, "2", "Approved", "Withheld"),
            (accepted.Order.OrderId, accepted.Order.Version, accepted.Order.Status, accepted.Order.PriceDisclosure));
        Assert.False(accepted.IsReplay);
        Assert.Equal(2, store.Writes); // Native create + native approval, no extra command.
    }

    [Fact]
    public async Task Service_order_approval_can_retry_after_disclosure_boundary_refusal_and_replay_checks_authority_again()
    {
        var (service, store, authority, user, id) = await ServiceDecisionFixtureAsync();
        var armed = false;
        store.AfterSyntheticSave = () => armed = true;
        authority.AfterPriceRead = () => { if (armed) authority.ApproveAllowed = false; };
        user.CurrentActor = Approver;
        var request = new ApprovePurchaseServiceOrderV1("1", "SENTETIK onay");
        await Assert.ThrowsAsync<PurchasingServiceException>(() => service.ApproveAsync(id, request, Metadata("service-retry-approve")));
        authority.ApproveAllowed = true; authority.AfterPriceRead = null; store.AfterSyntheticSave = null;
        var accepted = await service.ApproveAsync(id, request, Metadata("service-retry-approve"));
        store.ReplayResult = new(store.Saved, null, true, accepted.OriginalCorrelationId);
        var replay = await service.ApproveAsync(id, request, Metadata("service-retry-approve", "SENTETIK-later"));
        Assert.True(replay.IsReplay);
        Assert.Equal(accepted.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(2, store.Writes);
        authority.AfterPriceRead = () => authority.ApproveAllowed = false;
        await Assert.ThrowsAsync<PurchasingServiceException>(() => service.ApproveAsync(id, request, Metadata("service-retry-approve")));
        Assert.Equal(2, store.Writes);
    }

    private static async Task<(PurchaseServiceOrderServiceV1 Service, ServiceCommandStore Store,
        ServiceDecisionAuthority Authority, FixedUser User, Guid Id)> ServiceDecisionFixtureAsync()
    {
        var source = new ServiceCommandSource();
        var store = new ServiceCommandStore();
        var authority = new ServiceDecisionAuthority();
        var user = new FixedUser();
        var service = new PurchaseServiceOrderServiceV1(new MutableCompanyContext(), user, authority, store,
            source, source, source, source, TimeProvider.System);
        var created = await service.CreateAsync(ServiceCreate(source), Metadata("service-decision-create"));
        Assert.Equal("1", created.Order.Version);
        return (service, store, authority, user, created.Order.OrderId);
    }

    private sealed class ServiceDecisionAuthority : IErpAuthorityRevalidationService
    {
        public bool ApproveAllowed = true, PriceAllowed = true;
        public Action? AfterPriceRead;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        {
            var granted = permission == PurchasingPermissionManifestV1.OrdersApprove ? ApproveAllowed
                : permission != PurchasingPermissionManifestV1.OrdersPriceRead || PriceAllowed;
            if (permission == PurchasingPermissionManifestV1.OrdersPriceRead) AfterPriceRead?.Invoke();
            return Task.FromResult(new PermissionScope(granted, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }
}
