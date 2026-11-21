using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using Xunit;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseServiceInvoiceContractsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Fact]
    public void Actual_service_identity_is_preserved_without_becoming_a_Product_or_a_material_cost_source()
    {
        var type = Guid.NewGuid(); var invoice = Invoice(type);
        Assert.Equal(Guid.Empty, invoice.Lines[0].ProductId); Assert.Equal(type, invoice.Lines[0].ServiceTypeId);
        var match = new PurchaseInvoiceReceiptMatch(invoice.CompanyId, invoice.Lines[0].Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            3, invoice.SupplierPartyId, invoice.CompanyAccountId, invoice.CurrencyId, invoice.Lines[0].UnitId, 4m, 8m, null,
            invoice.Source.InvoiceDate, type);
        invoice.Match(1, [match], new(Guid.NewGuid(), 1, 0, 0), "SENTETIK-matcher");
        invoice.Finalize(2, Guid.NewGuid(), "SENTETIK-finalizer", new(2026, 11, 17, 0, 0, 0, TimeSpan.Zero));
        var source = PurchaseIncomingInvoiceFinalizationV1.Event(invoice);
        Assert.Equal(type, source.Lines[0].ServiceTypeId);
        Assert.Equal("Service", source.Lines[0].ItemType); Assert.Equal("service", source.Lines[0].LinePurpose);
        Assert.Contains("\"serviceTypeId\"", JsonSerializer.Serialize(source, Json));
    }
    [Fact]
    public void Service_identity_does_not_relax_Product_identity_for_legacy_goods_or_accept_inconsistent_service_metadata()
    {
        var type = Guid.NewGuid(); var source = Invoice(type);
        Assert.Throws<ArgumentException>(() => Rebuild(source, source.Lines[0] with { ServiceTypeId = null }));
        Assert.Throws<ArgumentException>(() => Rebuild(source, source.Lines[0] with { ProductId = Guid.NewGuid() }));
        Assert.Throws<ArgumentException>(() => Rebuild(source, source.Lines[0] with { LinePurpose = "production-input" }));
        Assert.Throws<ArgumentException>(() => Rebuild(source, source.Lines[0] with { ItemType = "material" }));
        var legacy = source.Lines[0] with { ProductId = Guid.NewGuid(), ServiceTypeId = null, ItemType = "material", LinePurpose = "resale" };
        Assert.DoesNotContain("serviceTypeId", JsonSerializer.Serialize(Rebuild(source, legacy).Lines[0], Json));
    }
    private static PurchaseIncomingInvoice Invoice(Guid type)
    {
        var currency = Guid.NewGuid(); var day = new DateOnly(2026, 11, 17);
        return PurchaseIncomingInvoice.Record(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), currency, currency, 2, 2, null,
            new(Guid.NewGuid(), 1, new string('a', 64), "SENTETIK-HIZ-1", Guid.NewGuid(), day), new(8, 0, 0), null,
            [new(Guid.NewGuid(), 1, Guid.Empty, Guid.NewGuid(), 4, "Service", "service", 0, new(8, 0, 0), null, type)],
            [new(Guid.NewGuid(), Guid.NewGuid(), 1, day, 8, null)], "SENTETIK-recorder", new(2026, 11, 17, 0, 0, 0, TimeSpan.Zero));
    }
    private static PurchaseIncomingInvoice Rebuild(PurchaseIncomingInvoice source, PurchaseInvoiceLine line) =>
        PurchaseIncomingInvoice.Record(source.Id, source.CompanyId, source.SupplierPartyId, source.CompanyAccountId, source.CurrencyId,
            source.FunctionalCurrencyId, source.MoneyScale, source.FunctionalScale, source.OfficialRate, source.Source, source.Amounts,
            source.FunctionalAmounts, [line], source.Installments, source.RecordedBy, source.RecordedAtUtc);
}
