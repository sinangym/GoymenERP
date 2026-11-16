namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// STK-S24 (SAT-D322 (1)): the physical kilograms the company still holds of each product, for the Logistics weighing-difference
/// record. Physical means the stored balance itself: allocations are not subtracted (an allocation is not a physical movement,
/// DEC-STK-003, and SAT-D322 (1) lets the sale use stock allocated to another order). Only the Free block of goods the resolved
/// company owns and holds itself is counted; blocked goods and goods held by a counterparty are left out (the narrow reading, so
/// that a doubtful kilogram raises an approval rather than hiding one; reported as NEEDS-SINAN with the slice).
/// Side-effect free, untracked and company-filtered; it carries no authority.
/// </summary>
public interface IInventoryProductPhysicalStockReadPortV1
{
    /// <summary>
    /// Every requested product appears in the answer; a product without a balance reads 0. A company other than the
    /// server-resolved one answers <see cref="InventoryProductPhysicalStockReadV1.Unreadable"/>, never another company's figure.
    /// </summary>
    Task<InventoryProductPhysicalStockReadV1> ReadAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default);
}

public sealed record InventoryProductPhysicalStockReadV1(
    bool IsReadable,
    IReadOnlyDictionary<Guid, decimal> PhysicalKgByProduct)
{
    public static InventoryProductPhysicalStockReadV1 Unreadable { get; } = new(false, new Dictionary<Guid, decimal>());
}
