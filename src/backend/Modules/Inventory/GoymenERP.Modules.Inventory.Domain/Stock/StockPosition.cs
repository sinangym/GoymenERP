namespace GoymenERP.Modules.Inventory.Domain.Stock;

/// <summary>Who owns the goods. Being in the same group is not a right to use another's stock (§5).</summary>
public enum StockOwnerKind
{
    /// <summary>One of the group's own legal companies.</summary>
    Company = 1,

    /// <summary>A counterparty's goods held by us (customer material, consignment, fason input).</summary>
    Counterparty = 2,
}

/// <summary>The owner of the goods, by the owning module's identity only.</summary>
public readonly record struct StockOwner
{
    public StockOwner(StockOwnerKind kind, Guid id)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new InventoryRuleException(InventoryRuleCodes.MissingReference, "Mal sahibi türü tanımsız.");
        }

        Kind = kind;
        Id = StockIds.Require(id, "Mal sahibi");
    }

    public StockOwnerKind Kind { get; }

    public Guid Id { get; }
}

/// <summary>
/// Blocking is its own dimension (§7.1): blocked stock is still physically there and still
/// counted; it is simply not free. Blocking or releasing moves quantity between the two,
/// it never adds or removes physical stock.
/// </summary>
public enum BlockState
{
    Free = 1,
    Blocked = 2,
}

/// <summary>
/// One physical balance bucket. The TRANSACTION company, the OWNER of the goods and the
/// PHYSICAL location are three separate facts (§5): goods may belong to a customer, be
/// held by one of our companies and sit in a location of another. None of them is folded
/// into another, so stock of one owner can never satisfy a movement for another.
/// Cross-module references are identities only — no other module is referenced.
/// </summary>
public sealed record StockPosition
{
    public StockPosition(Guid companyId, StockOwner owner, Guid locationId, Guid productId,
        Guid labelId, BlockState block)
    {
        CompanyId = StockIds.Require(companyId, "İşlem şirketi");
        if (owner.Id == Guid.Empty)
        {
            throw new InventoryRuleException(InventoryRuleCodes.MissingReference, "Mal sahibi zorunludur.");
        }

        Owner = owner;
        LocationId = StockIds.Require(locationId, "Fiziksel konum");
        ProductId = StockIds.Require(productId, "Ürün");
        LabelId = StockIds.Require(labelId, "Malzeme etiketi");
        if (!Enum.IsDefined(block))
        {
            throw new InventoryRuleException(InventoryRuleCodes.MissingReference, "Blokaj durumu tanımsız.");
        }

        Block = block;
    }

    /// <summary>The company the transaction is recorded under (Organization identity).</summary>
    public Guid CompanyId { get; }

    public StockOwner Owner { get; }

    /// <summary>The physical location (Organization identity).</summary>
    public Guid LocationId { get; }

    /// <summary>The Product identity; the label is the physical piece, not the product card.</summary>
    public Guid ProductId { get; }

    /// <summary>The physical identity of the material (<see cref="Labels.MaterialLabel"/>).</summary>
    public Guid LabelId { get; }

    public BlockState Block { get; }

    public StockPosition WithBlock(BlockState block) =>
        new(CompanyId, Owner, LocationId, ProductId, LabelId, block);
}

internal static class StockIds
{
    public static Guid Require(Guid id, string what) =>
        id == Guid.Empty
            ? throw new InventoryRuleException(InventoryRuleCodes.MissingReference, $"{what} kimliği zorunludur.")
            : id;
}
