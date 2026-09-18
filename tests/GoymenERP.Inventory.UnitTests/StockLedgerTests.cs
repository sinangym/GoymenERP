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
        ledger.Apply(Issue(Position(), 5m));

        Assert.Equal(65m, ledger.QuantityAt(Position()).Value);
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

    [Fact]
    public void A_label_that_holds_stock_cannot_be_received_again()
    {
        // ORG #281 P3-1: one piece arrives once. A second receipt of a stock-bearing label — even
        // to the same place, even partly blocked — is a double count and is refused whole.
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));

        Assert.Equal(InventoryRuleCodes.LabelAlreadyInStock,
            Assert.Throws<InventoryRuleException>(() => ledger.Apply(Receipt(Position(), 1m))).Code);
        ledger.Apply(Movement(StockMovementKind.Block, Out(Position(), 100m), In(Position(block: BlockState.Blocked), 100m)));
        Assert.Equal(InventoryRuleCodes.LabelAlreadyInStock,
            Assert.Throws<InventoryRuleException>(() => ledger.Apply(Receipt(Position(), 1m))).Code);
        Assert.Equal(100m, ledger.PhysicalTotal(CompanyA, OwnedByA, BandProduct).Value);
    }

    [Fact]
    public void A_label_whose_stock_has_all_left_may_be_received_again()
    {
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));
        ledger.Apply(Issue(Position(), 100m));

        ledger.Apply(Receipt(Position(), 40m));

        Assert.Equal(40m, ledger.QuantityAt(Position()).Value);
    }

    [Fact]
    public void A_second_label_in_the_same_receipt_is_checked_on_its_own()
    {
        // The rule is per label: a new label beside a stock-bearing one is still refused whole.
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));
        var both = Movement(StockMovementKind.Receipt, In(Position(label: Label2), 5m), In(Position(), 5m));

        Assert.Equal(InventoryRuleCodes.LabelAlreadyInStock,
            Assert.Throws<InventoryRuleException>(() => ledger.Apply(both)).Code);
        Assert.Equal(Kilograms.Zero, ledger.QuantityAt(Position(label: Label2)));
    }

    [Fact]
    public void A_ledger_seeded_from_stored_balances_applies_the_next_movement_against_them()
    {
        var stored = StockLedger.FromStoredBalances([
            new StockBalance(Position(), Kilograms.Of(70m)),
            new StockBalance(Position(block: BlockState.Blocked), Kilograms.Of(30m)),
        ]);

        Assert.Empty(stored.AppliedMovements);
        Assert.Equal(100m, stored.PhysicalTotal(CompanyA, OwnedByA, BandProduct).Value);
        Assert.Equal(InventoryRuleCodes.NegativePhysicalStock,
            Assert.Throws<InventoryRuleException>(() => stored.Apply(Issue(Position(), 70.001m))).Code);
        Assert.Equal(InventoryRuleCodes.LabelAlreadyInStock,
            Assert.Throws<InventoryRuleException>(() => stored.Apply(Receipt(Position(), 1m))).Code);
        stored.Apply(Issue(Position(), 70m));
        Assert.Equal(30m, stored.PhysicalTotal(CompanyA, OwnedByA, BandProduct).Value);
    }

    [Theory]
    [InlineData("duplicate-bucket")]
    [InlineData("label-in-two-places")]
    public void Stored_balances_that_are_not_a_valid_state_are_refused(string fault)
    {
        StockBalance[] balances = fault == "duplicate-bucket"
            ? [new(Position(), Kilograms.Of(1m)), new(Position(), Kilograms.Of(2m))]
            : [new(Position(), Kilograms.Of(1m)), new(Position(location: Warehouse2), Kilograms.Of(2m))];

        var error = Assert.Throws<InventoryRuleException>(() => StockLedger.FromStoredBalances(balances));
        Assert.Equal(fault == "duplicate-bucket" ? InventoryRuleCodes.InvalidMovement : InventoryRuleCodes.LabelInTwoPositions,
            error.Code);
    }

    [Fact]
    public void A_source_answered_whole_takes_no_line_movement_of_that_kind_and_the_other_way_round()
    {
        // #293 ORG P3-1: whole and line by line for one source and kind would count the same
        // quantity twice. The first movement fixes the grain; another kind is its own answer.
        var sourceId = Guid.NewGuid();
        var whole = new StockSourceReference("purchase.receipt", sourceId);
        var line = new StockSourceReference("PURCHASE.receipt", sourceId, Guid.NewGuid());

        var wholeFirst = new StockLedger();
        wholeFirst.Apply(Sourced(StockMovementKind.Receipt, whole, In(Position(), 10m)));
        Assert.Equal(InventoryRuleCodes.SourceGrainMismatch, Assert.Throws<InventoryRuleException>(
            () => wholeFirst.Apply(Sourced(StockMovementKind.Receipt, line, In(Position(label: Label2), 10m)))).Code);
        wholeFirst.Apply(Sourced(StockMovementKind.Issue, line, Out(Position(), 4m)));

        var linesFirst = new StockLedger();
        linesFirst.Apply(Sourced(StockMovementKind.Receipt, line, In(Position(), 10m)));
        linesFirst.Apply(Sourced(StockMovementKind.Receipt,
            new StockSourceReference("purchase.receipt", sourceId, Guid.NewGuid()), In(Position(label: Label2), 5m)));
        Assert.Equal(InventoryRuleCodes.SourceGrainMismatch, Assert.Throws<InventoryRuleException>(
            () => linesFirst.Apply(Sourced(StockMovementKind.Receipt, whole, In(Position(label: Label2), 1m)))).Code);

        Assert.Equal(6m, wholeFirst.QuantityAt(Position()).Value);
        Assert.Equal(15m, linesFirst.PhysicalTotal(CompanyA, OwnedByA, BandProduct).Value);
    }

    [Fact]
    public void A_source_or_source_line_causes_one_movement_per_kind()
    {
        var sourceId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var ledger = new StockLedger();
        ledger.Apply(Sourced(StockMovementKind.Receipt, new StockSourceReference("purchase.receipt", sourceId), In(Position(), 10m)));
        ledger.Apply(Sourced(StockMovementKind.Issue, new StockSourceReference("sales.shipment", sourceId, lineId), Out(Position(), 1m)));

        // The kind token compares ignoring case, as the database's default collation does.
        Assert.Equal(InventoryRuleCodes.DuplicateSource, Assert.Throws<InventoryRuleException>(
            () => ledger.Apply(Sourced(StockMovementKind.Receipt, new StockSourceReference("Purchase.Receipt", sourceId),
                In(Position(label: Label2), 1m)))).Code);
        Assert.Equal(InventoryRuleCodes.DuplicateSource, Assert.Throws<InventoryRuleException>(
            () => ledger.Apply(Sourced(StockMovementKind.Issue, new StockSourceReference("sales.shipment", sourceId, lineId),
                Out(Position(), 1m)))).Code);
        Assert.Equal(9m, ledger.QuantityAt(Position()).Value);
    }

    private static StockMovement Sourced(StockMovementKind kind, StockSourceReference source, params StockMovementLine[] lines) =>
        new(Guid.NewGuid(), CompanyA, kind, At, source, lines);
}
