using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// BS-5 / BS-14 (owner decisions of 2026-11-11): the production status a label carries apart from its
/// <see cref="InventoryLabelClassificationV1"/>. It is descriptive only (no stock, block, disposal or sellability
/// effect; sellability is decided separately, DEC-PRDCT-007). A label without a production status carries null.
/// </summary>
[JsonConverter(typeof(InventoryLabelProductionStatusJsonConverterV1))]
public enum InventoryLabelProductionStatusV1
{
    /// <summary>The label was born from a Manufacturing <c>UsableRemnant</c> output.</summary>
    UsableRemnant = 1,
}

/// <summary>
/// The contract carries the enum name as a string. Numeric tokens are not an alternate wire contract, even when they
/// happen to map to a defined CLR value.
/// </summary>
public sealed class InventoryLabelProductionStatusJsonConverterV1()
    : JsonStringEnumConverter<InventoryLabelProductionStatusV1>(namingPolicy: null, allowIntegerValues: false);
