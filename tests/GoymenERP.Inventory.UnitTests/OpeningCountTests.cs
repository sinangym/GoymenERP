using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Opening;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// INV-OPEN-1 (T07 opening stock, decision A #66 5738113623 §2): the count document is the source of
/// the opening. Synthetic identities only; no real opening data.
/// </summary>
public sealed class OpeningCountTests
{
    private static readonly Guid CountId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid Line1 = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid Line2 = Guid.Parse("60000000-0000-0000-0000-000000000002");
    private static readonly DateOnly CountedOn = new(2026, 9, 19);

    [Fact]
    public void Every_counted_piece_becomes_one_free_receipt_sourced_by_its_count_line_and_the_book_equals_the_count()
    {
        var count = Count(Line(Line1, Label1, 1250.5m), Line(Line2, Label2, 980m));

        var receipts = count.Lines.Select(count.ReceiptOf).ToArray();
        Assert.All(receipts.Zip(count.Lines), pair =>
        {
            var (receipt, line) = pair;
            Assert.Equal(line.Id, receipt.Id);
            Assert.Equal(StockMovementKind.Receipt, receipt.Kind);
            Assert.Equal(new StockSourceReference(OpeningCount.SourceKind, CountId, line.Id), receipt.Source);
            Assert.Equal(new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero), receipt.OccurredAtUtc);
            var receiptLine = Assert.Single(receipt.Lines);
            Assert.Equal((StockDirection.In, line.Quantity), (receiptLine.Direction, receiptLine.Quantity));
            // The company owns and keeps it, at the counted location, free.
            Assert.Equal((OwnedByA, KeptByA, Warehouse1, BlockState.Free),
                (receiptLine.Position.Owner, receiptLine.Position.Custodian, receiptLine.Position.LocationId, receiptLine.Position.Block));
        });

        var ledger = new StockLedger();
        foreach (var receipt in receipts)
        {
            ledger.Apply(receipt);
        }

