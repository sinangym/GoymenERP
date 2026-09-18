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
        Assert.Equal(6199.875m, ledger.PhysicalTotal(CompanyA, OwnedByA, BandProduct).Value);
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
    [InlineData("custodian")]
    [InlineData("company")]
    [InlineData("location")]
    public void Company_owner_custodian_and_location_are_separate_facts(string dimension)
    {
        // Stock of one owner/custodian/company/location never satisfies a movement for another (§5).
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));
        var other = dimension switch
        {
            "owner" => Position(owner: OwnedByCustomer),
            "custodian" => Position(custodian: KeptByFason),
            "company" => Position(company: CompanyB, owner: new StockOwner(StockPartyKind.Company, CompanyB)),
            _ => Position(location: Warehouse2),
        };

        Assert.Equal(InventoryRuleCodes.NegativePhysicalStock,
            Assert.Throws<InventoryRuleException>(() => ledger.Apply(Issue(other, 1m))).Code);
        Assert.Equal(Kilograms.Zero, ledger.QuantityAt(other));
    }

    [Fact]
    public void Customer_owned_goods_held_by_our_company_never_count_as_our_stock()
    {
        // Our company A holds 10 kg of its own and 5 kg of a customer's, same product, same
        // warehouse. Our free stock is 10, not 15 (§5, §7.1).
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 10m));
        ledger.Apply(Receipt(Position(owner: OwnedByCustomer, label: Label2), 5m));

        Assert.Equal(10m, ledger.PhysicalTotal(CompanyA, OwnedByA, BandProduct).Value);
        Assert.Equal(10m, ledger.FreeTotal(CompanyA, OwnedByA, BandProduct).Value);
        Assert.Equal(5m, ledger.PhysicalTotal(CompanyA, OwnedByCustomer, BandProduct).Value);
        Assert.Equal(5m, ledger.FreeTotal(CompanyA, OwnedByCustomer, BandProduct).Value);
    }

    [Theory]
    [InlineData("location")]
    [InlineData("owner")]
    [InlineData("custodian")]
    [InlineData("company")]
    [InlineData("product")]
    public void A_label_holds_stock_in_one_physical_place_only(string elsewhere)
    {
        // One coil cannot be in two warehouses, belong to two owners, be kept by two parties,
        // sit under two companies or be two products at once. Moving it is a transfer (T13).
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));
        var second = elsewhere switch
        {
            "location" => Position(location: Warehouse2),
            "owner" => Position(owner: OwnedByCustomer),
            "custodian" => Position(custodian: KeptByFason),
            "company" => Position(company: CompanyB, owner: new StockOwner(StockPartyKind.Company, CompanyB)),
            _ => Position(product: CoilProduct),
        };

        Assert.Equal(InventoryRuleCodes.LabelInTwoPositions,
            Assert.Throws<InventoryRuleException>(() => ledger.Apply(Receipt(second, 50m))).Code);
        Assert.Equal(100m, ledger.QuantityAt(Position()).Value);
        Assert.Equal(Kilograms.Zero, ledger.QuantityAt(second));
    }

    [Fact]
    public void A_partly_blocked_label_is_still_one_place()
    {
        // Free and blocked buckets of the same label at the same place are one piece.
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));
        ledger.Apply(Movement(StockMovementKind.Block, Out(Position(), 30m), In(Position(block: BlockState.Blocked), 30m)));
        ledger.Apply(Receipt(Position(), 5m));

        Assert.Equal(75m, ledger.QuantityAt(Position()).Value);
        Assert.Equal(30m, ledger.QuantityAt(Position(block: BlockState.Blocked)).Value);
    }

    [Fact]
    public void Blocked_stock_cannot_leave_until_it_is_released()
    {
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));
        var blocked = Position(block: BlockState.Blocked);
        ledger.Apply(Movement(StockMovementKind.Block, Out(Position(), 100m), In(blocked, 100m)));

        Assert.Equal(InventoryRuleCodes.InvalidMovement,
            Assert.Throws<InventoryRuleException>(() => Issue(blocked, 30m)).Code);
        ledger.Apply(Movement(StockMovementKind.Release, Out(blocked, 30m), In(Position(), 30m)));
        ledger.Apply(Issue(Position(), 30m));
        Assert.Equal(70m, ledger.PhysicalTotal(CompanyA, OwnedByA, BandProduct).Value);
    }

    [Fact]
    public void Blocking_moves_stock_out_of_free_without_changing_the_physical_total()
    {
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));
        var blocked = Position(block: BlockState.Blocked);
        ledger.Apply(Movement(StockMovementKind.Block, Out(Position(), 30m), In(blocked, 30m)));

        Assert.Equal(100m, ledger.PhysicalTotal(CompanyA, OwnedByA, BandProduct).Value);
        Assert.Equal(70m, ledger.FreeTotal(CompanyA, OwnedByA, BandProduct).Value);
        Assert.Equal(30m, ledger.QuantityAt(blocked).Value);
        // Blocked stock cannot leave as free stock.
        Assert.Equal(InventoryRuleCodes.NegativePhysicalStock,
            Assert.Throws<InventoryRuleException>(() => ledger.Apply(Issue(Position(), 70.001m))).Code);
        // Releasing puts it back; the physical total never moved.
        ledger.Apply(Movement(StockMovementKind.Release, Out(blocked, 30m), In(Position(), 30m)));
        Assert.Equal(100m, ledger.FreeTotal(CompanyA, OwnedByA, BandProduct).Value);
        Assert.Equal(100m, ledger.PhysicalTotal(CompanyA, OwnedByA, BandProduct).Value);
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
    public void A_movement_read_twice_is_refused_not_counted_twice()
    {
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));
        var twice = ledger.AppliedMovements.Concat(ledger.AppliedMovements);

        Assert.Equal(InventoryRuleCodes.DuplicateMovement,
            Assert.Throws<InventoryRuleException>(() => StockLedger.Reconcile(ledger.Balances, twice)).Code);
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
