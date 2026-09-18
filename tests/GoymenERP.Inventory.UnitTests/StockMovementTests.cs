using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

public sealed class StockMovementTests
{
    [Fact]
    public void A_movement_cannot_be_changed_after_it_is_built()
    {
        var source = new List<StockMovementLine> { In(Position(), 100m) };
        var movement = new StockMovement(Guid.NewGuid(), CompanyA, StockMovementKind.Receipt, At,
            new StockSourceReference("test.source", Guid.NewGuid()), source);

        // Changing the caller's list afterwards does not reach the movement.
        source.Add(In(Position(label: Label2), 50m));
        Assert.Single(movement.Lines);
        // The exposed list is read-only.
        Assert.Throws<NotSupportedException>(() => ((IList<StockMovementLine>)movement.Lines).Add(In(Position(), 1m)));
        // And the type offers no setter for any of its facts.
        Assert.All(typeof(StockMovement).GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void An_unknown_cost_is_not_calculated_never_zero()
    {
        var movement = Receipt(Position(), 100m);

        Assert.Equal(CostStatus.NotCalculated, movement.Cost.Status);
        Assert.Null(movement.Cost.Amount);
        Assert.Null(movement.Cost.CurrencyId);
    }

    [Fact]
    public void A_cost_status_cannot_contradict_its_amount()
    {
        Assert.Null(CostResult.WaitingPolicy.Amount);
        var known = KnownCost(0m);
        // A KNOWN zero is a real, decided zero — different from "not calculated".
        Assert.Equal((CostStatus.Known, 0m), (known.Status, known.Amount!.Value));
        Assert.NotEqual(CostResult.NotCalculated, known);
        Assert.Equal(InventoryRuleCodes.InvalidCostResult,
            Assert.Throws<InventoryRuleException>(() => CostResult.Known(-1m, Guid.NewGuid())).Code);
        Assert.Equal(InventoryRuleCodes.InvalidCostResult,
            Assert.Throws<InventoryRuleException>(() => CostResult.Known(1m, Guid.Empty)).Code);
    }

    [Theory]
    [InlineData("receipt-with-out-line")]
    [InlineData("issue-with-in-line")]
    [InlineData("no-lines")]
    [InlineData("line-in-other-company")]
    [InlineData("local-time")]
    [InlineData("block-changes-location")]
    [InlineData("block-changes-quantity")]
    [InlineData("block-wrong-direction")]
    [InlineData("block-single-line")]
    [InlineData("block-two-out-lines")]
    public void An_inconsistent_movement_is_refused(string fault)
    {
        var free = Position();
        var blocked = free.WithBlock(BlockState.Blocked);
        Action build = fault switch
        {
            "receipt-with-out-line" => () => Movement(StockMovementKind.Receipt, In(free, 1m), Out(Position(label: Label2), 1m)),
            "issue-with-in-line" => () => Movement(StockMovementKind.Issue, In(free, 1m)),
            "no-lines" => () => Movement(StockMovementKind.Receipt),
            // The transaction company is the movement's; a line cannot slip into another one.
            "line-in-other-company" => () => Movement(StockMovementKind.Receipt, CompanyA, In(Position(company: CompanyB), 1m)),
            "local-time" => () => new StockMovement(Guid.NewGuid(), CompanyA, StockMovementKind.Receipt,
                new DateTimeOffset(2026, 9, 18, 11, 0, 0, TimeSpan.FromHours(3)),
                new StockSourceReference("test.source", Guid.NewGuid()), [In(free, 1m)]),
            "block-changes-location" => () => Movement(StockMovementKind.Block,
                Out(free, 5m), In(Position(location: Warehouse2, block: BlockState.Blocked), 5m)),
            "block-changes-quantity" => () => Movement(StockMovementKind.Block, Out(free, 5m), In(blocked, 4m)),
            "block-wrong-direction" => () => Movement(StockMovementKind.Block, Out(blocked, 5m), In(free, 5m)),
            "block-single-line" => () => Movement(StockMovementKind.Block, In(blocked, 5m)),
            _ => () => Movement(StockMovementKind.Block, Out(free, 5m), Out(blocked, 5m)),
        };

        Assert.Equal(InventoryRuleCodes.InvalidMovement, Assert.Throws<InventoryRuleException>(build).Code);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("owner")]
    [InlineData("location")]
    [InlineData("product")]
    [InlineData("label")]
    [InlineData("source")]
    public void Every_identity_is_required(string missing)
    {
        Action build = missing switch
        {
            "company" => () => Position(company: Guid.Empty),
            "owner" => () => new StockOwner(StockOwnerKind.Company, Guid.Empty),
            "location" => () => Position(location: Guid.Empty),
            "product" => () => new StockPosition(CompanyA, OwnedByA, Warehouse1, Guid.Empty, Label1, BlockState.Free),
            // Lot/label trace is the mandatory minimum (Ö3): no line without its physical piece.
            "label" => () => Position(label: Guid.Empty),
            _ => () => new StockSourceReference("test.source", Guid.Empty),
        };

        Assert.Equal(InventoryRuleCodes.MissingReference, Assert.Throws<InventoryRuleException>(build).Code);
    }
}