        Assert.Equal(1250.5m, ledger.QuantityAt(count.PositionOf(count.Lines[0])).Value);
        Assert.Equal(980m, ledger.QuantityAt(count.PositionOf(count.Lines[1])).Value);
        Assert.Empty(StockLedger.Reconcile(ledger.Balances, receipts));
    }

    [Fact]
    public void A_validated_counterparty_can_own_every_piece_while_the_context_company_keeps_it()
    {
        var partyId = Guid.Parse("71000000-0000-0000-0000-000000000001");
        var count = new OpeningCount(CountId, CompanyA, Warehouse1, CountedOn, "SYN-CUSTOMER-SHEET",
            new StockOwner(StockPartyKind.Counterparty, partyId), KeptByA, [Line(Line1, Label1, 10m)]);

        var position = count.PositionOf(Assert.Single(count.Lines));

        Assert.Equal((StockPartyKind.Counterparty, partyId), (position.Owner.Kind, position.Owner.Id));
        Assert.Equal(KeptByA, position.Custodian);
    }

    [Fact]
    public void Opening_never_accepts_a_foreign_company_owner_or_third_party_custodian()
    {
        Assert.Equal(InventoryRuleCodes.InvalidOpeningCount,
            Assert.Throws<InventoryRuleException>(() => new OpeningCount(CountId, CompanyA, Warehouse1, CountedOn, "SYN",
                new StockOwner(StockPartyKind.Company, CompanyB), KeptByA, [Line(Line1, Label1, 1m)])).Code);
        Assert.Equal(InventoryRuleCodes.InvalidOpeningCount,
            Assert.Throws<InventoryRuleException>(() => new OpeningCount(CountId, CompanyA, Warehouse1, CountedOn, "SYN",
                OwnedByA, new StockCustodian(StockPartyKind.Counterparty, Guid.NewGuid()), [Line(Line1, Label1, 1m)])).Code);
    }

    [Theory]
    [InlineData("no-lines")]
    [InlineData("line-twice")]
    [InlineData("label-twice")]
    [InlineData("code-twice")]
    [InlineData("label-of-another-lot")]
    [InlineData("label-of-another-product")]
    [InlineData("lot-with-two-facts")]
    [InlineData("blank-sheet")]
    [InlineData("long-sheet")]
    [InlineData("control-in-sheet")]
    [InlineData("no-date")]
    public void A_count_that_is_not_one_valid_count_document_is_refused(string fault)
    {
        var otherLot = new MaterialLot(Guid.Parse("30000000-0000-0000-0000-000000000009"), CompanyA, BandProduct);
        var error = Assert.Throws<InventoryRuleException>(() => fault switch
        {
            "no-lines" => Count(),
            "line-twice" => Count(Line(Line1, Label1, 1m), Line(Line1, Label2, 1m)),
            "label-twice" => Count(Line(Line1, Label1, 1m), Line(Line2, Label1, 1m)),
            "code-twice" => Count(Line(Line1, Label1, 1m),
                new OpeningCountLine(Line2, Lot(), new MaterialLabel(Label2, CompanyA, CodeOf(Label1), BandProduct, Lot1), Kilograms.Positive(1m))),
            "label-of-another-lot" => Count(new OpeningCountLine(Line1, otherLot, Label(Label1), Kilograms.Positive(1m))),
            "label-of-another-product" => Count(new OpeningCountLine(Line1, Lot(),
                new MaterialLabel(Label1, CompanyA, CodeOf(Label1), CoilProduct, Lot1), Kilograms.Positive(1m))),
            "lot-with-two-facts" => Count(Line(Line1, Label1, 1m),
                new OpeningCountLine(Line2, new MaterialLot(Lot1, CompanyA, BandProduct, heatNumber: "SYN-HEAT"), Label(Label2),
                    Kilograms.Positive(1m))),
            "blank-sheet" => Count(sheet: " ", lines: Line(Line1, Label1, 1m)),
            "long-sheet" => Count(sheet: new string('S', OpeningCount.MaximumSheetReferenceLength + 1), lines: Line(Line1, Label1, 1m)),
            "control-in-sheet" => Count(sheet: "SYNSHEET", lines: Line(Line1, Label1, 1m)),
            _ => new OpeningCount(CountId, CompanyA, Warehouse1, default, "SYN-SHEET", [Line(Line1, Label1, 1m)]),
        });

        Assert.Equal(InventoryRuleCodes.InvalidOpeningCount, error.Code);
    }

    [Fact]
    public void A_piece_or_lot_of_another_company_is_refused()
    {
        var foreignLot = new MaterialLot(Lot1, CompanyB, BandProduct);
        var foreignLabel = new MaterialLabel(Label1, CompanyB, CodeOf(Label1), BandProduct, Lot1);

        var error = Assert.Throws<InventoryRuleException>(() => Count(new OpeningCountLine(Line1, foreignLot, foreignLabel, Kilograms.Positive(1m))));

        Assert.Equal(InventoryRuleCodes.LabelCompanyMismatch, error.Code);
    }

    [Fact]
    public void A_counted_quantity_is_a_positive_kilogram_amount_with_at_most_three_decimals()
    {
        // Ö2: a fourth decimal is refused, never cut; zero is not a counted piece.
        Assert.Equal(InventoryRuleCodes.KilogramPrecisionExceeded,
            Assert.Throws<InventoryRuleException>(() => Kilograms.Positive(1.2345m)).Code);
        Assert.Equal(InventoryRuleCodes.QuantityNotPositive,
            Assert.Throws<InventoryRuleException>(() => Count(new OpeningCountLine(Line1, Lot(), Label(Label1), Kilograms.Zero))).Code);
    }

    private static MaterialLot Lot() => new(Lot1, CompanyA, BandProduct);

    private static OpeningCountLine Line(Guid id, Guid label, decimal kilograms) =>
        new(id, Lot(), Label(label), Kilograms.Positive(kilograms));

    private static OpeningCount Count(params OpeningCountLine[] lines) => Count("SYN-SHEET-1", lines);

    private static OpeningCount Count(string sheet, params OpeningCountLine[] lines) =>
        new(CountId, CompanyA, Warehouse1, CountedOn, sheet, lines);
}
