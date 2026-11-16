using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// F2 (SATIS_RISK_SEVK_KAPISI_VE_FASON_FATURA_PLANI_v1 §F2): the subcontract service invoice draft's goldens — the billed quantity is
/// the delivery weighing's net kg whole (SAT-D309, LOJ-D9; scrap is never deducted), the amount is the invoice calculator's net kg ×
/// the approved kg price (SAT-D195), a missing price or tax source is refused (never a zero amount), and a stored row whose amounts
/// do not agree is refused on restore. Every value is SENTETIK.
/// </summary>
public sealed class SalesSubcontractServiceInvoiceTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Party = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Account = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Try = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Kg = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid MoneyProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid Product = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid Order = Guid.Parse("abababab-abab-4bab-8bab-abababababab");
    private static readonly Guid OrderLine = Guid.Parse("acacacac-acac-4cac-8cac-acacacacacac");
    private static readonly Guid Instruction = Guid.Parse("bcbcbcbc-bcbc-4cbc-8cbc-bcbcbcbcbcbc");
    private static readonly Guid Weighing = Guid.Parse("cdcdcdcd-cdcd-4dcd-8dcd-cdcdcdcdcdcd");
    private static readonly DateOnly Day = new(2026, 11, 15);
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    private static SalesLineTaxSnapshot Tax(string vatState = "Resolved", decimal? vat = 20m) =>
        new(Product, Day, "Normal", "Service", "Resolved", null, "Record", Guid.Parse("12121212-1212-4212-8212-121212121212"), 1, vatState,
            vat, "NotRequired", null, null, "NotApplied", null, "NotRequired", null, null, null, 0, "Normal", false);

    private static SalesSubcontractServiceInvoiceHeader Header() => new(Party, Account, Try, MoneyProfile, "3", 2, Day);

    private static SalesSubcontractServiceInvoiceSource Source(string netKg) => new(Order, Instruction, Weighing, 2, 4, netKg);

    private static SalesSubcontractServiceLineInput Line(string quantity, string? price, SalesLineTaxSnapshot? tax = null,
        Guid? priceUnit = null) =>
        new(Guid.Parse("dededede-dede-4ede-8ede-dededededede"), OrderLine, 1, Product, Kg,
            decimal.Parse(quantity, CultureInfo.InvariantCulture), quantity,
            new CommercialDraftFields(price, price is null ? null : priceUnit ?? Kg, null), tax ?? Tax());

    private static SalesSubcontractServiceInvoice Create(string netKg, params SalesSubcontractServiceLineInput[] lines) =>
        SalesSubcontractServiceInvoice.Create(Guid.Parse("efefefef-efef-4fef-8fef-efefefefefef"), Company, "SFH-2026-000001", Header(),
            Source(netKg), lines, decimal.Parse(netKg, CultureInfo.InvariantCulture), "SENTETIK-preparer", Now);

    private static string Text(Money money) => money.Amount.ToString("F2", CultureInfo.InvariantCulture);

    private static string[] Set(SalesInvoiceAmountSet set) =>
        [Text(set.Net), Text(set.Vat), Text(set.Withholding), Text(set.Gross), Text(set.Payable)];

    [Fact]
    public void The_service_fee_is_the_whole_delivery_net_kg_times_the_kg_price_and_nothing_is_deducted_for_scrap()
    {
        // 1234.560 kg × 2.50 = 3086.40; VAT 20 % = 617.28; no withholding; payable 3703.68 (SAT-D11). The net kg is billed as the
        // Logistics owner answered it — no scrap deduction, no rounding of the quantity.
        var invoice = Create("1234.560", Line("1234.560", "2.50"));

        Assert.Equal((SalesSubcontractServiceInvoiceState.Draft, 1L, "SFH-2026-000001", Company),
            (invoice.State, invoice.Version, invoice.DocumentNo, invoice.CompanyId));
        var line = Assert.Single(invoice.Lines);
        Assert.Equal((1, 1234.560m, "1234.560", 2.50m, 20m, (decimal?)null),
            (line.LineNo, line.Input.Quantity, line.Input.QuantityText, line.NetUnitPrice, line.VatPercent, line.WithholdingShare));
        Assert.Equal(["3086.40", "617.28", "0.00", "3703.68", "3703.68"], Set(line.Amounts));
        Assert.Equal(["3086.40", "617.28", "0.00", "3703.68", "3703.68"], Set(invoice.Totals));
        Assert.Equal((Order, Instruction, Weighing, 2, 4L, "1234.560"), (invoice.Source.SalesOrderId, invoice.Source.LoadingInstructionId,
            invoice.Source.WeighingId, invoice.Source.WeighingRevision, invoice.Source.InstructionVersion, invoice.Source.NetKgText));
        Assert.Equal(Try, invoice.Totals.Net.CurrencyId);
    }

    [Theory]
    [InlineData("1234.559")]
    [InlineData("1200")]
    [InlineData("1234.561")]
    public void A_billed_quantity_other_than_the_weighing_net_kg_is_refused_never_billed_partly(string billed)
    {
        Assert.Throws<ArgumentException>(() => SalesSubcontractServiceInvoice.Create(Guid.NewGuid(), Company, "SFH-2026-000001",
            Header(), Source("1234.560"), [Line(billed, "2.50")], 1234.560m, "SENTETIK-preparer", Now));
    }

    [Fact]
    public void An_unpriced_line_or_a_price_in_another_unit_or_a_tax_without_a_source_is_refused_never_a_zero_amount()
    {
        Assert.Equal(SalesRuleCodes.InvoiceLineUnpriced,
            Assert.Throws<SalesRuleException>(() => Create("10", Line("10", null))).Code);
        Assert.Equal(SalesRuleCodes.InvoiceLineUnpriced,
            Assert.Throws<SalesRuleException>(() => Create("10", Line("10", "2.50", priceUnit: Guid.NewGuid()))).Code);
        Assert.Equal(SalesRuleCodes.InvoiceTaxUnavailable,
            Assert.Throws<SalesRuleException>(() => Create("10", Line("10", "2.50", Tax(vatState: "Unavailable", vat: null)))).Code);
        Assert.Equal(SalesRuleCodes.InvoiceWithoutLines,
            Assert.Throws<SalesRuleException>(() => Create("10")).Code);
    }

    [Fact]
    public void A_source_without_a_weighing_revision_or_net_kg_text_is_refused()
    {
        Assert.Throws<ArgumentException>(() => SalesSubcontractServiceInvoice.Create(Guid.NewGuid(), Company, "SFH-2026-000001", Header(),
            Source("10") with { WeighingRevision = 0 }, [Line("10", "2.50")], 10m, "SENTETIK-preparer", Now));
        Assert.Throws<ArgumentException>(() => SalesSubcontractServiceInvoice.Create(Guid.NewGuid(), Company, "SFH-2026-000001", Header(),
            Source(" "), [Line("10", "2.50")], 10m, "SENTETIK-preparer", Now));
        Assert.ThrowsAny<ArgumentException>(() => SalesSubcontractServiceInvoice.Create(Guid.NewGuid(), Company, "SFH-2026-000001", Header(),
            Source("10") with { WeighingId = Guid.Empty }, [Line("10", "2.50")], 10m, "SENTETIK-preparer", Now));
    }

    [Fact]
    public void A_stored_invoice_is_restored_as_stored_and_a_line_whose_amounts_disagree_is_refused()
    {
        var created = Create("1234.560", Line("1234.560", "2.50"));
        var stored = created.Lines[0];

        var restored = SalesSubcontractServiceInvoice.Restore(created.Id, Company, created.DocumentNo, created.Header, created.Source,
            SalesSubcontractServiceInvoiceState.Draft, 1, created.CreatedBy, created.CreatedAtUtc,
            [SalesSubcontractServiceInvoice.StoredLine(1, stored.Input, stored.NetUnitPrice, stored.VatPercent, stored.WithholdingShare,
                stored.Amounts)]);
        Assert.Equal(Set(created.Totals), Set(restored.Totals));

        var corrupt = stored.Amounts with { Payable = Money.Of(1m, Try, 2) };
        Assert.Throws<InvalidOperationException>(() => SalesSubcontractServiceInvoice.Restore(created.Id, Company, created.DocumentNo,
            created.Header, created.Source, SalesSubcontractServiceInvoiceState.Draft, 1, created.CreatedBy, created.CreatedAtUtc,
            [SalesSubcontractServiceInvoice.StoredLine(1, stored.Input, stored.NetUnitPrice, stored.VatPercent, stored.WithholdingShare,
                corrupt)]));
        Assert.Throws<InvalidOperationException>(() => SalesSubcontractServiceInvoice.Restore(created.Id, Company, created.DocumentNo,
            created.Header, created.Source, (SalesSubcontractServiceInvoiceState)9, 1, created.CreatedBy, created.CreatedAtUtc,
            [stored]));
    }
}
