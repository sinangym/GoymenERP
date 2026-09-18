using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// Synthetic TEST identities only. None of these is a real company, customer, supplier,
/// location, product, lot or label; a fixture is test data, not master data (Ö1: the first
/// real product comes from its authorised card/label/document, never from here).
/// </summary>
internal static class InventoryFixture
{
    public static readonly Guid CompanyA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    public static readonly Guid CompanyB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    public static readonly Guid CustomerC = Guid.Parse("c0000000-0000-0000-0000-00000000000c");
    public static readonly Guid FasonF = Guid.Parse("f0000000-0000-0000-0000-00000000000f");
    public static readonly Guid Warehouse1 = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid Warehouse2 = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid BandProduct = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid CoilProduct = Guid.Parse("20000000-0000-0000-0000-000000000002");
    public static readonly Guid Lot1 = Guid.Parse("30000000-0000-0000-0000-000000000001");
    public static readonly Guid Label1 = Guid.Parse("40000000-0000-0000-0000-000000000001");
    public static readonly Guid Label2 = Guid.Parse("40000000-0000-0000-0000-000000000002");
    public static readonly DateTimeOffset At = new(2026, 9, 18, 8, 0, 0, TimeSpan.Zero);

    public static StockOwner OwnedByA => new(StockPartyKind.Company, CompanyA);

    public static StockOwner OwnedByCustomer => new(StockPartyKind.Counterparty, CustomerC);

    public static StockCustodian KeptByA => new(StockPartyKind.Company, CompanyA);

    public static StockCustodian KeptByFason => new(StockPartyKind.Counterparty, FasonF);

    /// <summary>
    /// A synthetic label of <paramref name="product"/> (the band product by default) held by
    /// <paramref name="company"/> (company A by default). Its code is derived from its id, so the
    /// same label always has the same code.
    /// </summary>
    public static MaterialLabel Label(Guid? id = null, Guid? product = null, Guid? company = null) =>
        new(id ?? Label1, company ?? CompanyA, CodeOf(id ?? Label1), product ?? BandProduct, Lot1);

    /// <summary>A synthetic printed code for a synthetic label id.</summary>
    public static string CodeOf(Guid labelId) => $"SYN-{labelId:N}";

    /// <summary>A position whose product is the label's own; override any one fact.</summary>
    public static StockPosition Position(Guid? company = null, StockOwner? owner = null, StockCustodian? custodian = null,
        Guid? location = null, Guid? label = null, BlockState block = BlockState.Free, Guid? product = null) =>
        new(company ?? CompanyA, owner ?? OwnedByA, custodian ?? KeptByA, location ?? Warehouse1,
            Label(label, product, company ?? CompanyA), block);

    public static StockMovementLine In(StockPosition position, decimal kilograms) =>
        new(position, StockDirection.In, Kilograms.Positive(kilograms));

    public static StockMovementLine Out(StockPosition position, decimal kilograms) =>
        new(position, StockDirection.Out, Kilograms.Positive(kilograms));

    public static StockMovement Movement(StockMovementKind kind, params StockMovementLine[] lines) =>
        Movement(kind, CompanyA, lines);

    public static StockMovement Movement(StockMovementKind kind, Guid company, params StockMovementLine[] lines) =>
        new(Guid.NewGuid(), company, kind, At, new StockSourceReference("test.source", Guid.NewGuid()), lines);

    public static StockMovement Receipt(StockPosition position, decimal kilograms) =>
        Movement(StockMovementKind.Receipt, position.CompanyId, In(position, kilograms));

    public static StockMovement Issue(StockPosition position, decimal kilograms) =>
        Movement(StockMovementKind.Issue, position.CompanyId, Out(position, kilograms));

    public static CostResult KnownCost(decimal amount) => CostResult.Known(amount, Guid.Parse("50000000-0000-0000-0000-000000000001"));
}
