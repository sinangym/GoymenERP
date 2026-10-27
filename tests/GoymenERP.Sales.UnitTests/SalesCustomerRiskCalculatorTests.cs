using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// MOD-01 / API-06: the customer risk calculator (DEC-FIN-007 gross risk against the credit limit). Every amount is SYNTHETIC test
/// data in one currency at scale 2; the limit is never a code value. Boundaries are exact (a gross risk equal to the limit is
/// within it; one cent more is over), and every unavailable path — a missing or unreadable part, another currency, more decimals
/// than the scale, a negative amount, a tax-basis mismatch, an undefined limit — is "Unavailable", never "Within".
/// </summary>
public sealed class SalesCustomerRiskCalculatorTests
{
    private static readonly Guid Try = Guid.Parse("c0000000-0000-4000-8000-000000000001");
    private static readonly Guid Usd = Guid.Parse("c0000000-0000-4000-8000-000000000002");
    private const int Scale = 2;

    private static SalesRiskInput Taxed(params decimal[] amounts) =>
        SalesRiskInput.Available([.. amounts.Select(amount => new SalesRiskAmount(Try, amount))], SalesRiskTaxBasis.IncludingTax);

    private static SalesRiskInput Limit(decimal amount, Guid? currency = null) =>
        SalesRiskInput.Available([new SalesRiskAmount(currency ?? Try, amount)], null);

    private static SalesRiskEvaluation Evaluate(SalesRiskInput receivables, SalesRiskInput shipped, SalesRiskInput remainder,
        SalesRiskInput limit) => SalesCustomerRiskCalculator.Evaluate(Try, Scale, receivables, shipped, remainder, limit);

