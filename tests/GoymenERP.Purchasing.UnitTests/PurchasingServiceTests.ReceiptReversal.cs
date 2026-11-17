using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>Explicitly synthetic service/owner protocol evidence; not combined host or SQL acceptance.</summary>
public sealed partial class PurchasingServiceTests
{
    [Fact]
    public async Task Receipt_reversal_without_its_real_transaction_host_is_unavailable()
    {
        var world = new World();
        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service()
            .ReverseGoodsReceiptAsync(Guid.NewGuid(), Guid.NewGuid(), ReversalRequest(), Metadata("reverse-no-host")));
        Assert.Equal((503, PurchaseGoodsReceiptCodesV1.Unavailable), (refused.Status, refused.Code));
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("company")]
    [InlineData("actor")]
    public async Task Receipt_reversal_refresh_keeps_the_original_company_actor_and_permission(string fault)
    {
        var world = new World();
        var host = new ReversalHostFake { BeforeFence = () =>
        {
            if (fault == "permission") world.Authority.Granted = false;
            if (fault == "company") world.Company.Set(Guid.NewGuid());
            if (fault == "actor") world.User.CurrentActor = "another-synthetic-actor";
        }};
        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(reversals: host)
            .ReverseGoodsReceiptAsync(Guid.NewGuid(), Guid.NewGuid(), ReversalRequest(), Metadata("reverse-fence")));
        Assert.Equal(403, refused.Status);
        Assert.Equal(0, host.Writes);
    }

    [Fact]
    public async Task Receipt_reversal_terminal_refusal_replays_before_the_host_and_rejects_changed_content()
    {
        var world = new World(); var host = new ReversalHostFake { ReplayStore = world.Store,
            Failure = new(409, "inventory_future_allocation_already_fulfilled") };
        var order = Guid.NewGuid(); var acceptance = Guid.NewGuid(); var request = ReversalRequest();
        var first = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(reversals: host)
            .ReverseGoodsReceiptAsync(order, acceptance, request, Metadata("reverse-terminal")));
        Assert.False(first.IsReplay);
        host.Failure = null;
        var replay = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(reversals: host)
            .ReverseGoodsReceiptAsync(order, acceptance, request, Metadata("reverse-terminal", correlation: "different-correlation")));
        Assert.True(replay.IsReplay);
        Assert.Equal((first.Code, "synthetic-correlation", 1), (replay.Code, replay.OriginalCorrelationId, host.BusinessCalls));
        var changed = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(reversals: host)
            .ReverseGoodsReceiptAsync(order, acceptance, request with { UndoQuantity = "1.5" }, Metadata("reverse-terminal")));
        Assert.Equal(PurchasingProblemCodesV1.SameKeyPayloadMismatch, changed.Code);
    }

    [Theory]
    [InlineData(503, "purchasing_receipt_adjustment_unavailable")]
    [InlineData(409, "purchasing_line_busy")]
    [InlineData(409, "sales_order_line_busy")]
    [InlineData(409, "inventory_concurrent_change")]
    [InlineData(422, "inventory_goods_receipt_reversal_partial_quantity_not_representable")]
    public async Task Receipt_reversal_transient_failure_does_not_record_the_key_and_the_same_key_can_succeed(int status, string code)
    {
        var world = new World(); var host = new ReversalHostFake { Failure = new(status, code) };
        var order = Guid.NewGuid(); var acceptance = Guid.NewGuid(); var request = ReversalRequest();
        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(reversals: host)
            .ReverseGoodsReceiptAsync(order, acceptance, request, Metadata("reverse-transient")));
        Assert.Equal((status, code), (refused.Status, refused.Code));
        host.Failure = null;
        var success = await world.Service(reversals: host).ReverseGoodsReceiptAsync(order, acceptance, request, Metadata("reverse-transient"));
        Assert.False(success.Purchase.IsReplay);
        Assert.Equal((2, 1), (host.Calls, host.Writes));
        Assert.Equal(("2", "1.9"), (host.Last!.Request.UndoQuantity, host.Last.Request.MeasuredUndoQuantityKg));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("purchase-quantity")]
    [InlineData("measured-quantity")]
    [InlineData("block")]
    public void Receipt_reversal_wire_refuses_noncanonical_versions_quantities_and_blocks(string fault)
    {
        var request = ReversalRequest();
        request = fault switch
        {
            "version" => request with { ExpectedPurchaseOrderVersion = "04" },
            "purchase-quantity" => request with { UndoQuantity = "2.000" },
            "measured-quantity" => request with { MeasuredUndoQuantityKg = "1.0001" },
            _ => request with { SourceBlock = "free" },
        };
        Assert.Throws<JsonException>(() => PurchaseReceiptReversalJsonV1.Validate(request));
    }

    private static ReversePurchaseReceiptV1 ReversalRequest() => new("4", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "2", "1.9", "Free", "SENTETIK receipt correction", DateTimeOffset.Parse("2026-11-17T00:00:00Z"));

    private sealed class ReversalHostFake : IPurchaseReceiptReversalTransactionV1
    {
        public int Calls { get; private set; }
        public int Writes { get; private set; }
        public int BusinessCalls { get; private set; }
        public IPurchasingStore? ReplayStore { get; init; }
        public Action? BeforeFence { get; init; }
        public PurchasingServiceException? Failure { get; set; }
        public PurchaseReceiptReversalWriteV1? Last { get; private set; }
        public async Task<PurchaseReceiptReversalResponseV1> ExecuteAsync(Guid companyId, PurchaseReceiptReversalWriteV1 write,
            PurchasingWriteEnvelope envelope, Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default)
        {
            Calls++; Last = write; BeforeFence?.Invoke(); await freshAuthority(cancellationToken);
            if (ReplayStore is not null)
            {
                PurchasingReplay? stored;
                try { stored = await ReplayStore.FindReplayAsync(envelope, PurchaseReceiptReversalHashV1.Compute(write),
                    PurchaseReceiptReversalApiV1.CommandName, cancellationToken); }
                catch (PurchasingStoreException exception) { throw new PurchasingServiceException(409, exception.Code); }
                await freshAuthority(cancellationToken);
                if (stored?.Refusal is { } refusal)
                    throw new PurchasingServiceException(refusal.Status, refusal.Code, refusal.Errors, isReplay: true,
                        originalCorrelationId: stored.OriginalCorrelationId);
            }
            BusinessCalls++;
            if (Failure is not null) throw Failure;
            Writes++;
            return new(new(companyId, write.Request.AdjustmentId, write.OrderId, write.AcceptanceId, Guid.NewGuid(), Guid.NewGuid(),
                "5", "2", "2", false, false, envelope.CorrelationId),
                new(companyId, write.Request.AdjustmentId, write.AcceptanceId, write.Request.InventoryMovementId,
                    Guid.NewGuid(), Guid.NewGuid(), "2", write.Request.MeasuredUndoQuantityKg, [], false, envelope.CorrelationId), null);
        }
    }
}
