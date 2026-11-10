using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.CustomerReturns;
using GoymenERP.Modules.Sales.Domain.Invoices;
using static GoymenERP.Sales.UnitTests.SalesCustomerReturnFixtures;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// API-40 (SAT-D289, SAT-D292, SAT-D293): the customer return calculator's goldens — per amount type proportioning with SAT-D11
/// rounding, the last return closing each type's kuruş residue against the FINALISED priors, a line returned several times, a
/// foreign-currency line proportioned from its stored TL amounts (SAT-D223 base, no re-conversion), the matrah + KDV − tevkifat
/// versus line total drift, the sign-flip review flag, the SAT-D241 separate match, and the SAT-D293 (4) balance with the single
/// decrement invariant. Every value is SENTETIK.
/// </summary>
public sealed class SalesCustomerReturnCalculatorTests
{
    private static SalesCustomerReturnOriginalLine Original(SalesInvoice invoice, int index = 0) =>
        SalesCustomerReturnOriginalLine.Of(invoice.Lines[index]);

    [Fact]
    public void A_line_returned_three_times_rounds_each_type_on_its_own_and_the_last_return_closes_the_kurus_residue()
    {
        var original = Original(TlInvoice(new StoredLine(3m, 100.00m, 20.00m, 0m)));

        var first = SalesCustomerReturnCalculator.Calculate(original, [], 1m);
        var second = SalesCustomerReturnCalculator.Calculate(original, [Prior(first)], 1m);
        var last = SalesCustomerReturnCalculator.Calculate(original, [Prior(first), Prior(second)], 1m);

        Assert.Equal(["33.33", "6.67", "0.00", "40.00"], Text(first.Amounts));
        Assert.Equal(["33.33", "6.67", "0.00", "40.00"], Text(second.Amounts));
        Assert.Equal(["33.34", "6.66", "0.00", "40.00"], Text(last.Amounts));
        Assert.Equal((false, false, true), (first.IsLastReturn, second.IsLastReturn, last.IsLastReturn));
        // The returns add up to the original line, type by type: nothing is left over and nothing is invented.
        var sum = SalesCustomerReturnAmounts.Sum([first.Amounts, second.Amounts, last.Amounts], Try, 2);
        Assert.Equal(["100.00", "20.00", "0.00", "120.00"], Text(sum));
        Assert.Null(last.Functional);
        Assert.False(last.NeedsReview);
    }

    [Fact]
    public void The_matrah_plus_kdv_minus_tevkifat_drift_against_the_line_total_is_shown_and_cancels_on_the_last_return()
    {
        // Matrah + KDV: net 10.05 and VAT 2.01 round up separately (5.03 + 1.01) while the line total 12.06 halves exactly to 6.03.
        var vatLine = Original(TlInvoice(new StoredLine(2m, 10.05m, 2.01m, 0m)));
        var vatFirst = SalesCustomerReturnCalculator.Calculate(vatLine, [], 1m);
        var vatLast = SalesCustomerReturnCalculator.Calculate(vatLine, [Prior(vatFirst)], 1m);
        Assert.Equal(["5.03", "1.01", "0.00", "6.03"], Text(vatFirst.Amounts));
        Assert.Equal(("6.04", "-0.01"), (F(vatFirst.Amounts.DerivedPayable), F(vatFirst.Amounts.Drift)));
        Assert.Equal(["5.02", "1.00", "0.00", "6.03"], Text(vatLast.Amounts));
        Assert.Equal(("6.02", "0.01"), (F(vatLast.Amounts.DerivedPayable), F(vatLast.Amounts.Drift)));

        // Tevkifat: withholding 1.01 halves up to 0.51 while the line total 10.99 halves up to 5.50.
        var withholdingLine = Original(TlInvoice(new StoredLine(2m, 10.00m, 2.00m, 1.01m, WithholdingShare: 0.5m)));
        var whFirst = SalesCustomerReturnCalculator.Calculate(withholdingLine, [], 1m);
        var whLast = SalesCustomerReturnCalculator.Calculate(withholdingLine, [Prior(whFirst)], 1m);
        Assert.Equal(["5.00", "1.00", "0.51", "5.50"], Text(whFirst.Amounts));
        Assert.Equal(("5.49", "0.01"), (F(whFirst.Amounts.DerivedPayable), F(whFirst.Amounts.Drift)));
        Assert.Equal(["5.00", "1.00", "0.50", "5.49"], Text(whLast.Amounts));
        Assert.Equal(("5.50", "-0.01"), (F(whLast.Amounts.DerivedPayable), F(whLast.Amounts.Drift)));

        // Across the line's returns the drift is zero: each type closes on its own original amount.
        var total = SalesCustomerReturnAmounts.Sum([whFirst.Amounts, whLast.Amounts], Try, 2);
        Assert.Equal(["10.00", "2.00", "1.01", "10.99"], Text(total));
        Assert.Equal("0.00", F(total.Drift));
    }

