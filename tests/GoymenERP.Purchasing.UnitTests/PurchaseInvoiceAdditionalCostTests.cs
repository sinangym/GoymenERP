using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseInvoiceAdditionalCostTests
{
    [Fact]
    public void An_expense_line_matches_money_without_a_receipt_quantity_or_a_material_cost_binding()
    {
        var invoice = Invoice(6000m); var proof = Proof(invoice);
        invoice.Match(1, [], new(Guid.NewGuid(), 1, 0, 0), "SENTETIK-matcher", [proof]);
        Assert.Empty(invoice.Matches); Assert.Equal(6000m, Assert.Single(invoice.AdditionalCosts).NetAmount); Assert.Empty(invoice.Variances);
        invoice.Finalize(2, Guid.NewGuid(), "SENTETIK-finalizer", DateTimeOffset.UtcNow);
        var snapshot = PurchaseIncomingInvoiceSnapshot.Capture(invoice);
        var restored = JsonSerializer.Deserialize<PurchaseIncomingInvoiceSnapshot>(JsonSerializer.Serialize(snapshot, PurchasingJson.Options), PurchasingJson.Options)!.Restore();
        Assert.Equal(6000m, Assert.Single(restored.AdditionalCosts).NetAmount);
        Assert.Equal(invoice.Lines[0].Quantity, restored.Lines[0].Quantity);
        Assert.Equal("expense", Assert.Single(PurchaseIncomingInvoiceFinalizationV1.Event(restored).Lines).LinePurpose);
    }
    [Theory]
    [InlineData(5999)] [InlineData(6001)]
    public void An_official_expense_line_must_be_fully_assigned_even_when_price_tolerance_is_nonzero(decimal selected)
    {
        var invoice = Invoice(6000m);
        Assert.Equal(PurchaseIncomingInvoice.MatchRequired, Assert.Throws<PurchasingRuleException>(() =>
            invoice.Match(1, [], new(Guid.NewGuid(), 1, 100m, 0), "SENTETIK-matcher", [Proof(invoice) with { NetAmount = selected }])).Code);
        Assert.Equal(1, invoice.Version); Assert.Empty(invoice.AdditionalCosts);
    }
    [Fact]
    public void Expense_proofs_reject_foreign_company_supplier_currency_type_and_total_oversubscription()
    {
        var invoice = Invoice(6000m); var proof = Proof(invoice);
        foreach (var changed in new[] { proof with { CompanyId = Guid.NewGuid() }, proof with { SupplierPartyId = Guid.NewGuid() },
            proof with { CurrencyId = Guid.NewGuid() }, proof with { ExpenseTypeId = Guid.NewGuid() }, proof with { SourceNetAmount = 5000m } })
            Assert.Equal(PurchaseIncomingInvoice.SourceMismatch, Assert.Throws<PurchasingRuleException>(() =>
                invoice.Match(1, [], new(Guid.NewGuid(), 1, 0, 0), "SENTETIK-matcher", [changed])).Code);
        Assert.Equal(PurchaseIncomingInvoice.SourceMismatch, Assert.Throws<PurchasingRuleException>(() =>
            invoice.Match(1, [], new(Guid.NewGuid(), 1, 0, 0), "SENTETIK-matcher", [proof with { NetAmount = 5001m },
                proof with { InvoiceLineId = Guid.NewGuid(), NetAmount = 5001m }])).Code);
        Assert.Equal(1, invoice.Version);
    }
    [Fact]
    public void Legacy_match_requests_and_snapshots_omit_absent_expense_fields()
    {
        Assert.DoesNotContain("additionalCosts", JsonSerializer.Serialize(new PurchaseIncomingInvoiceMatchRequestV1("1", []), PurchasingJson.Options));
        Assert.DoesNotContain("additionalCosts", JsonSerializer.Serialize(PurchaseIncomingInvoiceSnapshot.Capture(Invoice(1m)), PurchasingJson.Options));
        var parsed = PurchaseIncomingInvoiceWireV1.ReadMatch("""{"expectedVersion":"1","receipts":[],"additionalCosts":[{"invoiceLineId":"11111111-1111-4111-8111-111111111111","purchaseOrderId":"22222222-2222-4222-8222-222222222222","costId":"33333333-3333-4333-8333-333333333333","netAmount":"6000"}]}""");
        Assert.Empty(parsed.Receipts); Assert.Equal("6000", Assert.Single(parsed.AdditionalCosts!).NetAmount);
    }
    private static PurchaseInvoiceAdditionalCostMatch Proof(PurchaseIncomingInvoice invoice) => new(invoice.CompanyId, invoice.Lines[0].Id,
        Guid.NewGuid(), Guid.NewGuid(), 3, invoice.SupplierPartyId, invoice.CompanyAccountId, invoice.CurrencyId, invoice.Amounts.Net, 10000m, 20,
        new(2026, 11, 17));
    private static PurchaseIncomingInvoice Invoice(decimal net)
    {
        var currency = Guid.NewGuid(); var vat = net * .2m;
        return PurchaseIncomingInvoice.Record(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), currency, currency, 2, 2, null,
            new(Guid.NewGuid(), 1, new string('a', 64), "SENTETIK-EXP-1", Guid.NewGuid(), new(2026, 11, 17)), new(net, vat, 0), null,
            [new(Guid.NewGuid(), 1, Guid.Empty, Guid.NewGuid(), 1, "Expense", "expense", 20, new(net, vat, 0), null)],
            [new(Guid.NewGuid(), Guid.NewGuid(), 1, new(2026, 12, 17), net + vat, null)], "SENTETIK-recorder", DateTimeOffset.UtcNow);
    }
}
