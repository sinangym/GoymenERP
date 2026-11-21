using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Api.IntegrationTests;

public sealed class WorkNotificationReleaseConsumerTests
{
    [Fact]
    public async Task Receipt_link_failure_keeps_exact_producer_target_and_provenance_and_does_not_infer_other_release_policies()
    {
        var storage = new RecordingDelivery();
        var consumer = new InventoryAllocationReleaseNotificationConsumerV1(storage, "inventory.notifications");
        var value = Payload();
        var message = Envelope(value);
        await consumer.HandleAsync(message, CancellationToken.None);
        var command = Assert.Single(storage.Commands);
        Assert.Equal(message.EventId, command.EventId);
        Assert.Equal(message.PayloadJson, command.PayloadJson);
        Assert.Equal(new WorkNotificationTargetV1(value.SalesOrderId, value.SalesOrderLineId), Assert.Single(command.Targets));
        Assert.Equal((value.AllocationId, value.AllocationVersion, value.CorrelationId), (command.SourceEntityId, command.SourceVersion, command.CorrelationId));
        await consumer.HandleAsync(Envelope(value with { ReasonCode = InventoryFutureAllocationReleaseReasonsV1.Manual }), CancellationToken.None);
        Assert.Single(storage.Commands);
    }
    [Fact]
    public async Task Release_consumer_refuses_foreign_company_changed_envelope_missing_duplicate_extra_and_unsafe_version()
    {
        var storage = new RecordingDelivery();
        var consumer = new InventoryAllocationReleaseNotificationConsumerV1(storage, "inventory.notifications");
        var message = Envelope(Payload());
        foreach (var bad in new[] { message with { CompanyId = Guid.NewGuid() }, message with { SourceName = "foreign" },
            message with { CorrelationId = "other" }, message with { Version = 2 }, Envelope(Payload() with { AllocationVersion = "9223372036854775808" }),
            message with { PayloadJson = message.PayloadJson[..^1] + ",\"extra\":true}" },
            message with { PayloadJson = message.PayloadJson[..^1] + ",\"companyId\":\"" + message.CompanyId + "\"}" },
            message with { PayloadJson = "{}" } })
            Assert.Equal("notification_release_contract_invalid", (await Assert.ThrowsAsync<OutboxPermanentFailureException>(
                () => consumer.HandleAsync(bad, CancellationToken.None))).Code);
        Assert.Empty(storage.Commands);
    }
    private static InventoryFutureAllocationReleasedV1 Payload() => new(TestCompanyIds.CompanyA, Guid.NewGuid(), "2",
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1", "0", "Released",
        InventoryFutureAllocationReleaseReasonsV1.AcceptanceLinkFailed, "SENTETIK ayrım bırakıldı", "inventory", "SENTETIK-corr", null);
    private static WorkInboxEventV1 Envelope(InventoryFutureAllocationReleasedV1 payload) => new("inventory.notifications", Guid.NewGuid(), payload.CompanyId,
        InventoryFutureAllocationReleasedOutboxV1.EventType, 1, JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        DateTimeOffset.Parse("2026-11-17T00:00:00Z"), payload.CorrelationId, payload.CausationId);
    private sealed class RecordingDelivery : IWorkNotificationDeliveryV1
    {
        public List<DeliverWorkNotificationV1> Commands { get; } = [];
        public Task DeliverAsync(DeliverWorkNotificationV1 command, CancellationToken cancellationToken = default) { Commands.Add(command); return Task.CompletedTask; }
    }
}
