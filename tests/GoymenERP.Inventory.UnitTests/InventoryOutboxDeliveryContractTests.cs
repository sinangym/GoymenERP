using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryOutboxDeliveryContractTests
{
    [Fact]
    public void Contract_pins_registered_consumer_and_requires_explicit_utc_claim_window()
    {
        Assert.Equal("work.shipment-evidence.v1", InventoryOutboxDeliveryV1.ConsumerName);

        var now = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
        InventoryOutboxDeliveryV1.ValidateClaimRequest(new InventoryOutboxDeliveryClaimRequestV1(
            Guid.NewGuid(), InventoryOutboxDeliveryV1.ConsumerName, Guid.NewGuid(), now, now.AddMinutes(1)));

        Assert.Throws<ArgumentException>(() => InventoryOutboxDeliveryV1.ValidateClaimRequest(
            new InventoryOutboxDeliveryClaimRequestV1(
                Guid.NewGuid(), InventoryOutboxDeliveryV1.ConsumerName, Guid.NewGuid(), now, now)));
        Assert.Throws<ArgumentException>(() => InventoryOutboxDeliveryV1.ValidateClaimRequest(
            new InventoryOutboxDeliveryClaimRequestV1(
                Guid.NewGuid(), "unknown", Guid.NewGuid(), now, now.AddMinutes(1))));
    }

    [Fact]
    public void Claim_validation_pins_exact_event_envelope_and_payload_integrity()
    {
        const string payload = "{\"companyId\":\"11111111-1111-1111-1111-111111111111\"}";
        var now = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
        var claim = new InventoryOutboxDeliveryClaimV1(
            Guid.NewGuid(),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            InventoryOutboxDeliveryV1.ConsumerName,
            InventoryDefiniteShipmentOutboxV1.EventType,
            InventoryDefiniteShipmentOutboxV1.SchemaVersion,
            InventoryDefiniteShipmentOutboxV1.SourceOperation,
            InventoryDefiniteShipmentOutboxV1.AggregateType,
            Guid.NewGuid(),
            2,
            2,
            payload,
            InventoryOutboxDeliveryV1.PayloadSha256(payload),
            now,
            "correlation-1",
            null,
            now,
            1,
            Guid.NewGuid(),
            now,
            now.AddMinutes(1));

        InventoryOutboxDeliveryV1.ValidateClaim(claim);

        Assert.Throws<ArgumentException>(() =>
            InventoryOutboxDeliveryV1.ValidateClaim(claim with { PayloadSha256 = new string('0', 64) }));
        Assert.Throws<ArgumentException>(() =>
            InventoryOutboxDeliveryV1.ValidateClaim(claim with { Sequence = claim.Sequence + 1 }));
        Assert.Throws<ArgumentException>(() =>
            InventoryOutboxDeliveryV1.ValidateClaim(claim with { EventType = "inventory.other.v1" }));
    }

    [Fact]
    public void Completion_requires_the_registered_consumer_and_positive_fence()
    {
        var completedAt = new DateTimeOffset(2026, 10, 16, 8, 1, 0, TimeSpan.Zero);
        InventoryOutboxDeliveryV1.ValidateCompletion(new InventoryOutboxDeliveryCompletionV1(
            Guid.NewGuid(), InventoryOutboxDeliveryV1.ConsumerName, Guid.NewGuid(), 1, Guid.NewGuid(), completedAt));

        Assert.Throws<ArgumentException>(() => InventoryOutboxDeliveryV1.ValidateCompletion(
            new InventoryOutboxDeliveryCompletionV1(
                Guid.NewGuid(), InventoryOutboxDeliveryV1.ConsumerName, Guid.NewGuid(), 0, Guid.NewGuid(), completedAt)));
    }
}
