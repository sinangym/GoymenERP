using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// MOD-03 (SAT-D73…D76): the Settings tax master's answer feeds the ONE amount calculator (<see cref="SalesOrderTotalsCalculator"/>)
/// unchanged — the VAT percent as is, the withholding percent as its exact share (<see cref="TaxMasterCalculatorV1.WithholdingShare"/>).
/// Golden cases pin SAT-D75 (withholding from the ROUNDED line VAT) and SAT-D76 (the net unit price is never rounded). The order
/// line is not wired to the port in this slice (follow-up); every rate here is SENTETIK.
/// </summary>
public sealed class SalesTaxMasterGoldenTests
{
    private static readonly Guid Try = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Kg = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid L1 = Guid.Parse("10000000-0000-4000-8000-000000000011");

    [Theory]
    // 3 kg × 33.335 = 100.005 → 100.01 (unit price not rounded: 3 × 33.34 would be 100.02); VAT 20 % = 20.002 → 20.00;
    // withheld 50 % of the ROUNDED 20.00 = 10.00 (of the unrounded 20.002 it would be 10.001 → 10.00 — same here).
    [InlineData("3", "33.335", "20", "50", "100.01", "20.00", "10.00", "120.01", "110.01")]
    // 1 kg × 10.25 = 10.25; VAT 10 % = 1.025 → 1.03; withheld 50 % of the rounded 1.03 = 0.515 → 0.52
    // (of the unrounded 1.025 it would be 0.5125 → 0.51: SAT-D75 makes the difference visible).
    [InlineData("1", "10.25", "10", "50", "10.25", "1.03", "0.52", "11.28", "10.76")]
    // 7 kg × 2.5 = 17.50; VAT 1 % = 0.175 → 0.18; withheld 90 % of 0.18 = 0.162 → 0.16.
    [InlineData("7", "2.5", "1", "90", "17.50", "0.18", "0.16", "17.68", "17.52")]
    public void A_resolved_tax_master_answer_computes_the_golden_line_amounts(string quantity, string unitPrice, string vat,
        string withholding, string net, string vatAmount, string withheld, string gross, string payable)
    {
        var resolution = Resolved(vat, withholding);
        var rate = SalesLineTaxRate.Of(resolution.VatPercent!.Value,
            resolution.WithholdingState == TaxResolutionStatesV1.Resolved
                ? TaxMasterCalculatorV1.WithholdingShare(resolution.WithholdingPercent!.Value)
                : null);

        var line = Assert.Single(SalesOrderTotalsCalculator.Calculate(
            [new SalesTotalsLineInput(L1, 1, Kg, decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture),
                new CommercialDraftFields(unitPrice, Kg, null, null, null, null, null))],
            Try, 2, _ => rate).Lines);

        Assert.Equal((net, vatAmount, withheld, gross, payable), (Text(line.NetAmount), Text(line.VatAmount),
            Text(line.WithheldVatAmount), Text(line.GrossAmount), Text(line.PayableAmount)));
    }

    [Fact]
    public void An_unresolved_vat_is_never_turned_into_a_rate_so_no_tax_is_computed()
    {
        var data = new TaxMasterDataV1(new Dictionary<Guid, TaxCategoryDataV1>(), [], []);
        var parts = TaxMasterCalculatorV1.Resolve(data, Guid.NewGuid(), null, null, new DateOnly(2026, 11, 1));
        Assert.Equal(TaxResolutionStatesV1.Unresolved, parts.VatState);

        SalesLineTaxRate? rate = parts.VatState == TaxResolutionStatesV1.Resolved ? SalesLineTaxRate.Of(parts.VatPercent!.Value, null) : null;
        var line = Assert.Single(SalesOrderTotalsCalculator.Calculate(
            [new SalesTotalsLineInput(L1, 1, Kg, 1m, new CommercialDraftFields("10", Kg, null, null, null, null, null))],
            Try, 2, _ => rate).Lines);
        Assert.Equal((SalesTaxState.NoSource, (Money?)null, (Money?)null), (line.TaxState, line.VatAmount, line.WithheldVatAmount));
    }

    private static TaxLineResolutionPartsV1 Resolved(string vat, string withholding)
    {
        var vatClass = Guid.NewGuid();
        var withholdingClass = Guid.NewGuid();
        var buyer = Guid.NewGuid();
        var day = new DateOnly(2026, 11, 1);
        var data = new TaxMasterDataV1(
            new Dictionary<Guid, TaxCategoryDataV1>
            {
                [vatClass] = new(vatClass, TaxCategoryKindsV1.VatClass, "GENEL", "SENTETIK"),
                [withholdingClass] = new(withholdingClass, TaxCategoryKindsV1.WithholdingClass, "SINIF", "SENTETIK"),
                [buyer] = new(buyer, TaxCategoryKindsV1.BuyerCategory, "ALICI", "SENTETIK"),
            },
            [
                new TaxRateRecordDataV1(Guid.NewGuid(), TaxRateKindsV1.Vat, vatClass, null, 1, day, true, Parse(vat), null),
                new TaxRateRecordDataV1(Guid.NewGuid(), TaxRateKindsV1.Withholding, withholdingClass, buyer, 1, day, true,
                    Parse(withholding), "T1"),
            ],
            [new TaxCodeListVersionDataV1(TaxCodeListKindsV1.Withholding, 1, day, [new TaxCodeListEntryViewV1("T1", "SENTETIK")])]);
        var parts = TaxMasterCalculatorV1.Resolve(data, vatClass, withholdingClass, buyer, day);
        Assert.Equal((TaxResolutionStatesV1.Resolved, TaxResolutionStatesV1.Resolved), (parts.VatState, parts.WithholdingState));
        return parts;
    }

    private static decimal Parse(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    private static string? Text(Money? money) => money is { } value
        ? value.Amount.ToString("F" + value.Scale, System.Globalization.CultureInfo.InvariantCulture)
        : null;
}
