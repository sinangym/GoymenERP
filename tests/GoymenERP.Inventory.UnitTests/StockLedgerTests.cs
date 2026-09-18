using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

public sealed class StockLedgerTests
{
    [Fact]
    public void A_receipt_then_a_partial_issue_leaves_the_exact_remainder()
    {
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 10000m));
        ledger.Apply(Issue(Position(), 3800.125m));

        Assert.Equal(6199.875m, ledger.QuantityAt(Position()).Value);
        Assert.Equal(6199.875m, ledger.PhysicalTotal(CompanyA, BandProduct).Value);
    }

    [Fact]
    public void Physical_stock_never_goes_below_zero()
    {
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));

        var error = Assert.Throws<InventoryRuleException>(() => ledger.Apply(Issue(Position(), 100.001m)));
        Assert.Equal(InventoryRuleCodes.NegativePhysicalStock, error.Code);
        Assert.Equal(100m, ledger.QuantityAt(Position()).Value);
        // Exactly to zero is fine.
        ledger.Apply(Issue(Position(), 100m));
        Assert.Equal(0m, ledger.QuantityAt(Position()).Value);
    }

    [Fact]
    public void A_refused_movement_changes_nothing_at_all()
    {
        // Line one could apply on its own; line two cannot. Neither is applied.
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));
        ledger.Apply(Receipt(Position(label: Label2), 10m));
        var mixed = Movement(StockMovementKind.Issue, Out(Position(), 60m), Out(Position(label: Label2), 20m));

        Assert.Throws<InventoryRuleException>(() => ledger.Apply(mixed));

        Assert.Equal(100m, ledger.QuantityAt(Position()).Value);
        Assert.Equal(10m, ledger.QuantityAt(Position(label: Label2)).Value);
        Assert.DoesNotContain(mixed, ledger.AppliedMovements);
    }

    [Fact]
    public void Two_lines_on_the_same_bucket_are_checked_together()
    {
        // 60 + 60 out of 100 must fail even though each line alone would fit.
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));
        var doubled = Movement(StockMovementKind.Issue, Out(Position(), 60m), Out(Position(), 60m));

        Assert.Equal(InventoryRuleCodes.NegativePhysicalStock,
            Assert.Throws<InventoryRuleException>(() => ledger.Apply(doubled)).Code);
        Assert.Equal(100m, ledger.QuantityAt(Position()).Value);
    }

    [Fact]
    public void The_same_movement_is_applied_once()
    {
        var ledger = new StockLedger();
        var receipt = Receipt(Position(), 100m);
        ledger.Apply(receipt);

        Assert.Equal(InventoryRuleCodes.DuplicateMovement,
            Assert.Throws<InventoryRuleException>(() => ledger.Apply(receipt)).Code);
        Assert.Equal(100m, ledger.QuantityAt(Position()).Value);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("company")]
    [InlineData("location")]
    public void Company_owner_and_location_are_separate_facts(string dimension)
    {
        // Stock of one owner/company/location never satisfies a movement for another (§5).
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));
        var other = dimension switch
        {
            "owner" => Position(owner: OwnedByCustomer),
            "company" => Position(company: CompanyB, owner: new StockOwner(StockOwnerKind.Company, CompanyB)),
            _ => Position(location: Warehouse2),
        };

        Assert.Equal(InventoryRuleCodes.NegativePhysicalStock,
            Assert.Throws<InventoryRuleException>(() => ledger.Apply(Issue(other, 1m))).Code);
        Assert.Equal(Kilograms.Zero, ledger.QuantityAt(other));
    }

    [Fact]
    public void Customer_owned_goods_held_by_our_company_are_counted_apart()
    {
        // Our company A records both, in the same warehouse; ownership stays visible.
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));
        ledger.Apply(Receipt(Position(owner: OwnedByCustomer, label: Label2), 40m));

        Assert.Equal(140m, ledger.PhysicalTotal(CompanyA, BandProduct).Value);
        Assert.Equal(100m, ledger.Balances.Where(balance => balance.Position.Owner == OwnedByA).Sum(balance => balance.Quantity.Value));
        Assert.Equal(40m, ledger.Balances.Where(balance => balance.Position.Owner == OwnedByCustomer).Sum(balance => balance.Quantity.Value));
    }

    [Fact]
    public void Blocking_moves_stock_out_of_free_without_changing_the_physical_total()
    {
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));
        var blocked = Position(block: BlockState.Blocked);
        ledger.Apply(Movement(StockMovementKind.Block, Out(Position(), 30m), In(blocked, 30m)));

        Assert.Equal(100m, ledger.PhysicalTotal(CompanyA, BandProduct).Value);
        Assert.Equal(70m, ledger.FreeTotal(CompanyA, BandProduct).Value);
        Assert.Equal(30m, ledger.QuantityAt(blocked).Value);
        // Blocked stock cannot leave as free stock.
        Assert.Equal(InventoryRuleCodes.NegativePhysicalStock,
            Assert.Throws<InventoryRuleException>(() => ledger.Apply(Issue(Position(), 70.001m))).Code);
        // Releasing puts it back; the physical total never moved.
        ledger.Apply(Movement(StockMovementKind.Release, Out(blocked, 30m), In(Position(), 30m)));
        Assert.Equal(100m, ledger.FreeTotal(CompanyA, BandProduct).Value);
        Assert.Equal(100m, ledger.PhysicalTotal(CompanyA, BandProduct).Value);
    }

    [Fact]
    public void Balances_reconcile_with_the_movements_that_made_them()
    {
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 10000m));
        ledger.Apply(Receipt(Position(owner: OwnedByCustomer, label: Label2), 250.5m));
        ledger.Apply(Issue(Position(), 3800m));
        ledger.Apply(Movement(StockMovementKind.Block, Out(Position(), 200m), In(Position(block: BlockState.Blocked), 200m)));

        Assert.Empty(StockLedger.Reconcile(ledger.Balances, ledger.AppliedMovements));
    }

    [Fact]
    public void A_stored_balance_written_outside_its_movements_is_caught()
    {
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));
        ledger.Apply(Issue(Position(), 40m));
        // A store that "fixed" the number to 61 without a movement.
        var tampered = new[] { new StockBalance(Position(), Kilograms.Of(61m)) };

        var difference = Assert.Single(StockLedger.Reconcile(tampered, ledger.AppliedMovements));
        Assert.Equal((Position(), 61m, 60m), (difference.Position, difference.Balance, difference.FromMovements));
        // A movement with no stored balance at all is caught too.
        Assert.Single(StockLedger.Reconcile([], ledger.AppliedMovements));
    }

    [Fact]
    public void Ledger_offers_no_way_to_set_a_quantity_directly()
    {
        // Balances only change by applying a movement.
        var writable = typeof(StockLedger).GetMethods()
            .Where(method => method.DeclaringType == typeof(StockLedger) && !method.IsSpecialName && !method.IsStatic)
            .Select(method => method.Name)
            .Where(name => name != nameof(StockLedger.QuantityAt)
                && name != nameof(StockLedger.PhysicalTotal)
                && name != nameof(StockLedger.FreeTotal))
            .ToArray();
        Assert.Equal([nameof(StockLedger.Apply)], writable);
    }
}
