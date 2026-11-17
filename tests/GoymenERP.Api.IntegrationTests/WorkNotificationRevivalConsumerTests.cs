using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Api.IntegrationTests;

public sealed class WorkNotificationRevivalConsumerTests
{
    [Fact]
    public async Task Revival_consumes_only_immutable_sales_targets_and_the_recorded_purchase_target()
    {
        var storage = new RecordingDelivery();
        var consumer = new PurchaseSupplyRevivedNotificationConsumerV1(storage, "purchasing");
        var payload = Payload(); var message = Envelope(payload);
        await consumer.HandleAsync(message, CancellationToken.None);
        var command = Assert.Single(storage.Commands);
        Assert.Equal(WorkNotificationKindV1.PurchaseSupplyRevived, command.Kind);
        Assert.Equal(message.PayloadJson, command.PayloadJson);
        Assert.Equal(new WorkPurchaseNotificationTargetV1(payload.PurchaseOrderId, payload.PurchaseOrderLineId), command.PurchaseTarget);
        Assert.Equal(payload.SalesTargets!.Select(row => new WorkNotificationTargetV1(row.SalesOrderId, row.SalesOrderLineId)), command.Targets);
        Assert.Equal((payload.PurchaseOrderLineId, "7", message.CorrelationId, message.CausationId),
            (command.SourceEntityId, command.SourceVersion, command.CorrelationId, command.CausationId));
        // Explicitly no historical allocation is a real empty sales audience, while purchase recipients still exist.
        await consumer.HandleAsync(Envelope(payload with { SalesTargets = [] }), CancellationToken.None);
        Assert.Empty(storage.Commands[1].Targets);
        Assert.NotNull(storage.Commands[1].PurchaseTarget);
    }

    [Fact]
    public async Task Revival_refuses_missing_or_duplicated_target_evidence_foreign_company_and_ambiguous_operation()
    {
        var storage = new RecordingDelivery();
        var consumer = new PurchaseSupplyRevivedNotificationConsumerV1(storage, "purchasing");
        var payload = Payload(); var message = Envelope(payload);
        foreach (var bad in new[] { Envelope(payload with { SalesTargets = null }),
            Envelope(payload with { SalesTargets = [payload.SalesTargets![0], payload.SalesTargets[0]] }),
            Envelope(payload with { ReceiptAdjustmentId = null }), Envelope(payload with { BatchId = Guid.NewGuid() }),
            Envelope(payload with { OrderVersion = 0 }), Envelope(payload with { NetOpenedQuantity = "0" }),
            Envelope(payload with { NetOpenedQuantity = "01" }), message with { CompanyId = Guid.NewGuid() },
            message with { SourceName = "foreign" }, message with { Version = 2 },
            message with { PayloadJson = message.PayloadJson[..^1] + ",\"companyId\":\"" + message.CompanyId + "\"}" },
            message with { PayloadJson = message.PayloadJson.Replace("\"salesOrderLineId\":", "\"invented\":") },
            message with { PayloadJson = "{}" } })
            Assert.Equal("notification_revival_contract_invalid", (await Assert.ThrowsAsync<OutboxPermanentFailureException>(
                () => consumer.HandleAsync(bad, CancellationToken.None))).Code);
        Assert.Empty(storage.Commands);
    }

    private static PurchasingOrderLineRevivedPayloadV1 Payload() => new(TestCompanyIds.CompanyA, Guid.NewGuid(), Guid.NewGuid(),
        7, "2.5", Guid.NewGuid(), null, [new(Guid.NewGuid(), Guid.NewGuid()), new(Guid.NewGuid(), Guid.NewGuid())]);
    private static WorkInboxEventV1 Envelope(PurchasingOrderLineRevivedPayloadV1 payload) => new("purchasing", Guid.NewGuid(), payload.CompanyId,
        PurchasingOrderLineRevivedEventV1.EventType, 1, JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        DateTimeOffset.Parse("2026-11-17T00:00:00Z"), "SENTETIK-revival", "SENTETIK-cause");
    private sealed class RecordingDelivery : IWorkNotificationDeliveryV1
    {
        public List<DeliverWorkNotificationV1> Commands { get; } = [];
        public Task DeliverAsync(DeliverWorkNotificationV1 command, CancellationToken cancellationToken = default)
        { Commands.Add(command); return Task.CompletedTask; }
    }
}