    public static TheoryData<string, string, string, string, SalesRiskOutcome> Boundaries => new()
    {
        // receivables, shipped-not-invoiced, approved remainder, limit → outcome (gross = the three, never counted twice)
        { "400.00", "100.00", "500.00", "1000.00", SalesRiskOutcome.Within },
        { "400.00", "100.00", "500.01", "1000.00", SalesRiskOutcome.Over },
        { "400.00", "100.00", "499.99", "1000.00", SalesRiskOutcome.Within },
        { "0", "0", "0", "0", SalesRiskOutcome.Within },
        { "0", "0", "0.01", "0", SalesRiskOutcome.Over },
        { "999999.99", "0", "0", "1000000", SalesRiskOutcome.Within },
    };

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void Gross_risk_is_the_three_stages_and_only_exceeding_the_limit_is_over(string receivables, string shipped,
        string remainder, string limit, SalesRiskOutcome expected)
    {
        var evaluation = Evaluate(Taxed(decimal.Parse(receivables, System.Globalization.CultureInfo.InvariantCulture)),
            Taxed(decimal.Parse(shipped, System.Globalization.CultureInfo.InvariantCulture)),
            Taxed(decimal.Parse(remainder, System.Globalization.CultureInfo.InvariantCulture)),
            Limit(decimal.Parse(limit, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(expected, evaluation.Outcome);
        Assert.Empty(evaluation.Reasons);
        var gross = Assert.NotNull(evaluation.GrossRisk);
        Assert.Equal((Try, Scale), (gross.CurrencyId, gross.Scale));
        Assert.Equal(decimal.Parse(receivables, System.Globalization.CultureInfo.InvariantCulture)
            + decimal.Parse(shipped, System.Globalization.CultureInfo.InvariantCulture)
            + decimal.Parse(remainder, System.Globalization.CultureInfo.InvariantCulture), gross.Amount);
        Assert.Equal(decimal.Parse(limit, System.Globalization.CultureInfo.InvariantCulture), Assert.NotNull(evaluation.CreditLimit).Amount);
    }

    [Fact]
    public void Several_amounts_of_one_stage_are_added_exactly_and_an_empty_stage_is_a_recorded_zero()
    {
        var evaluation = Evaluate(Taxed(0.10m, 0.20m), Taxed(), Taxed(1000.5m), Limit(1000.80m));

        Assert.Equal((SalesRiskOutcome.Within, 1000.80m), (evaluation.Outcome, evaluation.GrossRisk!.Value.Amount));
        Assert.Equal(SalesRiskOutcome.Over, Evaluate(Taxed(0.10m, 0.20m), Taxed(), Taxed(1000.51m), Limit(1000.80m)).Outcome);
    }

    public static TheoryData<int> EachExposure => new() { 0, 1, 2 };

    [Theory]
    [MemberData(nameof(EachExposure))]
    public void A_part_without_a_source_or_an_unreadable_part_is_unavailable_never_within(int missing)
    {
        foreach (var input in new[] { SalesRiskInput.NoSource("owner_missing"), SalesRiskInput.Unavailable("owner_failed") })
        {
            var parts = new[] { Taxed(0m), Taxed(0m), Taxed(0m) };
            parts[missing] = input;

            // Even a zero exposure against a large limit: a missing part is never assumed zero.
            var evaluation = Evaluate(parts[0], parts[1], parts[2], Limit(1_000_000m));

            Assert.Equal(SalesRiskOutcome.Unavailable, evaluation.Outcome);
            Assert.Null(evaluation.GrossRisk);
            var component = new[]
            {
                SalesRiskComponents.OpenReceivables, SalesRiskComponents.ShippedNotInvoiced, SalesRiskComponents.ApprovedUnshippedRemainder,
            }[missing];
            Assert.Equal([new SalesRiskReason(component, input.ReasonCode!)], evaluation.Reasons);
            // The limit itself answered: it is still reported.
            Assert.Equal(1_000_000m, evaluation.CreditLimit!.Value.Amount);
        }
    }

    [Fact]
    public void A_missing_or_unreadable_limit_is_unavailable_while_the_gross_risk_is_still_computed()
    {
        var noLimit = Evaluate(Taxed(1m), Taxed(2m), Taxed(3m), SalesRiskInput.NoSource("credit_limit_owner_missing"));
        Assert.Equal((SalesRiskOutcome.Unavailable, 6m), (noLimit.Outcome, noLimit.GrossRisk!.Value.Amount));
        Assert.Null(noLimit.CreditLimit);
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.CreditLimit, "credit_limit_owner_missing")], noLimit.Reasons);

        var unreadable = Evaluate(Taxed(1m), Taxed(2m), Taxed(3m), new SalesRiskInput(SalesRiskInputState.Unavailable, [], null, null));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.CreditLimit, SalesRiskReasonCodes.Unavailable)], unreadable.Reasons);
        var withoutReason = Evaluate(new SalesRiskInput(SalesRiskInputState.NoSource, [], null, null), Taxed(), Taxed(), Limit(1m));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.OpenReceivables, SalesRiskReasonCodes.NoSource)], withoutReason.Reasons);
        // An unknown state is an unreadable answer: fail-closed.
        var unknown = Evaluate(new SalesRiskInput((SalesRiskInputState)99, [], SalesRiskTaxBasis.IncludingTax, null), Taxed(), Taxed(), Limit(1m));
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
            var evaluation = Evaluate(Taxed(1m), Taxed(), Taxed(), limit);
            Assert.Equal(SalesRiskOutcome.Unavailable, evaluation.Outcome);
            Assert.Equal([new SalesRiskReason(SalesRiskComponents.CreditLimit, SalesRiskReasonCodes.CreditLimitNotDefined)], evaluation.Reasons);
            Assert.Equal(1m, evaluation.GrossRisk!.Value.Amount);
        }
    }

    [Fact]
    public void Another_currency_is_refused_without_a_rate_decision_and_nothing_is_converted_or_added()
    {
        // A receivable in USD beside TRY: no exchange-rate decision exists for the risk check (NEEDS-SINAN).
        var mixed = SalesRiskInput.Available([new SalesRiskAmount(Try, 10m), new SalesRiskAmount(Usd, 1m)], SalesRiskTaxBasis.IncludingTax);
        var evaluation = Evaluate(mixed, Taxed(), Taxed(), Limit(1000m));
        Assert.Equal(SalesRiskOutcome.Unavailable, evaluation.Outcome);
        Assert.Null(evaluation.GrossRisk);
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.OpenReceivables, SalesRiskReasonCodes.CurrencyMismatch)], evaluation.Reasons);

        // A limit in another currency: the gross risk stands, the comparison does not.
        var foreignLimit = Evaluate(Taxed(1m), Taxed(), Taxed(), Limit(1000m, Usd));
        Assert.Equal((SalesRiskOutcome.Unavailable, 1m), (foreignLimit.Outcome, foreignLimit.GrossRisk!.Value.Amount));
        Assert.Null(foreignLimit.CreditLimit);
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.CreditLimit, SalesRiskReasonCodes.CurrencyMismatch)], foreignLimit.Reasons);
    }

    [Fact]
    public void More_decimals_than_the_scale_and_negative_amounts_are_refused_never_rounded()
    {
        var precise = Evaluate(Taxed(0.005m), Taxed(), Taxed(), Limit(1m));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.OpenReceivables, SalesRiskReasonCodes.AmountScaleExceeded)], precise.Reasons);
        Assert.Equal(SalesRiskOutcome.Unavailable, precise.Outcome);
        // Trailing zeros are not precision.
        Assert.Equal(SalesRiskOutcome.Within, Evaluate(Taxed(0.5000m), Taxed(), Taxed(), Limit(1.000m)).Outcome);

        var negative = Evaluate(Taxed(), Taxed(-1m), Taxed(), Limit(1m));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.ShippedNotInvoiced, SalesRiskReasonCodes.AmountInvalid)], negative.Reasons);
        var negativeLimit = Evaluate(Taxed(), Taxed(), Taxed(), Limit(-1m));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.CreditLimit, SalesRiskReasonCodes.AmountInvalid)], negativeLimit.Reasons);
        var nullAmount = Evaluate(SalesRiskInput.Available([null!], SalesRiskTaxBasis.IncludingTax), Taxed(), Taxed(), Limit(1m));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.OpenReceivables, SalesRiskReasonCodes.AmountInvalid)], nullAmount.Reasons);
    }

    [Fact]
    public void A_sum_beyond_an_exact_decimal_is_an_invalid_answer_not_a_server_error()
    {
        var evaluation = Evaluate(Taxed(decimal.MaxValue, 1m), Taxed(), Taxed(), Limit(1m));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.OpenReceivables, SalesRiskReasonCodes.AmountInvalid)], evaluation.Reasons);

        var across = Evaluate(Taxed(decimal.MaxValue), Taxed(1m), Taxed(), Limit(1m));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.Check, SalesRiskReasonCodes.AmountInvalid)], across.Reasons);
        Assert.Equal(SalesRiskOutcome.Unavailable, across.Outcome);
    }

    [Fact]
    public void Parts_on_different_tax_bases_are_never_added_and_the_mismatch_is_reported_even_beside_a_missing_part()
    {
        var net = SalesRiskInput.Available([new SalesRiskAmount(Try, 5m)], SalesRiskTaxBasis.ExcludingTax);
        var mismatch = Evaluate(Taxed(10m), Taxed(), net, Limit(1000m));
        Assert.Equal(SalesRiskOutcome.Unavailable, mismatch.Outcome);
        Assert.Null(mismatch.GrossRisk);
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.Check, SalesRiskReasonCodes.TaxBasisMismatch)], mismatch.Reasons);

        // Today's shape: invoices are S5 (no source), receivables include taxes, the order remainder does not — every reason listed.
        var today = Evaluate(Taxed(10m), SalesRiskInput.NoSource("invoicing_owner_missing"), net, SalesRiskInput.NoSource("limit_missing"));
        Assert.Equal(
            [
                new SalesRiskReason(SalesRiskComponents.ShippedNotInvoiced, "invoicing_owner_missing"),
                new SalesRiskReason(SalesRiskComponents.Check, SalesRiskReasonCodes.TaxBasisMismatch),
                new SalesRiskReason(SalesRiskComponents.CreditLimit, "limit_missing"),
            ],
            today.Reasons);
        Assert.Equal(SalesRiskOutcome.Unavailable, today.Outcome);

        // All on the net basis: comparable.
        var allNet = Evaluate(net, SalesRiskInput.Available([], SalesRiskTaxBasis.ExcludingTax), net, Limit(10m));
        Assert.Equal((SalesRiskOutcome.Within, 10m), (allNet.Outcome, allNet.GrossRisk!.Value.Amount));

        var unmarked = Evaluate(SalesRiskInput.Available([new SalesRiskAmount(Try, 1m)], null), Taxed(), Taxed(), Limit(10m));
        Assert.Equal([new SalesRiskReason(SalesRiskComponents.OpenReceivables, SalesRiskReasonCodes.TaxBasisMissing)], unmarked.Reasons);
        Assert.Null(unmarked.GrossRisk);
    }

    [Fact]
    public void The_check_currency_and_scale_are_required_and_bounded()
    {
        Assert.Throws<ArgumentException>(() => SalesCustomerRiskCalculator.Evaluate(Guid.Empty, Scale, Taxed(), Taxed(), Taxed(), Limit(1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SalesCustomerRiskCalculator.Evaluate(Try, -1, Taxed(), Taxed(), Taxed(), Limit(1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SalesCustomerRiskCalculator.Evaluate(Try, Money.MaximumScale + 1, Taxed(), Taxed(), Taxed(), Limit(1m)));
        Assert.Throws<ArgumentNullException>(() => SalesCustomerRiskCalculator.Evaluate(Try, Scale, null!, Taxed(), Taxed(), Limit(1m)));
    }
}
