using GoymenERP.Modules.Inventory.Domain.Costing;

namespace GoymenERP.Inventory.UnitTests;

public sealed class WeightedFirstReceiptExchangeRateCalculatorTests
{
    private static readonly Guid SourceCurrency = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TargetCurrency = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid OtherCurrency = Guid.Parse("00000000-0000-0000-0000-000000000003");

    [Fact]
    public void A_merge_weights_each_first_entry_rate_by_its_own_kilograms()
    {
        var result = Calculate(Input(100m, 30m), Input(200m, 45m));

        Ratio(result.QuantityKg, "300", "1");
        Ratio(result.Rate, "40", "1");
        Assert.Equal(SourceCurrency, result.SourceCurrencyId);
        Assert.Equal(TargetCurrency, result.TargetCurrencyId);
    }

    [Fact]
    public void A_nonterminating_weighted_rate_stays_an_exact_ratio_without_rounding()
    {
        var result = Calculate(Input(1m, 0.2m), Input(2m, 0.4m));

        Ratio(result.Rate, "1", "3");
        Assert.False(result.Rate.TryToDecimal(out _));
    }

    [Fact]
    public void Already_weighted_rational_rates_can_be_merged_without_a_scalar_intermediate()
    {
        var result = Calculate(Input(1m, Fraction(1m, 3m)), Input(1m, Fraction(2m, 3m)));

        Ratio(result.Rate, "1", "2");
    }

    [Fact]
    public void Rational_kilograms_remain_exact_in_both_the_weighted_numerator_and_total()
    {
        var result = Calculate(Input(Fraction(2m, 3m), ExactCostRatio.Of(10m)),
            Input(Fraction(1m, 3m), ExactCostRatio.Of(40m)));

        Ratio(result.QuantityKg, "1", "1");
        Ratio(result.Rate, "20", "1");
    }

    [Fact]
    public void A_single_input_preserves_its_exact_first_entry_rate()
    {
        var result = Calculate(Input(7m, Fraction(1m, 3m)));

        Ratio(result.QuantityKg, "7", "1");
        Ratio(result.Rate, "1", "3");
    }

    [Fact]
    public void Input_order_does_not_change_the_exact_result()
    {
        var inputs = new[] { Input(1m, Fraction(1m, 3m)), Input(2m, Fraction(2m, 7m)), Input(3m, 0.1m) };

        var forward = Calculate(inputs);
        var reverse = Calculate(inputs.Reverse().ToArray());

        Assert.Equal(0, forward.QuantityKg.CompareTo(reverse.QuantityKg));
        Assert.Equal(0, forward.Rate.CompareTo(reverse.Rate));
    }

    [Fact]
    public void Exact_intermediate_products_do_not_overflow_decimal_or_round_a_rate()
    {
        var result = Calculate(Input(decimal.MaxValue, decimal.MaxValue), Input(decimal.MaxValue, decimal.MaxValue));

        Assert.Equal(0, result.QuantityKg.CompareTo(ExactCostRatio.Of(decimal.MaxValue).Multiply(2m)));
        Assert.Equal(0, result.Rate.CompareTo(ExactCostRatio.Of(decimal.MaxValue)));
    }

