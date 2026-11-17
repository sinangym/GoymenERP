namespace GoymenERP.Modules.Inventory.Application;

public enum InventoryProductUnitConversionOutcomeV1 { Converted = 1, NoSource = 2, Denied = 3, Unavailable = 4 }
public sealed record InventoryProductUnitConversionAnswerV1(InventoryProductUnitConversionOutcomeV1 Outcome,
    decimal? Quantity, decimal? RemainderKg = null);

/// <summary>
/// Inventory's consumer contract for the Product-owned deterministic B-07 source. A null target means kg.
/// No Product table or formula crosses the module boundary. The host uses the server's current business date.
/// </summary>
public interface IInventoryProductUnitConversionSourceV1
{
    Task<InventoryProductUnitConversionAnswerV1> ConvertAsync(Guid productId, decimal quantity,
        Guid fromUnitId, Guid? toUnitId, CancellationToken cancellationToken = default);
}
