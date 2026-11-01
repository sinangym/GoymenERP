using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// MOD-03 (SAT-D75/D76, SAT-D139): the Settings tax master's answer feeds the ONE amount calculator (<see cref="SalesOrderTotalsCalculator"/>)
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
    public void The_sat_d139_steel_default_feeds_the_calculator_as_20_percent_vat_and_a_half_withheld()
    {
        var day = new DateOnly(2026, 11, 1);
        var parts = TaxMasterCalculatorV1.Resolve(new TaxMasterDataV1(
                ApprovedTaxItemTypeDefaultCatalog.For(ApprovedUnitProfileCatalog.GdcCompanyId), [], [],
                ApprovedTaxBuyerSituationCatalog.For(ApprovedUnitProfileCatalog.GdcCompanyId)),
            TaxItemTypesV1.Steel, TaxBuyerSituationsV1.Normal, day);
        var rate = SalesLineTaxRate.Of(parts.VatPercent!.Value, TaxMasterCalculatorV1.WithholdingShare(parts.WithholdingPercent!.Value));

        // 1000 kg × 25.505 = 25505.00; VAT 20 % = 5101.00; withheld 5/10 of the rounded VAT = 2550.50.
        var line = Assert.Single(SalesOrderTotalsCalculator.Calculate(
            [new SalesTotalsLineInput(L1, 1, Kg, 1000m, new CommercialDraftFields("25.505", Kg, null, null, null, null, null))],
            Try, 2, _ => rate).Lines);
        Assert.Equal(("25505.00", "5101.00", "2550.50", "30606.00", "28055.50"), (Text(line.NetAmount), Text(line.VatAmount),
            Text(line.WithheldVatAmount), Text(line.GrossAmount), Text(line.PayableAmount)));
    }

    [Fact]
    public void An_unresolved_default_is_never_turned_into_a_rate_so_no_tax_is_computed()
    {
        var data = new TaxMasterDataV1([], [], [], ApprovedTaxBuyerSituationCatalog.For(ApprovedUnitProfileCatalog.GdcCompanyId));
        var parts = TaxMasterCalculatorV1.Resolve(data, TaxItemTypesV1.Steel, TaxBuyerSituationsV1.Normal, new DateOnly(2026, 11, 1));
        Assert.Equal(TaxResolutionStatesV1.Unresolved, parts.State);

        SalesLineTaxRate? rate = parts.VatState == TaxResolutionStatesV1.Resolved ? SalesLineTaxRate.Of(parts.VatPercent!.Value, null) : null;
        var line = Assert.Single(SalesOrderTotalsCalculator.Calculate(
            [new SalesTotalsLineInput(L1, 1, Kg, 1m, new CommercialDraftFields("10", Kg, null, null, null, null, null))],
            Try, 2, _ => rate).Lines);
        Assert.Equal((SalesTaxState.NoSource, (Money?)null, (Money?)null), (line.TaxState, line.VatAmount, line.WithheldVatAmount));
    }

    [Fact]
    public void A_sat_d147_export_registered_buyer_feeds_zero_vat_and_no_withholding_to_the_calculator()
    {
        // SAT-D147 golden: the steel default (20 % + 5/10) for an export-registered buyer with a listed exemption code (SENTETIK)
        // → KDV %0 and no withholding: 1000 kg × 25.505 = 25505.00 net = gross = payable.
        var day = new DateOnly(2026, 11, 1);
        var gdc = ApprovedUnitProfileCatalog.GdcCompanyId;
        var parts = TaxMasterCalculatorV1.Resolve(new TaxMasterDataV1(ApprovedTaxItemTypeDefaultCatalog.For(gdc),
                [new TaxCodeListVersionDataV1(TaxCodeListKindsV1.Exemption, 1, day, [new TaxCodeListEntryViewV1("E701", "SENTETIK")])],
                [], ApprovedTaxBuyerSituationCatalog.For(gdc)),
            TaxItemTypesV1.Steel, TaxBuyerSituationsV1.ExportRegistered, day, exemptionCode: "E701");
        Assert.Equal((TaxResolutionStatesV1.ZeroRated, TaxResolutionStatesV1.Resolved, TaxResolutionStatesV1.NotApplied),
            (parts.VatState, parts.ExemptionCodeState, parts.WithholdingState));
        var rate = SalesLineTaxRate.Of(parts.VatPercent!.Value, null);

        var line = Assert.Single(SalesOrderTotalsCalculator.Calculate(
            [new SalesTotalsLineInput(L1, 1, Kg, 1000m, new CommercialDraftFields("25.505", Kg, null, null, null, null, null))],
            Try, 2, _ => rate).Lines);
        Assert.Equal(("25505.00", "0.00", "25505.00", "25505.00"), (Text(line.NetAmount), Text(line.VatAmount),
            Text(line.GrossAmount), Text(line.PayableAmount)));
    }

    private static TaxDefaultResolutionPartsV1 Resolved(string vat, string withholding)
    {
        var day = new DateOnly(2026, 11, 1);
        var company = Guid.NewGuid();
        var data = new TaxMasterDataV1(
            [
                new TaxItemTypeDefaultDataV1(Guid.NewGuid(), company, TaxItemTypesV1.Steel, TaxDefaultSourcesV1.Record, 1, day, true,
                    Parse(vat), null, true, Parse(withholding), "T1"),
            ],
            [new TaxCodeListVersionDataV1(TaxCodeListKindsV1.Withholding, 1, day, [new TaxCodeListEntryViewV1("T1", "SENTETIK")])],
            [], ApprovedTaxBuyerSituationCatalog.For(ApprovedUnitProfileCatalog.GdcCompanyId));
        var parts = TaxMasterCalculatorV1.Resolve(data, TaxItemTypesV1.Steel, TaxBuyerSituationsV1.Normal, day);
        Assert.Equal((TaxResolutionStatesV1.Resolved, TaxResolutionStatesV1.Resolved, TaxResolutionStatesV1.Resolved),
            (parts.VatState, parts.WithholdingState, parts.WithholdingCodeState));
        return parts;
    }

    private static decimal Parse(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    private static string? Text(Money? money) => money is { } value
        ? value.Amount.ToString("F" + value.Scale, System.Globalization.CultureInfo.InvariantCulture)
        : null;
}
