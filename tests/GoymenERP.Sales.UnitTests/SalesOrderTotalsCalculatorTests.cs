using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// API-03 / SAT-D11 / SAT-D75 / SAT-D76 / delegation No 15: golden cases of the one totals calculator. Line amount = quantity ×
/// the exact, never rounded net unit price (SAT-D76), rounded ONCE half away from zero to the money profile's amount scale; VAT
/// per line, rounded the same way; withholding = the ROUNDED line VAT × the share, rounded again (SAT-D75); document totals are
/// the sums of the rounded lines. Rates are SENTETIK inputs (SAT-D73/D74 sources do not exist; nothing here is a rate value).
/// </summary>
public sealed class SalesOrderTotalsCalculatorTests
{
    private static readonly Guid Try = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Usd = Guid.Parse("5a5a5a5a-5a5a-4a5a-8a5a-5a5a5a5a5a5a");
    private static readonly Guid Kg = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid Piece = Guid.Parse("88888888-8888-4888-8888-888888888888");
    private static readonly Guid L1 = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid L2 = Guid.Parse("10000000-0000-4000-8000-000000000002");
    private static readonly Guid L3 = Guid.Parse("10000000-0000-4000-8000-000000000003");

    private static SalesTotalsLineInput Line(Guid id, int no, string quantity, string? price, Guid? unit = null,
        string? discountPercent = null, string? discountAmount = null, string? surchargePercent = null, string? surchargeAmount = null,
        Guid? orderUnit = null) =>
        new(id, no, orderUnit ?? unit ?? Kg, decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture),
            new CommercialDraftFields(price, price is null ? null : unit ?? Kg, discountPercent, null, discountAmount, surchargePercent,
                surchargeAmount));

    private static string? Text(Money? money) => money is { } value
        ? value.Amount.ToString("F" + value.Scale, System.Globalization.CultureInfo.InvariantCulture)
        : null;

    [Fact]
    public void Multi_line_with_discount_maturity_surcharge_vat_and_withholding_rounds_every_line_once_and_sums_the_lines()
    {
        // L1: 12.5 kg × (12.50 − 10 % + 2 % vade farkı = 11.475) = 143.4375 → 143.44; VAT 20 % = 28.688 → 28.69; no withholding.
        // L2: 3 pieces × (100 − 0.50) = 298.50; VAT 20 % = 59.70; withheld 5/10 = 29.85.
        var rates = new Dictionary<Guid, SalesLineTaxRate>
        {
            [L1] = SalesLineTaxRate.Of(20m, null),
            [L2] = SalesLineTaxRate.Of(20m, 0.5m),
        };
        var totals = SalesOrderTotalsCalculator.Calculate(
            [
                Line(L1, 1, "12.5", "12.50", discountPercent: "10", surchargePercent: "2"),
                Line(L2, 2, "3", "100", Piece, discountAmount: "0.50"),
            ],
            Try, 2, id => rates[id]);

        Assert.Equal((Try, 2, SalesTaxState.Computed), (totals.CurrencyId, totals.AmountScale, totals.TaxState));
        var first = totals.Lines[0];
        Assert.Equal((SalesLineTotalState.Computed, 11.475m, "143.44", "28.69", "0.00", "172.13", "172.13"),
            (first.State, first.NetUnitPrice, Text(first.NetAmount), Text(first.VatAmount), Text(first.WithheldVatAmount),
                Text(first.GrossAmount), Text(first.PayableAmount)));
        var second = totals.Lines[1];
        Assert.Equal(("298.50", "59.70", "29.85", "358.20", "328.35"), (Text(second.NetAmount), Text(second.VatAmount),
            Text(second.WithheldVatAmount), Text(second.GrossAmount), Text(second.PayableAmount)));

        var document = totals.Document;
        Assert.Equal((true, SalesTaxState.Computed, "441.94", "88.39", "29.85", "530.33", "500.48"),
            (document.IsComplete, document.TaxState, Text(document.NetAmount), Text(document.VatAmount),
                Text(document.WithheldVatAmount), Text(document.GrossAmount), Text(document.PayableAmount)));
    }

    [Theory]
    // Half away from zero, never the banker's rule: 0.125 → 0.13 (banker's: 0.12); 0.135 → 0.14; 0.005 → 0.01.
    [InlineData("2.5", "0.05", "0.13")]
    [InlineData("2.7", "0.05", "0.14")]
    [InlineData("0.001", "5", "0.01")]
    [InlineData("0.001", "4", "0.00")]
    // The net unit price is never rounded before the multiplication: 3 × (10 − 33.333333 %) = 3 × 6.6666667 = 20.0000001.
    [InlineData("3", "10", "20.00", "33.333333")]
    public void SAT_D76_the_line_amount_is_rounded_once_half_away_from_zero_from_the_never_rounded_net_unit_price(string quantity, string price, string net,
        string? discountPercent = null)
    {
        var line = Assert.Single(SalesOrderTotalsCalculator.Calculate([Line(L1, 1, quantity, price, discountPercent: discountPercent)],
            Try, 2).Lines);
        Assert.Equal(net, Text(line.NetAmount));
    }

    [Fact]
    public void SAT_D75_vat_and_withholding_midpoints_round_away_from_zero_per_line_withholding_from_the_rounded_vat()
    {
        // net 0.25 × 10 % = 0.025 → 0.03; net 1.25 × 20 % = 0.25, withheld 5/10 = 0.125 → 0.13.
        var rates = new Dictionary<Guid, SalesLineTaxRate> { [L1] = SalesLineTaxRate.Of(10m, null), [L2] = SalesLineTaxRate.Of(20m, 0.5m) };
        var totals = SalesOrderTotalsCalculator.Calculate([Line(L1, 1, "1", "0.25"), Line(L2, 2, "1", "1.25")], Try, 2, id => rates[id]);
        Assert.Equal("0.03", Text(totals.Lines[0].VatAmount));
        Assert.Equal(("0.25", "0.13", "1.37"), (Text(totals.Lines[1].VatAmount), Text(totals.Lines[1].WithheldVatAmount),
            Text(totals.Lines[1].PayableAmount)));
    }

    [Fact]
    public void SAT_D75_withholding_is_taken_from_the_rounded_line_vat_not_the_unrounded_one()
    {
        // net 0.13 × 18 % = 0.0234 → VAT 0.02 (SAT-D75 base). Withheld 9/10: 0.02 × 0.9 = 0.018 → 0.02.
        // From the UNROUNDED VAT it would be 0.0234 × 0.9 = 0.02106 → 0.02 too, so a second case tells them apart:
        // net 0.65 × 10 % = 0.065 → VAT 0.07; withheld 5/10 of 0.07 = 0.035 → 0.04 (from the unrounded 0.065: 0.0325 → 0.03).
        var rates = new Dictionary<Guid, SalesLineTaxRate>
        {
            [L1] = SalesLineTaxRate.Of(18m, 0.9m),
            [L2] = SalesLineTaxRate.Of(10m, 0.5m),
        };
        var totals = SalesOrderTotalsCalculator.Calculate([Line(L1, 1, "1", "0.13"), Line(L2, 2, "1", "0.65")], Try, 2, id => rates[id]);
        Assert.Equal(("0.02", "0.02"), (Text(totals.Lines[0].VatAmount), Text(totals.Lines[0].WithheldVatAmount)));
        Assert.Equal(("0.07", "0.04", "0.68"), (Text(totals.Lines[1].VatAmount), Text(totals.Lines[1].WithheldVatAmount),
            Text(totals.Lines[1].PayableAmount)));
    }

    [Fact]
    public void SAT_D76_the_net_unit_price_is_reported_unrounded_beside_the_rounded_line_amount()
    {
        // 12.50 − 10 % + 2 % vade farkı = 11.475 (three decimals, kept); 1 × 11.475 = 11.475 → 11.48.
        var line = Assert.Single(SalesOrderTotalsCalculator.Calculate(
            [Line(L1, 1, "1", "12.50", discountPercent: "10", surchargePercent: "2")], Try, 2).Lines);
        Assert.Equal((11.475m, "11.48"), (line.NetUnitPrice, Text(line.NetAmount)));
    }

    [Fact]
    public void Document_totals_are_sums_of_rounded_lines_never_a_rounded_document_net()
    {
        // Three lines of 0.005 each: every line is 0.01 (SAT-D11 per line), the document 0.03 — not round(0.015) = 0.02.
        var totals = SalesOrderTotalsCalculator.Calculate(
            [Line(L1, 1, "0.001", "5"), Line(L2, 2, "0.001", "5"), Line(L3, 3, "0.001", "5")], Try, 2);
        Assert.All(totals.Lines, line => Assert.Equal("0.01", Text(line.NetAmount)));
        Assert.Equal("0.03", Text(totals.Document.NetAmount));
    }

    [Fact]
    public void Without_a_vat_master_the_net_is_computed_and_every_tax_is_no_source_never_zero()
    {
        var totals = SalesOrderTotalsCalculator.Calculate([Line(L1, 1, "2", "10"), Line(L2, 2, "1", "5")], Try, 2);
        Assert.Equal(SalesTaxState.NoSource, totals.TaxState);
        Assert.All(totals.Lines, line =>
        {
            Assert.Equal((SalesLineTotalState.Computed, SalesTaxState.NoSource), (line.State, line.TaxState));
            Assert.Null(line.TaxRate);
            Assert.Null(line.VatAmount);
            Assert.Null(line.WithheldVatAmount);
            Assert.Null(line.GrossAmount);
            Assert.Null(line.PayableAmount);
        });
        Assert.Equal((true, "25.00", SalesTaxState.NoSource), (totals.Document.IsComplete, Text(totals.Document.NetAmount),
            totals.Document.TaxState));
        Assert.Null(totals.Document.VatAmount);
        Assert.Null(totals.Document.GrossAmount);
        Assert.Null(totals.Document.PayableAmount);
    }

    [Fact]
    public void A_vat_master_without_a_rate_for_one_line_leaves_the_document_tax_no_source()
    {
        var totals = SalesOrderTotalsCalculator.Calculate([Line(L1, 1, "2", "10"), Line(L2, 2, "1", "5")], Try, 2,
            id => id == L1 ? SalesLineTaxRate.Of(20m, null) : null);
        Assert.Equal((SalesTaxState.Computed, SalesTaxState.NoSource), (totals.Lines[0].TaxState, totals.Lines[1].TaxState));
        Assert.Equal(("4.00", null), (Text(totals.Lines[0].VatAmount), Text(totals.Lines[1].VatAmount)));
        Assert.Equal((true, "25.00", SalesTaxState.NoSource), (totals.Document.IsComplete, Text(totals.Document.NetAmount),
            totals.Document.TaxState));
        Assert.Null(totals.Document.VatAmount);
    }

    [Fact]
    public void An_unpriced_line_has_no_amount_and_the_document_is_incomplete_without_a_partial_sum()
    {
        var totals = SalesOrderTotalsCalculator.Calculate([Line(L1, 1, "2", "10"), Line(L2, 2, "1", null)], Try, 2,
            _ => SalesLineTaxRate.Of(20m, null));
        Assert.Equal(("20.00", SalesTaxState.Computed), (Text(totals.Lines[0].NetAmount), totals.Lines[0].TaxState));
        var unpriced = totals.Lines[1];
        Assert.Equal((SalesLineTotalState.Unpriced, SalesTaxState.NotComputed), (unpriced.State, unpriced.TaxState));
        Assert.Null(unpriced.NetUnitPrice);
        Assert.Null(unpriced.NetAmount);
        Assert.Null(unpriced.VatAmount);
        Assert.Equal((false, 1, 0, SalesTaxState.NotComputed), (totals.Document.IsComplete, totals.Document.UnpricedLineCount,
            totals.Document.PriceUnitNotOrderUnitLineCount, totals.Document.TaxState));
        Assert.Null(totals.Document.NetAmount);
        Assert.Null(totals.Document.VatAmount);
    }

    [Fact]
    public void A_line_priced_in_another_unit_than_its_order_unit_is_never_converted_by_guess()
    {
        var totals = SalesOrderTotalsCalculator.Calculate([Line(L1, 1, "2", "10", unit: Piece, orderUnit: Kg)], Try, 2);
        var line = Assert.Single(totals.Lines);
        Assert.Equal((SalesLineTotalState.PriceUnitNotOrderUnit, null), (line.State, Text(line.NetAmount)));
        Assert.Equal((false, 0, 1), (totals.Document.IsComplete, totals.Document.UnpricedLineCount,
            totals.Document.PriceUnitNotOrderUnitLineCount));
    }

    [Fact]
    public void An_order_without_lines_is_incomplete_with_no_sum_never_a_computed_zero()
    {
        // Review B P2-2 / mutation m2: no line is nothing to total — never "Computed 0.00".
        var totals = SalesOrderTotalsCalculator.Calculate([], Try, 2, _ => SalesLineTaxRate.Of(20m, null));
        Assert.Empty(totals.Lines);
        Assert.Equal((SalesDocumentTotalState.Incomplete, false, 0, 0, 0, SalesTaxState.NotComputed), (totals.Document.State,
            totals.Document.IsComplete, totals.Document.UnpricedLineCount, totals.Document.PriceUnitNotOrderUnitLineCount,
            totals.Document.AmountOutOfRangeLineCount, totals.Document.TaxState));
        Assert.Null(totals.Document.NetAmount);
        Assert.Null(totals.Document.VatAmount);
    }

    [Fact]
    public void A_line_amount_beyond_the_decimal_range_is_out_of_range_and_the_document_incomplete_never_an_exception()
    {
        // Review B P2-1: 999999999999999.999 × 100000000000000 ≈ 1e29 > System.Decimal's maximum (≈ 7.9e28).
        var totals = SalesOrderTotalsCalculator.Calculate(
            [Line(L1, 1, "999999999999999.999", "100000000000000"), Line(L2, 2, "1", "10")], Try, 2, _ => SalesLineTaxRate.Of(20m, null));
        var outOfRange = totals.Lines[0];
        Assert.Equal((SalesLineTotalState.AmountOutOfRange, SalesTaxState.NotComputed), (outOfRange.State, outOfRange.TaxState));
        Assert.Null(outOfRange.NetUnitPrice);
        Assert.Null(outOfRange.NetAmount);
        Assert.Null(outOfRange.VatAmount);
        Assert.Equal("10.00", Text(totals.Lines[1].NetAmount));
        Assert.Equal((SalesDocumentTotalState.Incomplete, 0, 0, 1), (totals.Document.State, totals.Document.UnpricedLineCount,
            totals.Document.PriceUnitNotOrderUnitLineCount, totals.Document.AmountOutOfRangeLineCount));
        Assert.Null(totals.Document.NetAmount);
    }

    [Fact]
    public void A_gross_beyond_the_decimal_range_takes_the_whole_line_out_of_range()
    {
        // net 70000000000000000000000000000 fits; + 20 % VAT = 8.4e28 does not: no part of the line is shown.
        var line = Assert.Single(SalesOrderTotalsCalculator.Calculate(
            [Line(L1, 1, "700000000000000", "100000000000000")], Try, 0, _ => SalesLineTaxRate.Of(20m, null)).Lines);
        Assert.Equal(SalesLineTotalState.AmountOutOfRange, line.State);
        Assert.Null(line.NetAmount);
        Assert.Null(line.GrossAmount);
    }

    [Fact]
    public void A_document_sum_beyond_the_decimal_range_is_out_of_range_with_every_line_still_shown()
    {
        // Each line 5e28 fits; their sum 1e29 does not.
        var totals = SalesOrderTotalsCalculator.Calculate(
            [Line(L1, 1, "500000000000000", "100000000000000"), Line(L2, 2, "500000000000000", "100000000000000")], Try, 0);
        Assert.All(totals.Lines, line => Assert.Equal(("Computed", "50000000000000000000000000000"),
            (line.State.ToString(), Text(line.NetAmount))));
        Assert.Equal((SalesDocumentTotalState.AmountOutOfRange, 0, 0, 0, SalesTaxState.NotComputed), (totals.Document.State,
            totals.Document.UnpricedLineCount, totals.Document.PriceUnitNotOrderUnitLineCount, totals.Document.AmountOutOfRangeLineCount,
            totals.Document.TaxState));
        Assert.Null(totals.Document.NetAmount);
    }

    [Fact]
    public void A_document_sum_with_more_digits_than_decimal_holds_is_out_of_range_never_silently_rounded()
    {
        // Review B P2-3 repro: each line 1 × 600000000000000000000000000.01 fits (29 significant digits); the exact sum
        // 1200000000000000000000000000.02 needs 30, and System.Decimal's + would answer …0.00 without any exception.
        var totals = SalesOrderTotalsCalculator.Calculate(
            [Line(L1, 1, "1", "600000000000000000000000000.01"), Line(L2, 2, "1", "600000000000000000000000000.01")], Try, 2);
        Assert.All(totals.Lines, line => Assert.Equal((SalesLineTotalState.Computed, "600000000000000000000000000.01"),
            (line.State, Text(line.NetAmount))));
        Assert.Equal((SalesDocumentTotalState.AmountOutOfRange, SalesTaxState.NotComputed),
            (totals.Document.State, totals.Document.TaxState));
        Assert.Null(totals.Document.NetAmount);
    }

    [Fact]
    public void A_line_gross_with_more_digits_than_decimal_holds_takes_the_line_out_of_range_never_silently_rounded()
    {
        // Review B P2-3, line level: net 700000000000000000000000000.01 fits; VAT 20 % = 140000000000000000000000000.00; the exact
        // gross 840000000000000000000000000.01 needs 29 digits beyond System.Decimal's mantissa, so its + would round to …0.0.
        var line = Assert.Single(SalesOrderTotalsCalculator.Calculate(
            [Line(L1, 1, "1", "700000000000000000000000000.01")], Try, 2, _ => SalesLineTaxRate.Of(20m, null)).Lines);
        Assert.Equal((SalesLineTotalState.AmountOutOfRange, SalesTaxState.NotComputed), (line.State, line.TaxState));
        Assert.Null(line.NetAmount);
        Assert.Null(line.GrossAmount);
        Assert.Null(line.PayableAmount);
    }

    [Fact]
    public void The_exact_sum_is_exact_or_an_overflow_never_a_rounded_decimal()
    {
        Assert.Equal(441.94m, SalesLinePriceCalculator.ExactSum([143.44m, 298.50m]));
        Assert.Equal(500.48m, SalesLinePriceCalculator.ExactSum([530.33m], [29.85m]));
        // decimal's own + rounds this silently; the exact sum refuses it.
        Assert.Equal(1200000000000000000000000000.00m, 600000000000000000000000000.01m + 600000000000000000000000000.01m);
        Assert.Throws<OverflowException>(() => SalesLinePriceCalculator.ExactSum(
            [600000000000000000000000000.01m, 600000000000000000000000000.01m]));
        Assert.Throws<OverflowException>(() => SalesLinePriceCalculator.ExactSum([decimal.MaxValue, 1m]));
    }

    [Fact]
    public void A_zero_price_is_a_computed_zero_unlike_an_absent_price()
    {
        var totals = SalesOrderTotalsCalculator.Calculate([Line(L1, 1, "5", "0")], Try, 2, _ => SalesLineTaxRate.Of(20m, 0.5m));
        var line = Assert.Single(totals.Lines);
        Assert.Equal((SalesLineTotalState.Computed, "0.00", "0.00", "0.00", "0.00"), (line.State, Text(line.NetAmount),
            Text(line.VatAmount), Text(line.WithheldVatAmount), Text(line.PayableAmount)));
        Assert.True(totals.Document.IsComplete);
    }

    [Fact]
    public void A_foreign_currency_order_is_totalled_in_its_own_currency_at_its_profiles_scale_without_conversion()
    {
        var usd = SalesOrderTotalsCalculator.Calculate([Line(L1, 1, "1.5", "10.25", discountAmount: "0.10")], Usd, 2,
            _ => SalesLineTaxRate.Of(0m, null));
        var line = Assert.Single(usd.Lines);
        Assert.Equal((Usd, Usd, "15.23", "0.00", "15.23"), (usd.CurrencyId, line.NetAmount!.Value.CurrencyId, Text(line.NetAmount),
            Text(line.VatAmount), Text(line.GrossAmount)));

        // The scale is the money profile's, never a constant: a whole-unit profile rounds 3 × 10.5 = 31.5 to 32.
        var whole = SalesOrderTotalsCalculator.Calculate([Line(L1, 1, "3", "10.5")], Usd, 0);
        Assert.Equal("32", Text(Assert.Single(whole.Lines).NetAmount));
    }

    [Fact]
    public void Large_and_high_precision_amounts_are_multiplied_exactly()
    {
        var big = SalesOrderTotalsCalculator.Calculate([Line(L1, 1, "999999999999999.999", "79228.515")], Try, 2);
        Assert.Equal("79228514999999999920.77", Text(Assert.Single(big.Lines).NetAmount));

        // net = 1 − 33.333333 % + 1.234567 % = 0.6748971167078189 (exact); × 999999999999.999 = 674897116707.81822… → .82
        var precise = SalesOrderTotalsCalculator.Calculate(
            [Line(L1, 1, "999999999999.999", "1", discountPercent: "33.333333", surchargePercent: "1.234567")], Try, 2);
        var line = Assert.Single(precise.Lines);
        Assert.Equal((0.6748971167078189m, "674897116707.82"), (line.NetUnitPrice, Text(line.NetAmount)));
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(100.01, null)]
    [InlineData(20, 0.0)]
    [InlineData(20, 1.01)]
    public void A_tax_rate_outside_its_range_is_refused(double vatPercent, double? share)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SalesLineTaxRate.Of((decimal)vatPercent, share is { } value ? (decimal)value : null));
    }

    [Fact]
    public void A_full_share_and_a_zero_rate_are_valid_tax_rates()
    {
        Assert.Equal(1m, SalesLineTaxRate.Of(20m, 1m).WithholdingShare);
        Assert.Equal(0m, SalesLineTaxRate.Of(0m, null).VatPercent);
    }

    [Fact]
    public void The_scale_and_currency_are_required()
    {
        Assert.Throws<ArgumentException>(() => SalesOrderTotalsCalculator.Calculate([Line(L1, 1, "1", "1")], Guid.Empty, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => SalesOrderTotalsCalculator.Calculate([Line(L1, 1, "1", "1")], Try, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SalesOrderTotalsCalculator.Calculate([Line(L1, 1, "1", "1")], Try, 29));
    }
}