    [Fact]
    public void A_foreign_currency_line_proportions_its_stored_tl_amounts_and_never_converts_the_return_again()
    {
        // The stored TL VAT 691.37 carries a SAT-D223 group difference (20.00 × 34.5678 = 691.36); it is the base as stored.
        var invoice = UsdInvoice(34.5678m, new StoredLine(3m, 100.00m, 20.00m, 0m, Tl: (3456.78m, 691.37m, 0m)));
        var original = Original(invoice);

        var first = SalesCustomerReturnCalculator.Calculate(original, [], 2m);
        var last = SalesCustomerReturnCalculator.Calculate(original, [Prior(first)], 1m);

        Assert.Equal(["66.67", "13.33", "0.00", "80.00"], Text(first.Amounts));
        Assert.Equal(Usd, first.Amounts.Net.CurrencyId);
        Assert.Equal(["2304.52", "460.91", "0.00", "2765.43"], Text(first.Functional!));
        Assert.Equal(Try, first.Functional!.Net.CurrencyId);
        // Not 66.67 USD × 34.5678 = 2304.64 TL: the TL amount is the stored TL line's proportion (SAT-D289 (2), SAT-D292 (5)).
        Assert.NotEqual(Math.Round(first.Amounts.Net.Amount * 34.5678m, 2, MidpointRounding.AwayFromZero), first.Functional.Net.Amount);
        Assert.Equal(["33.33", "6.67", "0.00", "40.00"], Text(last.Amounts));
        Assert.Equal(["1152.26", "230.46", "0.00", "1382.72"], Text(last.Functional!));
        Assert.True(last.IsLastReturn);
        Assert.Equal(["3456.78", "691.37", "0.00", "4148.15"],
            Text(SalesCustomerReturnAmounts.Sum([first.Functional, last.Functional!], Try, 2)));
    }

    [Fact]
    public void A_last_return_whose_residue_flips_the_sign_of_an_amount_is_applied_as_decided_and_flagged_for_review()
    {
        var original = Original(TlInvoice(new StoredLine(4m, 1.00m, 0.20m, 0.02m, WithholdingShare: 0.1m)));
        var priors = new List<SalesCustomerReturnPrior>();
        for (var index = 0; index < 3; index++)
        {
            var result = SalesCustomerReturnCalculator.Calculate(original, priors, 1m);
            Assert.Equal(["0.25", "0.05", "0.01", "0.30"], Text(result.Amounts));
            Assert.False(result.NeedsReview);
            priors.Add(Prior(result));
        }

        var last = SalesCustomerReturnCalculator.Calculate(original, priors, 1m);

        Assert.Equal(["0.25", "0.05", "-0.01", "0.28"], Text(last.Amounts));
        Assert.True(last.NeedsReview);
    }

    [Fact]
    public void A_return_quantity_must_be_positive_within_the_scale_and_at_most_the_remaining_finalized_quantity()
    {
        var original = Original(TlInvoice(new StoredLine(3m, 100.00m, 20.00m, 0m)));
        var first = SalesCustomerReturnCalculator.Calculate(original, [], 2m);

        Assert.Equal(1m, SalesCustomerReturnCalculator.RemainingQuantity(original, [Prior(first)]));
        Assert.Equal(SalesRuleCodes.CustomerReturnQuantityExceeded,
            Assert.Throws<SalesRuleException>(() => SalesCustomerReturnCalculator.Calculate(original, [Prior(first)], 2m)).Code);
        Assert.Equal(SalesRuleCodes.CustomerReturnQuantityExceeded,
            Assert.Throws<SalesRuleException>(() => SalesCustomerReturnCalculator.Calculate(original, [], 3.001m)).Code);
        foreach (var invalid in new[] { 0m, -1m, 0.0001m })
        {
            Assert.Equal(SalesRuleCodes.CustomerReturnQuantityInvalid,
                Assert.Throws<SalesRuleException>(() => SalesCustomerReturnCalculator.Calculate(original, [], invalid)).Code);
        }
    }

    [Fact]
    public void The_official_match_compares_payable_vat_and_withholding_separately_with_an_inclusive_tolerance()
    {
        var totals = new SalesCustomerReturnAmounts(M(1152.26m), M(230.46m), M(0m), M(1382.72m));

        var match = SalesCustomerReturnCalculator.EvaluateMatch(totals, 1382.70m, 230.46m, 0m);

        Assert.Equal((0.02m, 0m, 0m), (match.PayableDifference, match.VatDifference, match.WithholdingDifference));
        Assert.False(match.Exceeds(0.02m));
        Assert.True(match.Exceeds(0.01m));
        // A VAT difference alone sends the difference to the approval even when the payable matches (SAT-D241).
        var vatOnly = SalesCustomerReturnCalculator.EvaluateMatch(totals, 1382.72m, 230.40m, 0m);
        Assert.Equal(0m, vatOnly.PayableDifference);
        Assert.True(vatOnly.Exceeds(0.05m));
    }

    [Fact]
    public void An_unlinked_return_must_fit_the_invoiced_not_shipped_balance_and_a_linked_return_never_decrements_it()
    {
        var lineId = LineId(1);
        var unlinked = new SalesCustomerReturnPrior(3m, new SalesCustomerReturnAmounts(M(1m), M(0m), M(0m), M(1m)), null, false);
        var linked = unlinked with { IsShipmentLinked = true };

        Assert.Equal(1m, SalesCustomerReturnBalance.UnshippedRemaining(10m, 6m, [unlinked]));
        // Single decrement: a later return linked to a shipment of the same goods does not decrement the balance a second time.
        Assert.Equal(1m, SalesCustomerReturnBalance.UnshippedRemaining(10m, 6m, [unlinked, linked]));
        SalesCustomerReturnBalance.RequireFits(false, 1m, 10m, 6m, [unlinked, linked], lineId);
        SalesCustomerReturnBalance.RequireFits(true, 7m, 10m, 6m, [unlinked, linked], lineId);
        var refused = Assert.Throws<SalesRuleException>(() =>
            SalesCustomerReturnBalance.RequireFits(false, 1.5m, 10m, 6m, [unlinked, linked], lineId));
        Assert.Equal((SalesRuleCodes.CustomerReturnUnshippedBalanceExceeded, (Guid?)lineId), (refused.Code, refused.LineId));
        // A whole shipped invoice line has no invoiced-not-shipped balance: every unlinked return of it is refused.
        Assert.Throws<SalesRuleException>(() => SalesCustomerReturnBalance.RequireFits(false, 0.001m, 3m, 3m, [], lineId));
    }
}
