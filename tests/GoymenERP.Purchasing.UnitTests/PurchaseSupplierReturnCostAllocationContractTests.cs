using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseSupplierReturnCostAllocationContractTests
{
    private static readonly Guid InvoiceSource = Guid.NewGuid();

    [Fact]
    public void Whole_sources_keep_exact_thirds_while_top_level_measurements_remain_canonical()
    {
        var allocation = new PurchaseSupplierReturnCostAllocationV1("source-v1", new(true, null, null),
            [new(InvoiceSource, true, null, null)]);
        PurchaseSupplierReturnCostAllocationContractV1.Validate(allocation, "1", "2");
        PurchaseSupplierReturnCostAllocationContractV1.ValidateAvailability(allocation, "1", "2", Preview());
    }

    [Theory]
    [InlineData("0.334", "0.666", "0.666", "1.334")]
    [InlineData("0.333", "0.667", "0.667", "1.333")]
    public void A_rounded_fraction_cannot_claim_more_than_either_exact_source(string pendingKg, string invoiceKg,
        string pendingUndo, string invoiceUndo)
    {
        var allocation = new PurchaseSupplierReturnCostAllocationV1("source-v1", new(false, pendingKg, pendingUndo),
            [new(InvoiceSource, false, invoiceKg, invoiceUndo)]);
        PurchaseSupplierReturnCostAllocationContractV1.Validate(allocation, "1", "2");
        var error = Assert.Throws<PurchasingServiceException>(() =>
            PurchaseSupplierReturnCostAllocationContractV1.ValidateAvailability(allocation, "1", "2", Preview()));
        Assert.Equal((409, PurchaseSupplierReturnCostAllocationContractV1.ExceedsSource), (error.Status, error.Code));
    }

    [Fact]
    public void Kg_and_purchase_units_are_independent_sums_and_neither_is_inferred()
    {
        var wrongUndo = new PurchaseSupplierReturnCostAllocationV1("v", new(false, "1", "1"), []);
        Assert.Throws<JsonException>(() => PurchaseSupplierReturnCostAllocationContractV1.Validate(wrongUndo, "1", "2"));
        Assert.Throws<JsonException>(() => PurchaseSupplierReturnCostAllocationContractV1.Validate(wrongUndo, "1", null));
    }

    [Fact]
    public void Whole_source_selection_refuses_scalar_overrides_and_duplicate_invoice_ids()
    {
        Assert.Throws<JsonException>(() => PurchaseSupplierReturnCostAllocationContractV1.Validate(
            new("v", new(true, "1", null), []), "1", "1"));
        Assert.Throws<JsonException>(() => PurchaseSupplierReturnCostAllocationContractV1.Validate(
            new("v", new(false, "0", "0"), [new(InvoiceSource, true, null, null), new(InvoiceSource, true, null, null)]), "1", "1"));
    }

    [Fact]
    public void Stale_source_version_cannot_be_silently_refreshed()
    {
        var error = Assert.Throws<PurchasingServiceException>(() => PurchaseSupplierReturnCostAllocationContractV1.ValidateAvailability(
            new("older", new(true, null, null), [new(InvoiceSource, true, null, null)]), "1", "2", Preview()));
        Assert.Equal(PurchaseSupplierReturnCostAllocationContractV1.Stale, error.Code);
    }

    [Fact]
    public void Preview_has_only_identifiers_display_labels_and_quantities()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(Preview(), PurchasingJson.Options));
        Assert.Equal(new[] { "companyId", "goodsAcceptanceId", "invoicedSources", "labelId", "pendingQuantityKg", "pendingUndoQuantity", "pendingWholePairAvailable", "sourceVersion" },
            document.RootElement.EnumerateObject().Select(x => x.Name).Order().ToArray());
        var invoice = document.RootElement.GetProperty("invoicedSources")[0];
        Assert.Equal(new[] { "availableQuantityKg", "availableUndoQuantity", "invoiceId", "invoiceLineId", "invoiceLineNo", "invoiceNumber", "sourceEventId", "wholePairAvailable" },
            invoice.EnumerateObject().Select(x => x.Name).Order().ToArray());
    }

    [Fact]
    public void Whole_remaining_kg_after_shipment_requires_independently_entered_purchase_quantity()
    {
        var source = new PurchaseSupplierReturnCostSourcesV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "v",
            new("20", "1"), new("100", "1"), [], PendingWholePairAvailable: false);
        var inferred = new PurchaseSupplierReturnCostAllocationV1("v", new(true, null, null), []);
        Assert.Throws<PurchasingServiceException>(() =>
            PurchaseSupplierReturnCostAllocationContractV1.ValidateAvailability(inferred, "20", "100", source));
        var explicitUnits = new PurchaseSupplierReturnCostAllocationV1("v", new(true, null, "19"), []);
        PurchaseSupplierReturnCostAllocationContractV1.Validate(explicitUnits, "20", "19");
        PurchaseSupplierReturnCostAllocationContractV1.ValidateAvailability(explicitUnits, "20", "19", source);
    }

    [Theory]
    [InlineData(true, "50")]
    [InlineData(false, "100")]
    public void A_source_cannot_leave_orphan_kg_or_purchase_units_when_the_return_exhausts_one_side(bool wholePair, string undo)
    {
        var source = new PurchaseSupplierReturnCostSourcesV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "v",
            new("20", "1"), new("100", "1"), [], PendingWholePairAvailable: wholePair);
        var selection = new PurchaseSupplierReturnCostAllocationV1("v", new(true, null, undo), []);
        PurchaseSupplierReturnCostAllocationContractV1.Validate(selection, "20", undo);
        Assert.Equal(PurchaseSupplierReturnCostAllocationContractV1.ExceedsSource,
            Assert.Throws<PurchasingServiceException>(() =>
                PurchaseSupplierReturnCostAllocationContractV1.ValidateAvailability(selection, "20", undo, source)).Code);
    }

    private static PurchaseSupplierReturnCostSourcesV1 Preview() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "source-v1",
        new("1", "3"), new("2", "3"), [new(InvoiceSource, Guid.NewGuid(), Guid.NewGuid(), "SENTETIK-FATURA", 1,
            new("2", "3"), new("4", "3"), WholePairAvailable: true)], PendingWholePairAvailable: true);
}
