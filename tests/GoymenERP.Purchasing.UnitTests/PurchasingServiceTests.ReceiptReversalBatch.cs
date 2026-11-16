using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    private static ReversePurchaseReceiptsV1 ReversalBatchRequest() => new(Guid.NewGuid(), "4", "SENTETIK batch correction",
        DateTimeOffset.Parse("2026-11-17T00:00:00Z"),
        [new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "2", "1.9", "Free"),
         new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "3", "2.8", "Blocked")]);

    [Fact]
    public void Batch_wire_keeps_one_expected_version_and_refuses_duplicate_source_or_effect_identities()
    {
        var request = ReversalBatchRequest(); PurchaseReceiptReversalBatchJsonV1.Validate(request);
        var duplicateSource = request with { Lines = [request.Lines[0], request.Lines[1] with { AcceptanceId = request.Lines[0].AcceptanceId }] };
        Assert.Throws<JsonException>(() => PurchaseReceiptReversalBatchJsonV1.Validate(duplicateSource));
        var duplicateMovement = request with { Lines = [request.Lines[0], request.Lines[1] with { InventoryMovementId = request.Lines[0].InventoryMovementId }] };
        Assert.Throws<JsonException>(() => PurchaseReceiptReversalBatchJsonV1.Validate(duplicateMovement));
        Assert.Throws<JsonException>(() => PurchaseReceiptReversalBatchJsonV1.Validate(request with { ExpectedPurchaseOrderVersion = "04" }));
        Assert.Throws<JsonException>(() => PurchaseReceiptReversalBatchJsonV1.Validate(request with { Lines = [] }));
    }

    [Theory]
    [InlineData(422, "inventory_goods_receipt_reversal_partial_quantity_not_representable")]
    [InlineData(503, "purchasing_receipt_adjustment_unavailable")]
    [InlineData(409, "inventory_concurrent_change")]
    public async Task A_batch_rule_or_transient_refusal_is_not_recorded_and_the_same_key_can_be_retried(int status, string code)
    {
        var world = new World(); var host = new ReversalBatchHostFake { Failure = new(status, code) };
        var request = ReversalBatchRequest(); var order = Guid.NewGuid();
        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(reversalBatches: host)
            .ReverseGoodsReceiptsAsync(order, request, Metadata("reverse-batch-retry")));
        Assert.Equal((status, code), (refused.Status, refused.Code)); Assert.Empty(world.Store.Refusals);
        host.Failure = null;
        var result = await world.Service(reversalBatches: host).ReverseGoodsReceiptsAsync(order, request, Metadata("reverse-batch-retry"));
        Assert.Equal(request.BatchId, result.BatchId); Assert.Equal(request.Lines.Count, result.Lines.Count);
        Assert.Equal((2, 1), (host.Calls, host.Writes)); Assert.All(result.Lines, line => Assert.Equal(result.OrderVersion, line.Purchase.OrderVersion));
    }

    [Fact]
    public async Task A_batch_owner_wait_rechecks_the_captured_company_actor_and_permission_before_any_write()
    {
        foreach (var fault in new[] { "permission", "company", "actor" })
        {
            var world = new World(); var host = new ReversalBatchHostFake { BeforeFence = () =>
            {
                if (fault == "permission") world.Authority.Granted = false;
                if (fault == "company") world.Company.Set(Guid.NewGuid());
                if (fault == "actor") world.User.CurrentActor = "another-synthetic-actor";
            }};
            var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(reversalBatches: host)
                .ReverseGoodsReceiptsAsync(Guid.NewGuid(), ReversalBatchRequest(), Metadata("reverse-batch-fence")));
            Assert.Equal(403, refused.Status); Assert.Equal(0, host.Writes); Assert.Empty(world.Store.Refusals);
        }
    }

    private sealed class ReversalBatchHostFake : IPurchaseReceiptReversalBatchTransactionV1
    {
        public int Calls { get; private set; }
        public int Writes { get; private set; }
        public Action? BeforeFence { get; init; }
        public PurchasingServiceException? Failure { get; set; }
        public async Task<PurchaseReceiptReversalBatchResponseV1> ExecuteBatchAsync(Guid companyId, PurchaseReceiptReversalBatchWriteV1 write,
            PurchasingWriteEnvelope envelope, Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default)
        {
            Calls++; BeforeFence?.Invoke(); await freshAuthority(cancellationToken);
            if (Failure is not null) throw Failure;
            Writes++;
            return new(companyId, write.OrderId, write.Request.BatchId, "5", write.Request.Lines.Select(line =>
                new PurchaseReceiptReversalResponseV1(new(companyId, line.AdjustmentId, write.OrderId, line.AcceptanceId,
                    Guid.NewGuid(), Guid.NewGuid(), "5", "2", "2", false, false, envelope.CorrelationId),
                    new(companyId, line.AdjustmentId, line.AcceptanceId, line.InventoryMovementId, Guid.NewGuid(), Guid.NewGuid(), "2",
                        line.MeasuredUndoQuantityKg, [], false, envelope.CorrelationId), null)).ToArray(), false, false, envelope.CorrelationId);
        }
    }
}
