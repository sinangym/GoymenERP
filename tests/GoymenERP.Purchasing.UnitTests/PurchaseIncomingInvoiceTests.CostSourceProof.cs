using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchaseIncomingInvoiceTests
{
    [Fact]
    public void Finalized_goods_event_carries_frozen_receipt_totals_and_document_identity_without_changing_official_money()
    {
        var invoice = Invoice(); var match = Match(invoice) with { AcceptedQuantity = 12m };
        invoice.Match(1, [match], Policy(), "matcher"); invoice.Finalize(2, Guid.NewGuid(), "finalizer", Now);
        var value = PurchaseIncomingInvoiceFinalizationV1.Event(invoice);
        var source = Assert.Single(value.Lines).GoodsCostSource!;
        Assert.Equal((invoice.Lines[0].ProductId, invoice.Lines[0].UnitId, invoice.Lines[0].Quantity), (source.ProductId, source.UnitId, source.InvoiceQuantity));
        Assert.Equal(new PurchaseInvoiceReceiptCostShareV1(match.AcceptanceId, match.OrderId, match.OrderLineId, match.OrderVersion, match.Quantity, 12m), Assert.Single(source.Receipts));
        Assert.Equal(new PurchaseInvoiceOfficialDocumentV1(invoice.Source.DocumentId, invoice.Source.DocumentVersion, invoice.Source.Sha256, invoice.Source.Ettn), value.OfficialDocument);
        Assert.Equal((100m, 20m, 120m), (value.Net, value.Vat, value.Payable));
        var restored = JsonSerializer.Deserialize<PurchaseIncomingInvoiceSnapshot>(JsonSerializer.Serialize(PurchaseIncomingInvoiceSnapshot.Capture(invoice)))!.Restore();
        Assert.Equal(JsonSerializer.Serialize(value), JsonSerializer.Serialize(PurchaseIncomingInvoiceFinalizationV1.Event(restored)));
    }

    [Fact]
    public void Legacy_match_and_finalized_event_JSON_remain_readable_without_inventing_missing_receipt_totals()
    {
        var invoice = Invoice(); var match = Match(invoice);
        var matchJson = JsonSerializer.Serialize(match, PurchasingJson.Options);
        Assert.DoesNotContain("acceptedQuantity", matchJson, StringComparison.Ordinal);
        Assert.Null(JsonSerializer.Deserialize<PurchaseInvoiceReceiptMatch>(matchJson, PurchasingJson.Options)!.AcceptedQuantity);
        invoice.Match(1, [match], Policy(), "matcher"); invoice.Finalize(2, Guid.NewGuid(), "finalizer", Now);
        var payload = PurchaseIncomingInvoiceFinalizationV1.Event(invoice);
        Assert.Null(Assert.Single(payload.Lines).GoodsCostSource);
        var json = JsonNode.Parse(JsonSerializer.Serialize(payload, PurchasingJson.Options))!.AsObject();
        Assert.DoesNotContain("goodsCostSource", json.ToJsonString(), StringComparison.Ordinal);
        json.Remove("officialDocument"); // A V1 event persisted before this additive proof existed.
        var legacy = JsonSerializer.Deserialize<PurchaseInvoiceFinalizedPayloadV1>(json.ToJsonString(), PurchasingJson.Options)!;
        Assert.Null(legacy.OfficialDocument); Assert.Null(Assert.Single(legacy.Lines).GoodsCostSource);
        Assert.Equal(payload.Payable, legacy.Payable);
    }

    [Fact]
    public void Approved_invoice_quantity_variance_is_reported_as_its_actual_receipt_shares_instead_of_becoming_a_cost_distribution()
    {
        var invoice = Invoice(); var match = Match(invoice) with { Quantity = 8m, AcceptedQuantity = 12m };
        invoice.Match(1, [match], Policy(), "matcher");
        invoice.ApproveVariance(2, "manager", "SENTETIK approved quantity variance", Now);
        invoice.Finalize(3, Guid.NewGuid(), "finalizer", Now);
        var source = Assert.Single(PurchaseIncomingInvoiceFinalizationV1.Event(invoice).Lines).GoodsCostSource!;
        Assert.Equal(10m, source.InvoiceQuantity); Assert.Equal(8m, Assert.Single(source.Receipts).MatchedQuantity);
        Assert.Equal(12m, source.Receipts[0].AcceptedQuantity); // Consumer decides unavailable; Purchasing does not change the approved document.
        Assert.Equal(100m, invoice.Amounts.Net);
    }
}
