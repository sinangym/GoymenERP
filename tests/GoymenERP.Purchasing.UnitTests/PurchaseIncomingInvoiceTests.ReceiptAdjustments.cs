using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchaseIncomingInvoiceTests
{
    [Fact]
    public void Finalized_invoice_freezes_actual_undo_purchase_units_and_preserves_them_through_restore()
    {
        var invoice = Invoice();
        var adjustment = new PurchaseInvoiceReceiptAdjustment(Guid.NewGuid(), 2m, true);
        var mutable = new[] { adjustment };
        invoice.Match(1, [Match(invoice) with { AcceptedQuantity = 12m, ReceiptAdjustments = mutable }], Policy(), "matcher");
        mutable[0] = adjustment with { UndoQuantity = 11m };
        invoice.Finalize(2, Guid.NewGuid(), "finalizer", Now);
        var payload = PurchaseIncomingInvoiceFinalizationV1.Event(invoice);
        var share = Assert.Single(Assert.Single(payload.Lines).GoodsCostSource!.Receipts);
        Assert.Equal((adjustment.AdjustmentId, 2m, true),
            (Assert.Single(share.ReceiptAdjustments!).AdjustmentId, share.ReceiptAdjustments![0].UndoQuantity, share.ReceiptAdjustments[0].IsSupplierReturn));
        Assert.Equal((100m, 20m, 120m), (payload.Net, payload.Vat, payload.Payable));
        var restored = JsonSerializer.Deserialize<PurchaseIncomingInvoiceSnapshot>(
            JsonSerializer.Serialize(PurchaseIncomingInvoiceSnapshot.Capture(invoice)))!.Restore();
        Assert.Equal(JsonSerializer.Serialize(payload), JsonSerializer.Serialize(PurchaseIncomingInvoiceFinalizationV1.Event(restored)));
    }

    [Fact]
    public void Legacy_invoice_receipt_proof_does_not_become_a_proven_empty_adjustment_list()
    {
        var invoice = Invoice();
        invoice.Match(1, [Match(invoice) with { AcceptedQuantity = 12m }], Policy(), "matcher");
        invoice.Finalize(2, Guid.NewGuid(), "finalizer", Now);
        var json = JsonSerializer.Serialize(PurchaseIncomingInvoiceFinalizationV1.Event(invoice), PurchasingJson.Options);
        Assert.DoesNotContain("receiptAdjustments", json, StringComparison.Ordinal);
        var restored = JsonSerializer.Deserialize<PurchaseInvoiceFinalizedPayloadV1>(json, PurchasingJson.Options)!;
        Assert.Null(Assert.Single(Assert.Single(restored.Lines).GoodsCostSource!.Receipts).ReceiptAdjustments);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(12)]
    public void Invalid_or_excessive_undo_evidence_cannot_finalize_a_remaining_receipt_share(decimal undo)
    {
        var invoice = Invoice();
        var source = Match(invoice) with { AcceptedQuantity = 12m,
            ReceiptAdjustments = [new(Guid.NewGuid(), undo, true)] };
        var failure = Assert.Throws<PurchasingRuleException>(() => invoice.Match(1, [source], Policy(), "matcher"));
        Assert.Equal(PurchaseIncomingInvoice.SourceMismatch, failure.Code);
        Assert.Equal(1, invoice.Version);
    }
}
