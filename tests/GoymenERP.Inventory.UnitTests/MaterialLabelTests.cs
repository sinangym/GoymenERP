using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

public sealed class MaterialLabelTests
{
    [Fact]
    public void A_reprint_is_the_same_physical_identity_with_an_audited_print()
    {
        var label = new MaterialLabel(Label1, BandProduct, Lot1);
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
        var label = new MaterialLabel(Label1, BandProduct, Lot1);
        label.Print(At, "synthetic-operator");
        label.Reprint(At.AddMinutes(1), "synthetic-operator", "Okunmuyor");
        label.Reprint(At.AddMinutes(2), "synthetic-operator", "Okunmuyor");

        // Printing is not a movement: the ledger never heard of it.
        Assert.Single(ledger.AppliedMovements);
        Assert.Equal(100m, ledger.PhysicalTotal(CompanyA, BandProduct).Value);
    }

    [Theory]
    [InlineData("reprint-before-print")]
    [InlineData("second-first-print")]
    [InlineData("reprint-without-reason")]
    [InlineData("print-without-actor")]
    public void Print_rules_are_kept(string fault)
    {
        var label = new MaterialLabel(Label1, BandProduct, Lot1);
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
    [InlineData("product")]
    [InlineData("lot")]
    public void Lot_and_label_trace_is_mandatory(string missing)
    {
        Action build = missing switch
        {
            "label" => () => _ = new MaterialLabel(Guid.Empty, BandProduct, Lot1),
            "product" => () => _ = new MaterialLabel(Label1, Guid.Empty, Lot1),
            _ => () => _ = new MaterialLabel(Label1, BandProduct, Guid.Empty),
        };
        Assert.Equal(InventoryRuleCodes.MissingReference, Assert.Throws<InventoryRuleException>(build).Code);
    }

    [Fact]
    public void A_missing_required_document_does_not_hide_the_physical_arrival()
    {
        // Ö3: the piece arrives without its MTC. The arrival is recorded; the piece waits.
        var label = new MaterialLabel(Label1, BandProduct, Lot1, requiredDocuments: ["MTC"]);
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(label: label.Id), 5000m));

        Assert.Equal(5000m, ledger.PhysicalTotal(CompanyA, BandProduct).Value);
        Assert.Equal(LabelDocumentStatus.PendingDocuments, label.DocumentStatus);
        Assert.False(label.IsReleasableForProductionOrShipment);

        label.AttachDocument("MTC");
        Assert.Equal(LabelDocumentStatus.Complete, label.DocumentStatus);
        Assert.True(label.IsReleasableForProductionOrShipment);
        // The stock never moved on either side of that.
        Assert.Single(ledger.AppliedMovements);
    }

    [Fact]
    public void Only_the_required_documents_decide_the_status()
    {
        var label = new MaterialLabel(Label1, BandProduct, Lot1, requiredDocuments: ["MTC", "CUSTOMER-SPEC"]);
        label.AttachDocument("MTC");
        label.AttachDocument("PHOTO");
        Assert.Equal(LabelDocumentStatus.PendingDocuments, label.DocumentStatus);

        label.AttachDocument("CUSTOMER-SPEC");
        Assert.Equal(LabelDocumentStatus.Complete, label.DocumentStatus);
        // No requirement at all: nothing is waiting.
        Assert.Equal(LabelDocumentStatus.Complete, new MaterialLabel(Label2, BandProduct, Lot1).DocumentStatus);
    }

    [Fact]
    public void Heat_and_coil_are_never_generated()
    {
        var unknown = new MaterialLabel(Label1, BandProduct, Lot1);
        Assert.Null(unknown.HeatNumber);
        Assert.Null(unknown.CoilNumber);

        var known = new MaterialLabel(Label2, BandProduct, Lot1, heatNumber: " SYN-HEAT-1 ", coilNumber: "SYN-COIL-7");
        Assert.Equal(("SYN-HEAT-1", "SYN-COIL-7"), (known.HeatNumber, known.CoilNumber));
    }
}
