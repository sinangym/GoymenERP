using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryDefiniteShipmentOutboxContractTests
{
    [Fact]
    public void Event_identity_and_schema_are_versioned_and_stable()
    {
        Assert.Equal("inventory.definite-shipment.recorded.v1", InventoryDefiniteShipmentOutboxV1.EventType);
        Assert.Equal(1, InventoryDefiniteShipmentOutboxV1.SchemaVersion);
        Assert.Equal("inventory.definite-shipment.record.v1", InventoryDefiniteShipmentOutboxV1.SourceOperation);
        Assert.Equal("Inventory.StockAllocation", InventoryDefiniteShipmentOutboxV1.AggregateType);
    }

    [Fact]
    public void Payload_contains_exact_business_fields_and_no_authority_or_replay_metadata()
    {
        var payload = new InventoryDefiniteShipmentRecordedV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "2", Guid.NewGuid(), "3", Guid.NewGuid(), "40.125",
            new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero), "correlation", "causation");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var names = json.RootElement.EnumerateObject().Select(property => property.Name).ToArray();

        Assert.Equal([
            "companyId", "shipmentId", "loadingInstructionId", "loadingInstructionLineId",
            "allocationId", "allocationVersion", "labelId", "labelVersion", "movementId",
            "quantityKg", "occurredAtUtc", "correlationId", "causationId",
        ], names);
        Assert.DoesNotContain(names, name => name.Contains("actor", StringComparison.OrdinalIgnoreCase)
            || name.Contains("permission", StringComparison.OrdinalIgnoreCase)
            || name.Contains("role", StringComparison.OrdinalIgnoreCase)
            || name.Contains("replay", StringComparison.OrdinalIgnoreCase)
            || name.Contains("idempotency", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("40.125", json.RootElement.GetProperty("quantityKg").GetString());
    }
}
