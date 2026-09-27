using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Logistics pins Inventory's definite-shipment 409 set as its durable terminal refusals without an application
/// reference to Inventory. Only the host sees both owners, so the drift check lives here: a new Inventory
/// shipment refusal (e.g. the Sales ownership gate's inventory_definite_shipment_owner_not_company) must become
/// a Logistics terminal attempt in the same change, never an unknown outcome retried forever.
/// </summary>
public sealed class LogisticsInventoryTerminalCodeDriftTests
{
    [Fact]
    public void Logistics_terminal_inventory_codes_are_exactly_the_inventory_definite_shipment_409_codes()
    {
        Assert.Equal(
            InventoryDefiniteShipmentCodesV1.Status409Codes.Order(StringComparer.Ordinal),
            LogisticsRealizationDependenciesV1.InventoryTerminalConflictCodes.Order(StringComparer.Ordinal));
    }
}
