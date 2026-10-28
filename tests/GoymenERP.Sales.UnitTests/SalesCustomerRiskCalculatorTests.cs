using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// MOD-01 / API-06: the customer risk calculator (DEC-FIN-007; SAT-D46…D49, SAT-D53). Every amount is SYNTHETIC test data at
/// scale 2; the limit is never a code value. The decision is made on gross risk + this order in the LIMIT's currency (SAT-D48,
/// SAT-D53); a total equal to the limit is within it, one cent more is over (SAT-D49). Every unavailable path — a missing or
/// unreadable part, another currency without a rate record (SAT-D47), more decimals than the scale, a negative amount, a
/// tax-basis mismatch (SAT-D46), an undefined limit, an unknown local or limit currency — is "Unavailable", never "Within".
/// </summary>
public sealed class SalesCustomerRiskCalculatorTests
{
    private static readonly Guid Try = Guid.Parse("c0000000-0000-4000-8000-000000000001");
    private static readonly Guid Usd = Guid.Parse("c0000000-0000-4000-8000-000000000002");
    private const int Scale = 2;
    private static readonly SalesRiskCurrency Local = new(Try, Scale);
    private static readonly SalesRiskCurrency UsdLimit = new(Usd, Scale);

    private static decimal D(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

    private static SalesRiskInput Taxed(params decimal[] amounts) => In(Try, amounts);

    private static SalesRiskInput In(Guid currency, params decimal[] amounts) =>
        SalesRiskInput.Available([.. amounts.Select(amount => new SalesRiskAmount(currency, amount))], SalesRiskTaxBasis.IncludingTax);

    private static SalesRiskInput Limit(decimal amount, Guid? currency = null) =>
        SalesRiskInput.Available([new SalesRiskAmount(currency ?? Try, amount)], null);

    private static SalesRiskEvaluation Evaluate(SalesRiskInput receivables, SalesRiskInput shipped, SalesRiskInput remainder,
        SalesRiskInput thisOrder, SalesRiskInput limit, SalesRiskCurrency? local = null, SalesRiskCurrency? limitCurrency = null) =>
        SalesCustomerRiskCalculator.Evaluate(local ?? Local, limitCurrency ?? Local, receivables, shipped, remainder, thisOrder, limit);

    public static TheoryData<string, string, string, string, string, SalesRiskOutcome> Boundaries => new()
    {
        // receivables, shipped-not-invoiced, approved remainder, this order, limit → outcome (decided on gross + this order)
        { "400.00", "100.00", "300.00", "200.00", "1000.00", SalesRiskOutcome.Within },
        { "400.00", "100.00", "300.00", "200.01", "1000.00", SalesRiskOutcome.Over },
        { "400.00", "100.00", "300.00", "199.99", "1000.00", SalesRiskOutcome.Within },
        // The gross risk alone is within; this order takes it over (SAT-D48).
        { "900.00", "0", "0", "100.01", "1000.00", SalesRiskOutcome.Over },
        { "0", "0", "0", "0", "0", SalesRiskOutcome.Within },
        { "0", "0", "0.01", "0", "0", SalesRiskOutcome.Over },
        { "999999.99", "0", "0", "0.01", "1000000", SalesRiskOutcome.Within },
    };

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void Gross_risk_is_the_three_stages_the_decision_adds_this_order_and_only_exceeding_the_limit_is_over(string receivables,
        string shipped, string remainder, string thisOrder, string limit, SalesRiskOutcome expected)
    {
        var evaluation = Evaluate(Taxed(D(receivables)), Taxed(D(shipped)), Taxed(D(remainder)), Taxed(D(thisOrder)), Limit(D(limit)));

        Assert.Equal(expected, evaluation.Outcome);
        Assert.Empty(evaluation.Reasons);
        var gross = D(receivables) + D(shipped) + D(remainder);
        foreach (var totals in new[] { evaluation.InLocalCurrency!, evaluation.InLimitCurrency! })
        {
            Assert.Equal(Local, totals.Currency);
            Assert.Equal((gross, gross + D(thisOrder)), (totals.GrossRisk!.Value.Amount, totals.GrossRiskWithThisOrder!.Value.Amount));
            Assert.Equal((Try, Scale), (totals.GrossRisk.Value.CurrencyId, totals.GrossRisk.Value.Scale));
        }

        Assert.Equal(D(limit), Assert.NotNull(evaluation.CreditLimit).Amount);
    }

    [Fact]
    public void Several_amounts_of_one_stage_are_added_exactly_and_an_empty_stage_is_a_recorded_zero()
    {
        var evaluation = Evaluate(Taxed(0.10m, 0.20m), Taxed(), Taxed(1000.5m), Taxed(), Limit(1000.80m));

        Assert.Equal((SalesRiskOutcome.Within, 1000.80m), (evaluation.Outcome, evaluation.InLimitCurrency!.GrossRiskWithThisOrder!.Value.Amount));
        Assert.Equal(SalesRiskOutcome.Over, Evaluate(Taxed(0.10m, 0.20m), Taxed(), Taxed(1000.51m), Taxed(), Limit(1000.80m)).Outcome);
    }

    public static TheoryData<int> EachPart => new() { 0, 1, 2, 3 };

    [Theory]
    [MemberData(nameof(EachPart))]
    public void A_part_without_a_source_or_an_unreadable_part_is_unavailable_never_within(int missing)
    {
        foreach (var input in new[] { SalesRiskInput.NoSource("owner_missing"), SalesRiskInput.Unavailable("owner_failed") })
        {
            var parts = new[] { Taxed(0m), Taxed(0m), Taxed(0m), Taxed(0m) };
            parts[missing] = input;

            // Even a zero exposure against a large limit: a missing part is never assumed zero.
            var evaluation = Evaluate(parts[0], parts[1], parts[2], parts[3], Limit(1_000_000m));

            Assert.Equal(SalesRiskOutcome.Unavailable, evaluation.Outcome);
            Assert.Null(evaluation.InLimitCurrency!.GrossRiskWithThisOrder);
            var component = new[]
            {
                SalesRiskComponents.OpenReceivables, SalesRiskComponents.ShippedNotInvoiced, SalesRiskComponents.ApprovedUnshippedRemainder,
                SalesRiskComponents.ThisOrder,
            }[missing];
            Assert.Equal([new SalesRiskReason(component, input.ReasonCode!)], evaluation.Reasons);
            // Only this order missing: the gross risk itself is still answered (SAT-D48 shows both).
            Assert.Equal(missing == 3, evaluation.InLimitCurrency.GrossRisk is not null);
            Assert.Equal(1_000_000m, evaluation.CreditLimit!.Value.Amount);
        }
    }

    [Fact]
    public void A_missing_or_unreadable_limit_is_unavailable_while_the_local_totals_are_still_computed()
    {
        var noLimit = Evaluate(Taxed(1m), Taxed(2m), Taxed(3m), Taxed(4m), SalesRiskInput.NoSource("credit_limit_owner_missing"),
            limitCurrency: null);
        Assert.Equal(SalesRiskOutcome.Unavailable, noLimit.Outcome);
        Assert.Equal((6m, 10m), (noLimit.InLocalCurrency!.GrossRisk!.Value.Amount, noLimit.InLocalCurrency.GrossRiskWithThisOrder!.Value.Amount));
        Assert.Null(noLimit.InLimitCurrency);
        Assert.Null(noLimit.CreditLimit);
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.CreditLimit, "credit_limit_owner_missing")], noLimit.Reasons);

        var unreadable = Evaluate(Taxed(1m), Taxed(2m), Taxed(3m), Taxed(), new SalesRiskInput(SalesRiskInputState.Unavailable, [], null, null));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.CreditLimit, SalesRiskReasonCodes.Unavailable)], unreadable.Reasons);
        var withoutReason = Evaluate(new SalesRiskInput(SalesRiskInputState.NoSource, [], null, null), Taxed(), Taxed(), Taxed(), Limit(1m));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.OpenReceivables, SalesRiskReasonCodes.NoSource)], withoutReason.Reasons);
        // An unknown state is an unreadable answer: fail-closed.
        var unknown = Evaluate(new SalesRiskInput((SalesRiskInputState)99, [], SalesRiskTaxBasis.IncludingTax, null), Taxed(), Taxed(), Taxed(), Limit(1m));
        Assert.Equal(SalesRiskOutcome.Unavailable, unknown.Outcome);
    }

    [Fact]
    public void An_available_limit_with_no_single_amount_is_not_defined_never_unlimited_or_zero()
    {
        foreach (var limit in new[]
                 {
                     SalesRiskInput.Available([], null),
                     SalesRiskInput.Available([new SalesRiskAmount(Try, 10m), new SalesRiskAmount(Try, 20m)], null),
                 })
        {
            var evaluation = Evaluate(Taxed(1m), Taxed(), Taxed(), Taxed(), limit);
            Assert.Equal(SalesRiskOutcome.Unavailable, evaluation.Outcome);
            Assert.Equal([new SalesRiskReason(SalesRiskComponents.CreditLimit, SalesRiskReasonCodes.CreditLimitNotDefined)], evaluation.Reasons);
            Assert.Equal(1m, evaluation.InLocalCurrency!.GrossRisk!.Value.Amount);
        }
    }

    [Fact]
    public void The_decision_is_made_in_the_limit_currency_and_another_currency_needs_a_rate_record_that_does_not_exist()
    {
        // SAT-D53: a USD limit — every part is in TRY, and no DEC-FIN-010 rate record exists (SAT-D47): no USD total, no decision;
        // the local (TL) totals still stand. Nothing is converted with an invented rate.
        var usd = Evaluate(Taxed(10m), Taxed(), Taxed(), Taxed(), Limit(1000m, Usd), limitCurrency: UsdLimit);
        Assert.Equal(SalesRiskOutcome.Unavailable, usd.Outcome);
        Assert.Equal((10m, 10m), (usd.InLocalCurrency!.GrossRisk!.Value.Amount, usd.InLocalCurrency.GrossRiskWithThisOrder!.Value.Amount));
        Assert.Equal((Usd, (Money?)null, (Money?)null), (usd.InLimitCurrency!.Currency.CurrencyId, usd.InLimitCurrency.GrossRisk,
            usd.InLimitCurrency.GrossRiskWithThisOrder));
        Assert.Equal(1000m, usd.CreditLimit!.Value.Amount);
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.OpenReceivables, SalesRiskReasonCodes.ExchangeRateUnavailable)], usd.Reasons);

        // Everything in USD against a USD limit: decided in USD; the local (TL) totals are the ones that need the rate.
        var allUsd = Evaluate(In(Usd, 600m), In(Usd), In(Usd, 300m), In(Usd, 100m), Limit(1000m, Usd), limitCurrency: UsdLimit);
        Assert.Equal((Usd, 1000m), (allUsd.InLimitCurrency!.Currency.CurrencyId, allUsd.InLimitCurrency.GrossRiskWithThisOrder!.Value.Amount));
        Assert.Null(allUsd.InLocalCurrency!.GrossRisk);
        Assert.Equal(SalesRiskOutcome.Unavailable, allUsd.Outcome);
        Assert.All(allUsd.Reasons, reason => Assert.Equal(SalesRiskReasonCodes.ExchangeRateUnavailable, reason.Code));

        // A part with an amount in another currency: refused (listed once even though both totals met it).
        var mixed = SalesRiskInput.Available([new SalesRiskAmount(Try, 10m), new SalesRiskAmount(Usd, 1m)], SalesRiskTaxBasis.IncludingTax);
        var evaluation = Evaluate(mixed, Taxed(), Taxed(), Taxed(), Limit(1000m));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.OpenReceivables, SalesRiskReasonCodes.ExchangeRateUnavailable)], evaluation.Reasons);
        Assert.Null(evaluation.InLocalCurrency!.GrossRisk);
    }

    [Fact]
    public void An_unknown_local_or_limit_currency_is_unavailable_never_the_order_currency_in_its_place()
    {
        var noLocal = SalesCustomerRiskCalculator.Evaluate(null, Local, Taxed(1m), Taxed(), Taxed(), Taxed(), Limit(10m));
        Assert.Null(noLocal.InLocalCurrency);
        Assert.Equal(1m, noLocal.InLimitCurrency!.GrossRiskWithThisOrder!.Value.Amount);
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.Check, SalesRiskReasonCodes.LocalCurrencyUnavailable)], noLocal.Reasons);
        Assert.Equal(SalesRiskOutcome.Unavailable, noLocal.Outcome);

        var noLimitScale = SalesCustomerRiskCalculator.Evaluate(Local, null, Taxed(1m), Taxed(), Taxed(), Taxed(), Limit(10m, Usd));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.CreditLimit, SalesRiskReasonCodes.LimitCurrencyUnavailable)], noLimitScale.Reasons);
        Assert.Null(noLimitScale.InLimitCurrency);
        // A limit-currency record that is not the limit's own currency is refused the same way.
        var wrong = SalesCustomerRiskCalculator.Evaluate(Local, Local, Taxed(1m), Taxed(), Taxed(), Taxed(), Limit(10m, Usd));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.CreditLimit, SalesRiskReasonCodes.LimitCurrencyUnavailable)], wrong.Reasons);
    }

    [Fact]
    public void More_decimals_than_the_scale_and_negative_amounts_are_refused_never_rounded()
    {
        var precise = Evaluate(Taxed(0.005m), Taxed(), Taxed(), Taxed(), Limit(1m));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.OpenReceivables, SalesRiskReasonCodes.AmountScaleExceeded)], precise.Reasons);
        Assert.Equal(SalesRiskOutcome.Unavailable, precise.Outcome);
        // Trailing zeros are not precision.
        Assert.Equal(SalesRiskOutcome.Within, Evaluate(Taxed(0.5000m), Taxed(), Taxed(), Taxed(), Limit(1.000m)).Outcome);

        var negative = Evaluate(Taxed(), Taxed(-1m), Taxed(), Taxed(), Limit(1m));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.ShippedNotInvoiced, SalesRiskReasonCodes.AmountInvalid)], negative.Reasons);
        var negativeLimit = Evaluate(Taxed(), Taxed(), Taxed(), Taxed(), Limit(-1m));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.CreditLimit, SalesRiskReasonCodes.AmountInvalid)], negativeLimit.Reasons);
        var nullAmount = Evaluate(SalesRiskInput.Available([null!], SalesRiskTaxBasis.IncludingTax), Taxed(), Taxed(), Taxed(), Limit(1m));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.OpenReceivables, SalesRiskReasonCodes.AmountInvalid)], nullAmount.Reasons);
    }

    [Fact]
    public void A_sum_beyond_an_exact_decimal_is_an_invalid_answer_not_a_server_error()
    {
        var evaluation = Evaluate(Taxed(decimal.MaxValue, 1m), Taxed(), Taxed(), Taxed(), Limit(1m));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.OpenReceivables, SalesRiskReasonCodes.AmountInvalid)], evaluation.Reasons);

        var across = Evaluate(Taxed(decimal.MaxValue), Taxed(1m), Taxed(), Taxed(), Limit(1m));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.Check, SalesRiskReasonCodes.AmountInvalid)], across.Reasons);
        Assert.Equal(SalesRiskOutcome.Unavailable, across.Outcome);
    }

    [Fact]
    public void Parts_on_different_tax_bases_are_never_added_and_the_mismatch_is_reported_even_beside_a_missing_part()
    {
        // SAT-D46: every part is VAT-included; a part on another basis (an owner's answer) is never added.
        var net = SalesRiskInput.Available([new SalesRiskAmount(Try, 5m)], SalesRiskTaxBasis.ExcludingTax);
        var mismatch = Evaluate(Taxed(10m), Taxed(), net, Taxed(), Limit(1000m));
        Assert.Equal(SalesRiskOutcome.Unavailable, mismatch.Outcome);
        Assert.Null(mismatch.InLocalCurrency!.GrossRisk);
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.Check, SalesRiskReasonCodes.TaxBasisMismatch)], mismatch.Reasons);

        // This order on another basis than the stages: no total with it, and the gross risk alone stays.
        var orderMismatch = Evaluate(Taxed(10m), Taxed(), Taxed(), net, Limit(1000m));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.Check, SalesRiskReasonCodes.TaxBasisMismatch)], orderMismatch.Reasons);
        Assert.Null(orderMismatch.InLocalCurrency!.GrossRiskWithThisOrder);

        // Today's shape: invoices are S5 (no source), the VAT-included remainder and this order wait for API-03, no limit.
        var today = Evaluate(Taxed(10m), SalesRiskInput.NoSource("invoicing_owner_missing"),
            SalesRiskInput.Unavailable("vat_inclusive_amount_unavailable"), SalesRiskInput.Unavailable("vat_inclusive_amount_unavailable"),
            SalesRiskInput.NoSource("limit_missing"), limitCurrency: null);
        Assert.Equal(
            [
                new SalesRiskReason(SalesRiskComponents.ShippedNotInvoiced, "invoicing_owner_missing"),
                new SalesRiskReason(SalesRiskComponents.ApprovedUnshippedRemainder, "vat_inclusive_amount_unavailable"),
                new SalesRiskReason(SalesRiskComponents.ThisOrder, "vat_inclusive_amount_unavailable"),
                new SalesRiskReason(SalesRiskComponents.CreditLimit, "limit_missing"),
            ],
            today.Reasons);
        Assert.Equal(SalesRiskOutcome.Unavailable, today.Outcome);
        Assert.Equal((Try, (Money?)null), (today.InLocalCurrency!.Currency.CurrencyId, today.InLocalCurrency.GrossRisk));

        var unmarked = Evaluate(SalesRiskInput.Available([new SalesRiskAmount(Try, 1m)], null), Taxed(), Taxed(), Taxed(), Limit(10m));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.OpenReceivables, SalesRiskReasonCodes.TaxBasisMissing)], unmarked.Reasons);
        Assert.Null(unmarked.InLocalCurrency!.GrossRisk);
    }

    [Fact]
    public void The_currencies_and_scales_are_bounded_and_the_parts_required()
    {
        Assert.Throws<ArgumentException>(() =>
            SalesCustomerRiskCalculator.Evaluate(new SalesRiskCurrency(Guid.Empty, Scale), Local, Taxed(), Taxed(), Taxed(), Taxed(), Limit(1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SalesCustomerRiskCalculator.Evaluate(Local, new SalesRiskCurrency(Try, -1), Taxed(), Taxed(), Taxed(), Taxed(), Limit(1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SalesCustomerRiskCalculator.Evaluate(new SalesRiskCurrency(Try, Money.MaximumScale + 1), Local, Taxed(), Taxed(), Taxed(), Taxed(), Limit(1m)));
        Assert.Throws<ArgumentNullException>(() => SalesCustomerRiskCalculator.Evaluate(Local, Local, null!, Taxed(), Taxed(), Taxed(), Limit(1m)));
        Assert.Throws<ArgumentNullException>(() => SalesCustomerRiskCalculator.Evaluate(Local, Local, Taxed(), Taxed(), Taxed(), null!, Limit(1m)));
    }
}
