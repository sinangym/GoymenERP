using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// Synthetic TEST identities only. None of these is a real company, customer, location,
/// product, lot or label; a fixture is test data, not master data (Ö1: the first real
/// product comes from its authorised card/label/document, never from here).
/// </summary>
internal static class InventoryFixture
{
    public static readonly Guid CompanyA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    public static readonly Guid CompanyB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    public static readonly Guid CustomerC = Guid.Parse("c0000000-0000-0000-0000-00000000000c");
    public static readonly Guid Warehouse1 = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid Warehouse2 = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid BandProduct = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid Lot1 = Guid.Parse("30000000-0000-0000-0000-000000000001");
    public static readonly Guid Label1 = Guid.Parse("40000000-0000-0000-0000-000000000001");
    public static readonly Guid Label2 = Guid.Parse("40000000-0000-0000-0000-000000000002");
    public static readonly DateTimeOffset At = new(2026, 9, 18, 8, 0, 0, TimeSpan.Zero);

    public static StockOwner OwnedByA => new(StockOwnerKind.Company, CompanyA);

    public static StockOwner OwnedByCustomer => new(StockOwnerKind.Counterparty, CustomerC);

    public static StockPosition Position(Guid? company = null, StockOwner? owner = null, Guid? location = null,
        Guid? label = null, BlockState block = BlockState.Free) =>
        new(company ?? CompanyA, owner ?? OwnedByA, location ?? Warehouse1, BandProduct, label ?? Label1, block);

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
