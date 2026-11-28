using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    // Actual Application authority/metadata boundary with a controlled store; not SQL or a constructed success receipt.
    [Fact]
    public async Task Mixed_price_uses_only_native_OrdersRevise_and_cannot_borrow_OrdersApprove_or_read_as_mutation_authority()
    {
        var world = new World(); world.Authority.Denied.Add(RevisePermission);
        var failure = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ReviseMixedServicePricesAsync(
            Guid.NewGuid(), PriceAdmissionBody(), Metadata("price-denied")));
        Assert.Equal(403, failure.Status); Assert.Equal(0, world.Store.PriceAdmissionCalls);
        Assert.Equal(RevisePermission, Assert.Single(world.Authority.Measured)); Assert.Equal(0, world.Accounts.Calls);
    }

    [Fact]
    public async Task Mixed_price_admission_forwards_exact_native_metadata_and_frozen_intent_without_a_second_approval_or_general_read()
    {
        var world = new World(); world.Authority.Denied.Add(ApprovePermission); world.Authority.Denied.Add(ReadPermission);
        var id = Guid.NewGuid(); var body = PriceAdmissionBody(); var metadata = Metadata("price-native-key", "price-native-correlation");
        var failure = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ReviseMixedServicePricesAsync(id, body, metadata));
        Assert.Equal(503, failure.Status); // The controlled store deliberately has no genuine original receipt.
        Assert.Equal(1, world.Store.PriceAdmissionCalls); Assert.Equal(id, world.Store.PriceAdmissionId);
        Assert.Equal((Actor, RevisePermission, metadata.IdempotencyKey, metadata.CorrelationId),
            (world.Store.PriceAdmissionEnvelope!.ActorId, world.Store.PriceAdmissionEnvelope.PermissionCode,
                world.Store.PriceAdmissionEnvelope.IdempotencyKey, world.Store.PriceAdmissionEnvelope.CorrelationId));
        Assert.Equal(PurchasingRequestHash.Compute(PurchaseMixedServicePriceIntentV1.Command, id, body), world.Store.PriceAdmissionHash);
        Assert.Equal(body.ExpectedVersion, world.Store.PriceAdmissionRequest!.ExpectedVersion);
        Assert.Equal(body.Services[0].ExpectedVersion, world.Store.PriceAdmissionRequest.Services[0].ExpectedVersion);
        Assert.NotSame(body.Services, world.Store.PriceAdmissionRequest.Services);
        Assert.All(world.Authority.Measured, permission => Assert.Equal(RevisePermission, permission));
    }

    [Fact]
    public async Task Mixed_price_missing_permission_binding_and_pre_cancel_are_closed_before_any_store_admission()
    {
        var world = new World { Bindings = new(RevisePermission: null) };
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ReviseMixedServicePricesAsync(
            Guid.NewGuid(), PriceAdmissionBody(), Metadata("price-unconfigured")))).Status);
        Assert.Equal(0, world.Store.PriceAdmissionCalls); Assert.Equal(0, world.Authority.Calls);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Service().ReviseMixedServicePricesAsync(
            Guid.NewGuid(), PriceAdmissionBody(), Metadata("price-cancelled"), cancellation.Token));
        Assert.Equal(0, world.Store.PriceAdmissionCalls);
    }

    private static RevisePurchaseMixedServicePricesRequest PriceAdmissionBody()
    {
        var child = Guid.NewGuid(); var line = Guid.NewGuid();
        return new("9007199254740993", [new(child, "9223372036854775806", [line])], [new(child, [new(line, "0")])], "SENTETIK native revision");
    }

    private sealed partial class FakeStore : IPurchaseMixedServicePriceRevisionStoreV1
    {
        public bool MixedServicePriceEntryIsClean => true;
        internal int PriceAdmissionCalls;
        internal Guid PriceAdmissionId;
        internal RevisePurchaseMixedServicePricesRequest? PriceAdmissionRequest;
        internal PurchasingWriteEnvelope? PriceAdmissionEnvelope;
        internal string? PriceAdmissionHash;
        public Task<PurchaseMixedServicePriceAnswerV1> ReviseMixedServicePricesAsync(Guid id, RevisePurchaseMixedServicePricesRequest request,
            PurchasingWriteEnvelope envelope, string hash, bool cleanBeforeAuthority,
            Func<StoredPurchaseOrder, string, PurchaseOrderDetailView> project, Func<CancellationToken, Task> freshFence, CancellationToken token = default)
        {
            PriceAdmissionCalls++; PriceAdmissionId = id; PriceAdmissionRequest = request; PriceAdmissionEnvelope = envelope; PriceAdmissionHash = hash;
            return Task.FromException<PurchaseMixedServicePriceAnswerV1>(new PurchasingServiceException(503, "service_unavailable"));
        }
    }
}
