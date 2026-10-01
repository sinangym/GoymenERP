using GoymenERP.Modules.Inventory.Domain.Labels;

namespace GoymenERP.Modules.Inventory.Domain.Stock;

/// <summary>Which kind of party an owner or custodian is.</summary>
public enum StockPartyKind
{
    /// <summary>One of the group's own legal companies (Organization identity).</summary>
    Company = 1,

    /// <summary>A counterparty (Counterparty identity): a customer, a fason supplier, a warehouse keeper.</summary>
    Counterparty = 2,
}

/// <summary>
/// Who owns the goods. Being in the same group is not a right to use another's stock (§5).
/// </summary>
public readonly record struct StockOwner
{
    public StockOwner(StockPartyKind kind, Guid id)
    {
        Kind = StockIds.RequireKind(kind, "Mal sahibi");
        Id = StockIds.Require(id, "Mal sahibi");
    }

    public StockPartyKind Kind { get; }

    public Guid Id { get; }
}

/// <summary>
/// Who physically keeps the goods (§5 "gözeten taraf"): our own company, or e.g. a fason
/// supplier or an external warehouse holding them. Not the owner and not the location: a
/// customer's coil (owner) can be kept by a fason supplier (custodian) in that supplier's
/// yard (location), all under our company's transaction.
/// </summary>
public readonly record struct StockCustodian
{
    public StockCustodian(StockPartyKind kind, Guid id)
    {
        Kind = StockIds.RequireKind(kind, "Gözeten taraf");
        Id = StockIds.Require(id, "Gözeten taraf");
    }

    public StockPartyKind Kind { get; }

    public Guid Id { get; }
}

/// <summary>
/// Blocking is its own dimension (§7.1): blocked stock is still physically there and still
/// counted; it is simply not free, and it cannot leave while blocked — releasing it is a
/// movement of its own. Blocking or releasing moves quantity between the two, it never adds
/// or removes physical stock.
/// </summary>
public enum BlockState
{
    Free = 1,
    Blocked = 2,
}

/// <summary>
/// One physical balance bucket. §5 names FOUR separate facts — the TRANSACTION company, the
/// OWNER of the goods, the CUSTODIAN who keeps them and the PHYSICAL location — and none of
/// them is folded into another, so stock of one owner can never satisfy a movement for
/// another. The product is not an input: it is the label's own product, so a position can
/// never claim a label is a different product. Cross-module references are identities only.
/// </summary>
public sealed record StockPosition
{
    public StockPosition(Guid companyId, StockOwner owner, StockCustodian custodian, Guid locationId,
        MaterialLabel label, BlockState block)
    {
        ArgumentNullException.ThrowIfNull(label);
        CompanyId = StockIds.Require(companyId, "İşlem şirketi");
        if (label.CompanyId != CompanyId)
        {
            throw new InventoryRuleException(InventoryRuleCodes.LabelCompanyMismatch,
                "Bir etiket yalnız kendi şirketinin pozisyonunda bulunur.");
        }

        Owner = owner.Id == Guid.Empty
            ? throw new InventoryRuleException(InventoryRuleCodes.MissingReference, "Mal sahibi zorunludur.")
            : owner;
        Custodian = custodian.Id == Guid.Empty
            ? throw new InventoryRuleException(InventoryRuleCodes.MissingReference, "Gözeten taraf zorunludur.")
            : custodian;
        LocationId = StockIds.Require(locationId, "Fiziksel konum");
        ProductId = label.ProductId;
        LabelId = label.Id;
        Block = Enum.IsDefined(block)
            ? block
            : throw new InventoryRuleException(InventoryRuleCodes.MissingReference, "Blokaj durumu tanımsız.");
    }

    private StockPosition(StockPosition source, BlockState block)
    {
        CompanyId = source.CompanyId;
        Owner = source.Owner;
        Custodian = source.Custodian;
        LocationId = source.LocationId;
        ProductId = source.ProductId;
        LabelId = source.LabelId;
        Block = block;
    }

    private StockPosition(StockPosition source, StockCustodian custodian, Guid locationId)
    {
        CompanyId = source.CompanyId;
        Owner = source.Owner;
        Custodian = custodian.Id == Guid.Empty
            ? throw new InventoryRuleException(InventoryRuleCodes.MissingReference, "Gözeten taraf zorunludur.")
            : custodian;
        LocationId = StockIds.Require(locationId, "Fiziksel konum");
        ProductId = source.ProductId;
        LabelId = source.LabelId;
        Block = source.Block;
    }

    /// <summary>The company the transaction is recorded under (Organization identity).</summary>
    public Guid CompanyId { get; }

    public StockOwner Owner { get; }

    public StockCustodian Custodian { get; }

    /// <summary>The physical location (Organization identity).</summary>
    public Guid LocationId { get; }

    /// <summary>The label's product; never given separately, so it cannot disagree with the label.
    /// The label is also of this position's company (<see cref="InventoryRuleCodes.LabelCompanyMismatch"/>).</summary>
    public Guid ProductId { get; }

    /// <summary>The physical identity of the material (<see cref="MaterialLabel"/>).</summary>
    public Guid LabelId { get; }

    public BlockState Block { get; }

    public StockPosition WithBlock(BlockState block) =>
        Enum.IsDefined(block)
            ? new(this, block)
            : throw new InventoryRuleException(InventoryRuleCodes.MissingReference, "Blokaj durumu tanımsız.");

    /// <summary>
    /// The same physical piece after a same-company transfer. Ownership, product, label and block state are preserved;
    /// only its custodian and physical location change.
    /// </summary>
    public StockPosition Relocate(StockCustodian custodian, Guid locationId) => new(this, custodian, locationId);

    /// <summary>
    /// The same physical place of the same piece, whatever its block state: company, owner,
    /// custodian, location, product and label. A label may have at most one of these at a time.
    /// </summary>
    internal StockPosition PhysicalPlace => Block == BlockState.Free ? this : WithBlock(BlockState.Free);
}

internal static class StockIds
{
    public static Guid Require(Guid id, string what) =>
        id == Guid.Empty
            ? throw new InventoryRuleException(InventoryRuleCodes.MissingReference, $"{what} kimliği zorunludur.")
            : id;

    public static StockPartyKind RequireKind(StockPartyKind kind, string what) =>
        Enum.IsDefined(kind)
            ? kind
            : throw new InventoryRuleException(InventoryRuleCodes.MissingReference, $"{what} türü tanımsız.");
}
