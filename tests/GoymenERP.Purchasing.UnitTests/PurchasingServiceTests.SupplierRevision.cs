using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    [Fact]
    public async Task Supplier_revision_uses_existing_revise_permission_replays_and_keeps_approval_separate()
    {
        var world = new World(); var (id, _) = await SupplierRevisionApprovedOrderAsync(world);
        var request = new RevisePurchaseOrderSupplierRequest("3", Guid.NewGuid(), Guid.NewGuid(), "SENTETIK revision");
        var result = await world.Service().ReviseOrderSupplierAsync(id, request, Metadata("supplier-revision"));
        Assert.Equal(("Draft", "4"), (result.Detail.Order.Status, result.Detail.Order.Version));
        Assert.Equal(RevisePermission, world.Store.Envelopes.Last().PermissionCode);
        Assert.Equal("purchasing.purchase-order.supplier-revised.v1", world.Store.Audits.Last().EventType);
        var replay = await world.Service().ReviseOrderSupplierAsync(id, request, Metadata("supplier-revision", correlation: "retry"));
        Assert.True(replay.IsReplay);
        Assert.Single(world.Store.Transitions, item => item.Command == PurchasingCommands.OrderSupplierReviseV1);
        var history = await world.Service().GetSupplierRevisionsAsync(id);
        Assert.Equal(1, history.Revision);
        Assert.Equal(request.SupplierPartyId, Assert.Single(history.Revisions).SupplierPartyId);
    }

    [Fact]
    public async Task Invoice_history_refusal_is_terminal_and_same_key_different_supplier_is_not_reexecuted()
    {
        var world = new World(); var (id, _) = await SupplierRevisionApprovedOrderAsync(world);
        world.Store.SupplierInvoiceHistory = PurchaseOrderInvoiceHistory.Exists;
        var request = new RevisePurchaseOrderSupplierRequest("3", Guid.NewGuid(), Guid.NewGuid(), "SENTETIK revision");
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ReviseOrderSupplierAsync(id, request, Metadata("invoice-refused")));
        Assert.Equal((409, PurchaseOrder.SupplierRevisionBlocked), (refusal.Status, refusal.Code));
        var again = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ReviseOrderSupplierAsync(id, request, Metadata("invoice-refused")));
        Assert.True(again.IsReplay);
        var mismatch = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ReviseOrderSupplierAsync(id,
            request with { SupplierPartyId = Guid.NewGuid() }, Metadata("invoice-refused")));
        Assert.Equal(PurchasingProblemCodesV1.SameKeyPayloadMismatch, mismatch.Code);
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == PurchasingCommands.OrderSupplierReviseV1);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("reason")]
    [InlineData("company")]
    public async Task Stale_version_missing_reason_or_changed_company_never_revises_supplier(string fault)
    {
        var world = new World(); var (id, _) = await SupplierRevisionApprovedOrderAsync(world);
        var request = new RevisePurchaseOrderSupplierRequest(fault == "version" ? "2" : "3", Guid.NewGuid(), Guid.NewGuid(), fault == "reason" ? " " : "SENTETIK");
        if (fault == "company") world.Store.BeforeSupplierRevisionFence = () => world.Company.Set(Guid.NewGuid());
        await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ReviseOrderSupplierAsync(id, request, Metadata("refuse")));
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == PurchasingCommands.OrderSupplierReviseV1);
    }

    [Theory]
    [InlineData("{\"expectedVersion\":\"3\",\"supplierPartyId\":\"11111111-1111-4111-8111-111111111111\",\"companyAccountId\":\"22222222-2222-4222-8222-222222222222\",\"reason\":\"SENTETIK\",\"companyId\":\"33333333-3333-4333-8333-333333333333\"}")]
    [InlineData("{\"expectedVersion\":\"3\",\"supplierPartyId\":\"11111111-1111-4111-8111-111111111111\",\"companyAccountId\":\"22222222-2222-4222-8222-222222222222\"}")]
    public void Supplier_revision_wire_rejects_client_company_and_missing_fields(string json) =>
        Assert.Throws<System.Text.Json.JsonException>(() => PurchaseSupplierRevisionJsonV1.ReadRevision(json));

    [Fact]
    public async Task Missing_legacy_money_snapshot_never_changes_supplier_or_discards_approval()
    {
        var world = new World(); var (id, _) = await ApprovedOrderAsync(world, "10");
        var request = new RevisePurchaseOrderSupplierRequest("3", Guid.NewGuid(), Guid.NewGuid(), "SENTETIK revision");
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ReviseOrderSupplierAsync(id, request, Metadata("legacy-money")));
        Assert.Equal((503, "service_unavailable"), (refusal.Status, refusal.Code));
        var stored = (await world.Store.FindOrderAsync(id))!.Order;
        Assert.Equal((3L, PurchaseOrderStatus.Approved, Party), (stored.Version, stored.Status, stored.SupplierPartyId));
    }

    private static async Task<(Guid Order, Guid Line)> SupplierRevisionApprovedOrderAsync(World world)
    {
        var created = await world.Service().CreateOrderAsync(PricedOrderOf("10", new PurchaseOrderLinePriceRequest("12.5")), Metadata("supplier-original"));
        await world.AsApprover(() => world.Service().ApproveOrderAsync(created.Detail.Order.Id, new ApprovePurchaseOrderRequest("2"), Metadata("supplier-original-approval")));
        return (created.Detail.Order.Id, created.Detail.Lines[0].LineId);
    }

    [Fact]
    public async Task Price_read_revoked_during_write_withholds_the_new_price_in_the_command_response()
    {
        var world = new World(); var (id, line) = await SupplierRevisionApprovedOrderAsync(world);
        await world.Service().ReviseOrderSupplierAsync(id, new("3", Guid.NewGuid(), Guid.NewGuid(), "SENTETIK revision"), Metadata("supplier-revision"));
        world.Store.BeforeSupplierRevisionFence = () => world.Authority.Denied.Add(PurchasingPermissionManifestV1.OrdersPriceRead);
        var response = await world.Service().SetSupplierRevisionPriceAsync(id, line, new("4", new("17.5")), Metadata("new-price"));
        Assert.Equal(PurchasingPriceDisclosureV1.Withheld, response.Detail.PriceDisclosure);
        Assert.Null(response.Detail.Lines[0].Price);
        Assert.Equal(17.5m, (await world.Store.FindOrderAsync(id))!.Order.Lines[0].Price!.UnitPrice);
    }

    private sealed partial class FakeStore : IPurchasingSupplierRevisionStoreV1
    {
        public PurchaseOrderInvoiceHistory SupplierInvoiceHistory { get; set; } = PurchaseOrderInvoiceHistory.None;
        public Action? BeforeSupplierRevisionFence { get; set; }

        public async Task<PurchasingOrderStoreResult?> ReviseSupplierAsync(Guid orderId, long expectedVersion, Guid partyId, Guid companyAccountId,
            string reason, PurchasingWriteEnvelope envelope, string requestHash, Func<CancellationToken, Task> requireStillAuthorized,
            CancellationToken cancellationToken = default)
        {
            BeforeSupplierRevisionFence?.Invoke();
            await requireStillAuthorized(cancellationToken);
            var result = await MutateOrderAsync(envelope, requestHash, PurchasingCommands.OrderSupplierReviseV1, orderId,
                (order, now) => order.ReviseSupplier(expectedVersion, partyId, SupplierInvoiceHistory, envelope.ActorId, reason, now),
                new("purchasing.purchase-order.supplier-revised.v1", "ReviseSupplier", reason), cancellationToken);
            if (result is null) return null;
            var revised = result.Order with { CompanyAccountId = companyAccountId };
            _orders[_orders.FindIndex(item => item.Order.Id == orderId)] = revised;
            var key = (PurchasingCommands.OrderSupplierReviseV1, envelope.IdempotencyKey);
            _attempts[key] = _attempts[key] with { Order = revised };
            return result with { Order = revised };
        }

        public async Task<PurchasingOrderStoreResult?> SetRevisionPriceAsync(Guid orderId, Guid lineId, long expectedVersion, PurchaseOrderLinePrice price,
            PurchasingWriteEnvelope envelope, string requestHash, Func<CancellationToken, Task> requireStillAuthorized, CancellationToken cancellationToken = default)
        {
            BeforeSupplierRevisionFence?.Invoke(); await requireStillAuthorized(cancellationToken);
            return await MutateOrderAsync(envelope, requestHash, PurchasingCommands.OrderSupplierRevisionPriceV1, orderId,
                (order, now) => order.SetSupplierRevisionPrice(expectedVersion, lineId, price, envelope.ActorId, now),
                new("purchasing.purchase-order.supplier-revision-price-recorded.v1", "SetRevisionPrice", "SENTETIK"), cancellationToken);
        }
    }
}