    [Fact]
    public void One_unavailable_input_refuses_the_entire_merge_without_reusing_a_known_rate()
    {
        Refused([Input(5m, 30m), Input(1m, (ExactCostRatio?)null)],
            WeightedFirstReceiptExchangeRateCalculator.RateUnavailable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Different_directed_currency_pairs_are_not_blindly_averaged(bool changeSource)
    {
        var first = Input(1m, 30m);
        var other = Input(1m, 40m) with
        {
            SourceCurrencyId = changeSource ? OtherCurrency : SourceCurrency,
            TargetCurrencyId = changeSource ? TargetCurrency : OtherCurrency,
        };

        Refused([first, other], WeightedFirstReceiptExchangeRateCalculator.CurrencyPairMismatch);
    }

    [Fact]
    public void A_reverse_pair_is_not_inverted_or_averaged_into_the_requested_pair()
    {
        var reverse = Input(1m, 0.5m) with { SourceCurrencyId = TargetCurrency, TargetCurrencyId = SourceCurrency };

        Refused([Input(1m, 2m), reverse], WeightedFirstReceiptExchangeRateCalculator.CurrencyPairMismatch);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Nonpositive_kilograms_are_refused(int kilograms)
    {
        Refused([Input(kilograms, 30m)], WeightedFirstReceiptExchangeRateCalculator.InvalidInput);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Nonpositive_known_rates_are_refused(int rate)
    {
        Refused([Input(1m, rate)], WeightedFirstReceiptExchangeRateCalculator.InvalidInput);
    }

    [Theory]
    [InlineData("label")]
    [InlineData("source")]
    [InlineData("target")]
    [InlineData("same_currency")]
    [InlineData("quantity")]
    public void Malformed_owner_evidence_is_refused(string field)
    {
        var input = Input(1m, 30m);
        input = field switch
        {
            "label" => input with { LabelId = Guid.Empty },
            "source" => input with { SourceCurrencyId = Guid.Empty },
            "target" => input with { TargetCurrencyId = Guid.Empty },
            "same_currency" => input with { TargetCurrencyId = SourceCurrency },
            "quantity" => input with { QuantityKg = null! },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        Refused([input], WeightedFirstReceiptExchangeRateCalculator.InvalidInput);
    }

    [Fact]
    public void The_same_label_cannot_be_counted_twice_even_with_different_quantities()
    {
        var input = Input(1m, 30m);

        Refused([input, input with { QuantityKg = ExactCostRatio.Of(2m) }],
            WeightedFirstReceiptExchangeRateCalculator.InvalidInput);
    }

    [Fact]
    public void Empty_missing_and_null_row_inputs_are_refused()
    {
        Refused([], WeightedFirstReceiptExchangeRateCalculator.InvalidInput);
        Refused(null, WeightedFirstReceiptExchangeRateCalculator.InvalidInput);
        Refused([null!], WeightedFirstReceiptExchangeRateCalculator.InvalidInput);
    }

    [Fact]
    public void Oversized_input_is_bounded_before_it_can_produce_a_rate()
    {
        var input = Input(1m, 30m);
        var oversized = Enumerable.Repeat(input, WeightedFirstReceiptExchangeRateCalculator.MaximumInputs + 1);

        Refused(oversized, WeightedFirstReceiptExchangeRateCalculator.InputLimitExceeded);
    }

    private static WeightedFirstReceiptExchangeRateResult Calculate(params WeightedFirstReceiptExchangeRateInput[] inputs)
    {
        Assert.True(WeightedFirstReceiptExchangeRateCalculator.TryCalculate(inputs, out var result, out var reason), reason);
        Assert.Null(reason);
        return Assert.IsType<WeightedFirstReceiptExchangeRateResult>(result);
    }

    private static void Refused(IEnumerable<WeightedFirstReceiptExchangeRateInput>? inputs, string expectedReason)
    {
        Assert.False(WeightedFirstReceiptExchangeRateCalculator.TryCalculate(inputs, out var result, out var reason));
        Assert.Null(result);
        Assert.Equal(expectedReason, reason);
    }

    private static WeightedFirstReceiptExchangeRateInput Input(decimal quantity, decimal rate) =>
        Input(ExactCostRatio.Of(quantity), ExactCostRatio.Of(rate));

    private static WeightedFirstReceiptExchangeRateInput Input(decimal quantity, ExactCostRatio? rate) =>
        Input(ExactCostRatio.Of(quantity), rate);

    private static WeightedFirstReceiptExchangeRateInput Input(ExactCostRatio quantity, ExactCostRatio? rate) =>
        new(Guid.NewGuid(), quantity, rate, SourceCurrency, TargetCurrency);

    private static ExactCostRatio Fraction(decimal numerator, decimal denominator) => ExactCostRatio.Of(numerator).Divide(denominator);

    private static void Ratio(ExactCostRatio actual, string numerator, string denominator)
    {
        Assert.Equal(numerator, actual.Numerator);
        Assert.Equal(denominator, actual.Denominator);
    }
}
