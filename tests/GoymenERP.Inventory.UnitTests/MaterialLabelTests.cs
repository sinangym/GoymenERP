using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Stock;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

public sealed class MaterialLabelTests
{
    [Fact]
    public void A_reprint_is_the_same_physical_identity_with_an_audited_print()
    {
        var label = new MaterialLabel(Label1, CompanyA, CodeOf(Label1), BandProduct, Lot1);
        var first = label.Print(At, "synthetic-operator");
        var second = label.Reprint(At.AddMinutes(5), "synthetic-operator", "Etiket yırtıldı");

        Assert.Equal(Label1, label.Id);
        Assert.Equal([1, 2], label.Prints.Select(print => print.Sequence));
        Assert.Null(first.Reason);
        Assert.Equal("Etiket yırtıldı", second.Reason);
    }

    [Fact]
    public void A_reprint_creates_no_stock()
    {
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));
        var label = new MaterialLabel(Label1, CompanyA, CodeOf(Label1), BandProduct, Lot1);
        label.Print(At, "synthetic-operator");
        label.Reprint(At.AddMinutes(1), "synthetic-operator", "Okunmuyor");
        label.Reprint(At.AddMinutes(2), "synthetic-operator", "Okunmuyor");

        // Printing is not a movement: the ledger never heard of it.
        Assert.Single(ledger.AppliedMovements);
        Assert.Equal(100m, ledger.PhysicalTotal(CompanyA, OwnedByA, BandProduct).Value);
    }

    [Theory]
    [InlineData("reprint-before-print")]
    [InlineData("second-first-print")]
    [InlineData("reprint-without-reason")]
    [InlineData("print-without-actor")]
    public void Print_rules_are_kept(string fault)
    {
        var label = new MaterialLabel(Label1, CompanyA, CodeOf(Label1), BandProduct, Lot1);
        if (fault != "reprint-before-print")
        {
            label.Print(At, "synthetic-operator");
        }

        Action act = fault switch
        {
            "reprint-before-print" => () => label.Reprint(At, "synthetic-operator", "x"),
            "second-first-print" => () => label.Print(At, "synthetic-operator"),
            "reprint-without-reason" => () => label.Reprint(At, "synthetic-operator", " "),
            _ => () => label.Reprint(At, " ", "x"),
        };
        Assert.Equal(InventoryRuleCodes.InvalidLabelOperation, Assert.Throws<InventoryRuleException>(act).Code);
    }

    [Theory]
    [InlineData("label")]
    [InlineData("company")]
    [InlineData("code")]
    [InlineData("product")]
    [InlineData("lot")]
    public void Lot_and_label_trace_is_mandatory(string missing)
    {
        Action build = missing switch
        {
            "label" => () => _ = new MaterialLabel(Guid.Empty, CompanyA, "SYN-X", BandProduct, Lot1),
            "company" => () => _ = new MaterialLabel(Label1, Guid.Empty, CodeOf(Label1), BandProduct, Lot1),
            "code" => () => _ = new MaterialLabel(Label1, CompanyA, " ", BandProduct, Lot1),
            "product" => () => _ = new MaterialLabel(Label1, CompanyA, CodeOf(Label1), Guid.Empty, Lot1),
            _ => () => _ = new MaterialLabel(Label1, CompanyA, CodeOf(Label1), BandProduct, Guid.Empty),
        };
        Assert.Equal(InventoryRuleCodes.MissingReference, Assert.Throws<InventoryRuleException>(build).Code);
    }

    [Fact]
    public void A_missing_required_document_does_not_hide_the_physical_arrival()
    {
        // Ö3: the piece arrives without its MTC. The arrival is recorded; the piece waits.
        var label = new MaterialLabel(Label1, CompanyA, CodeOf(Label1), BandProduct, Lot1, requiredDocuments: ["MTC"]);
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(label: label.Id), 5000m));

        Assert.Equal(5000m, ledger.PhysicalTotal(CompanyA, OwnedByA, BandProduct).Value);
        Assert.Equal(LabelDocumentStatus.PendingDocuments, label.DocumentStatus);
        Assert.False(label.IsReleasableForProductionOrShipment);

        label.AttachDocument("MTC", At.AddDays(2), "synthetic-quality");
        Assert.Equal(LabelDocumentStatus.Complete, label.DocumentStatus);
        Assert.True(label.IsReleasableForProductionOrShipment);
        // The stock never moved on either side of that.
        Assert.Single(ledger.AppliedMovements);
    }

    [Fact]
    public void Only_the_required_documents_decide_the_status()
    {
        var label = new MaterialLabel(Label1, CompanyA, CodeOf(Label1), BandProduct, Lot1, requiredDocuments: ["MTC", "CUSTOMER-SPEC"]);
        label.AttachDocument("MTC", At, "synthetic-quality");
        label.AttachDocument("PHOTO", At, "synthetic-quality");
        Assert.Equal(LabelDocumentStatus.PendingDocuments, label.DocumentStatus);

        label.AttachDocument("CUSTOMER-SPEC", At, "synthetic-quality");
        Assert.Equal(LabelDocumentStatus.Complete, label.DocumentStatus);
        // No requirement at all: nothing is waiting.
        Assert.Equal(LabelDocumentStatus.Complete, new MaterialLabel(Label2, CompanyA, CodeOf(Label2), BandProduct, Lot1).DocumentStatus);
    }

    [Fact]
    public void Attaching_a_document_is_recorded_with_who_and_when()
    {
        // Leaving "waiting for documents" is an event (Ö3): kind, time and actor are kept.
        var label = new MaterialLabel(Label1, CompanyA, CodeOf(Label1), BandProduct, Lot1, requiredDocuments: ["MTC"]);
        var attachment = label.AttachDocument(" MTC ", At.AddHours(3), " synthetic-quality ");

        Assert.Equal(("MTC", At.AddHours(3), "synthetic-quality"),
            (attachment.Kind, attachment.AttachedAtUtc, attachment.AttachedBy));
        Assert.Equal([attachment], label.DocumentAttachments);
        Assert.Equal(InventoryRuleCodes.InvalidLabelOperation, Assert.Throws<InventoryRuleException>(() =>
            label.AttachDocument("MTC", new DateTimeOffset(2026, 9, 18, 11, 0, 0, TimeSpan.FromHours(3)), "x")).Code);
        Assert.Equal(InventoryRuleCodes.InvalidLabelOperation,
            Assert.Throws<InventoryRuleException>(() => label.AttachDocument("MTC", At, " ")).Code);
    }

    [Fact]
    public void A_label_belongs_to_one_company_and_carries_its_printed_code()
    {
        // ADR-INV-001 A: the label is a record of the company holding its stock; the code is
        // unique within that company only, and its format is not chosen by this module.
        var label = new MaterialLabel(Label1, CompanyA, "  SYN CODE 1  ", BandProduct, Lot1);
        Assert.Equal((CompanyA, "SYN CODE 1"), (label.CompanyId, label.Code));

        var tooLong = new string('X', MaterialLabel.MaximumCodeLength + 1);
        Assert.Equal(InventoryRuleCodes.InvalidLabelOperation, Assert.Throws<InventoryRuleException>(
            () => new MaterialLabel(Label1, CompanyA, tooLong, BandProduct, Lot1)).Code);
        Assert.Equal(MaterialLabel.MaximumCodeLength,
            new MaterialLabel(Label1, CompanyA, tooLong[1..], BandProduct, Lot1).Code.Length);
    }

    [Fact]
    public void A_label_of_one_company_cannot_stand_in_another_company_s_position()
    {
        var label = Label(company: CompanyA);
        var error = Assert.Throws<InventoryRuleException>(() => new StockPosition(CompanyB,
            new StockOwner(StockPartyKind.Company, CompanyB), new StockCustodian(StockPartyKind.Company, CompanyB),
            Warehouse1, label, BlockState.Free));
        Assert.Equal(InventoryRuleCodes.LabelCompanyMismatch, error.Code);
    }

    [Fact]
    public void A_stored_label_is_restored_with_its_prints_and_attachments_in_order()
    {
        var original = new MaterialLabel(Label1, CompanyA, CodeOf(Label1), BandProduct, Lot1,
            requiredDocuments: ["MTC"], heatNumber: "SYN-HEAT-1");
        original.Print(At, "synthetic-operator");
        original.Reprint(At.AddMinutes(1), "synthetic-operator", "Okunmuyor");
        original.AttachDocument("MTC", At.AddHours(1), "synthetic-quality");

        var restored = MaterialLabel.Restore(original.Id, original.CompanyId, original.Code, original.ProductId,
            original.LotId, original.RequiredDocuments, original.HeatNumber, original.CoilNumber,
            original.Prints, original.DocumentAttachments);

        Assert.Equal((original.Id, original.CompanyId, original.Code, original.ProductId, original.LotId),
            (restored.Id, restored.CompanyId, restored.Code, restored.ProductId, restored.LotId));
        Assert.Equal(original.Prints, restored.Prints);
        Assert.Equal(original.DocumentAttachments, restored.DocumentAttachments);
        Assert.Equal(LabelDocumentStatus.Complete, restored.DocumentStatus);
        Assert.Equal(("SYN-HEAT-1", (string?)null), (restored.HeatNumber, restored.CoilNumber));
    }

    [Theory]
    [InlineData("sequence-gap")]
    [InlineData("first-with-reason")]
    [InlineData("reprint-without-reason")]
    [InlineData("local-time")]
    public void A_stored_label_that_breaks_a_print_rule_is_refused_not_repaired(string fault)
    {
        LabelPrint[] prints = fault switch
        {
            "sequence-gap" => [new(1, At, "op", null), new(3, At, "op", "x")],
            "first-with-reason" => [new(1, At, "op", "x")],
            "reprint-without-reason" => [new(1, At, "op", null), new(2, At, "op", null)],
            _ => [new(1, new DateTimeOffset(2026, 9, 18, 11, 0, 0, TimeSpan.FromHours(3)), "op", null)],
        };

        Assert.Equal(InventoryRuleCodes.InvalidLabelOperation, Assert.Throws<InventoryRuleException>(
            () => MaterialLabel.Restore(Label1, CompanyA, CodeOf(Label1), BandProduct, Lot1, [], null, null, prints, [])).Code);
    }

    [Fact]
    public void Heat_and_coil_are_never_generated()
    {
        var unknown = new MaterialLabel(Label1, CompanyA, CodeOf(Label1), BandProduct, Lot1);
        Assert.Null(unknown.HeatNumber);
        Assert.Null(unknown.CoilNumber);

        var known = new MaterialLabel(Label2, CompanyA, CodeOf(Label2), BandProduct, Lot1, heatNumber: " SYN-HEAT-1 ", coilNumber: "SYN-COIL-7");
        Assert.Equal(("SYN-HEAT-1", "SYN-COIL-7"), (known.HeatNumber, known.CoilNumber));
    }
}
