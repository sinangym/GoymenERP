using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseInvoiceSubcontractTaxMetadataTests
{
    [Fact]
    public void Finalization_carries_the_approved_service_tax_source_without_recalculating_the_official_document_amounts()
    {
        var company = Guid.NewGuid(); var party = Guid.NewGuid(); var account = Guid.NewGuid(); var currency = Guid.NewGuid();
        var type = Guid.NewGuid(); var unit = Guid.NewGuid(); var line = Guid.NewGuid(); var day = new DateOnly(2026, 11, 17);
        var amounts = new PurchaseInvoiceAmounts(100m, 20m, 10m);
        var invoice = PurchaseIncomingInvoice.Record(Guid.NewGuid(), company, party, account, currency, currency, 2, 2, null,
            new(Guid.NewGuid(), 1, new string('a', 64), "SENTETIK-FASON", Guid.NewGuid(), day), amounts, null,
            [new(line, 1, Guid.Empty, unit, 1m, "Service", "service", 20m, amounts, null, type)],
            [new(Guid.NewGuid(), Guid.NewGuid(), 1, day.AddDays(1), 110m, null)], "SENTETIK hazırlayan", DateTimeOffset.UtcNow);
        var tax = new PurchaseServiceTax(true, 20m, null, true, 50m, "601", 3);
        var accepted = new PurchaseInvoiceReceiptMatch(company, line, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5,
            party, account, currency, unit, 1m, 100m, null, day, type, tax);
        invoice.Match(1, [accepted], new(Guid.NewGuid(), 1, 0m, 0m), "SENTETIK eşleştiren");
        invoice.Finalize(2, Guid.NewGuid(), "SENTETIK kesinleştiren", DateTimeOffset.UtcNow);
        var payload = PurchaseIncomingInvoiceFinalizationV1.Event(invoice);
        var source = Assert.Single(Assert.Single(payload.Lines).SubcontractTaxSources!);
        Assert.Equal((accepted.OrderId, accepted.OrderVersion, accepted.AcceptanceId, tax), (source.OrderId, source.OrderVersion, source.AcceptanceId, source.Tax));
        Assert.Equal((100m, 20m, 10m, 110m), (payload.Net, payload.Vat, payload.Withholding, payload.Payable));
        var restored = PurchaseIncomingInvoiceSnapshot.Capture(invoice).Restore();
        Assert.Equal(tax, Assert.Single(restored.Matches).SubcontractTax);
    }
    [Fact]
    public void Absent_service_tax_metadata_is_omitted_for_legacy_goods_replay_and_event_shapes()
    {
        var match = new PurchaseInvoiceReceiptMatch(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m, 1m, null, new(2026, 11, 17));
        Assert.DoesNotContain("subcontractTax", JsonSerializer.Serialize(match, new JsonSerializerOptions(JsonSerializerDefaults.Web)), StringComparison.Ordinal);
        var line = new PurchaseInvoiceFinalizedLineV1(Guid.NewGuid(), 1, "Steel", "stock", 20m, 1m, .2m, 0m, null, null, null);
        Assert.DoesNotContain("subcontractTaxSources", JsonSerializer.Serialize(line, new JsonSerializerOptions(JsonSerializerDefaults.Web)), StringComparison.Ordinal);
    }
}
