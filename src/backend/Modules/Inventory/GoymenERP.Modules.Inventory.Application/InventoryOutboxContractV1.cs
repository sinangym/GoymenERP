namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Stable Work committed-outbox source identity; registration does not activate the dispatcher.</summary>
public static class InventoryOutboxContractV1
{
    public const string SourceName = "inventory.outbox.v1";
    public const string ManufacturingCostDifferenceProposed = "inventory.manufacturing-cost-difference.proposed.v1";
    public const int ManufacturingCostDifferenceSchemaVersion = 1;
}
