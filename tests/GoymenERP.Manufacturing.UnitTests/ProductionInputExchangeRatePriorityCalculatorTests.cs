using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-SC36 golden of the one rate priority of a production input's foreign currency → TL conversion: the linked order's
/// rate (FNS-D73) at the kind and date the order names, a missing one completed from TCMB forex selling on the order date
/// (FNS-D83, URT-D105) → without an order the approved rate of the first stock-entry goods receipt (FNS-D77) →
/// "hesaplanamadı". FNS-D78: each input of a multi-input output is converted with its own rate and the output's converted
/// cost is their sum; one unreadable input makes it unreadable. The numbers are synthetic; the expected values are this
/// file's own arithmetic of those rules.
/// </summary>
public sealed class ProductionInputExchangeRatePriorityCalculatorTests
{
    private static readonly Guid IssueA = Guid.Parse("c0000000-0000-0000-0000-0000000036a1");
    private static readonly Guid IssueB = Guid.Parse("c0000000-0000-0000-0000-0000000036b2");
    private static readonly Guid IssueC = Guid.Parse("c0000000-0000-0000-0000-0000000036c3");
    private static readonly Guid CurrencyUsd = Guid.Parse("70000000-0000-0000-0000-00000000036a");
    private static readonly Guid CurrencyEur = Guid.Parse("70000000-0000-0000-0000-00000000036b");

    private static ProductionInputRateCandidatesV1 Foreign(Guid issue, Guid currency, decimal foreignCost,
        ProductionInputOrderRateV1 order, ProductionInputFirstReceiptRateV1 receipt) =>
        new(issue, currency, foreignCost, order, receipt);

    private static ProductionInputRateResolutionV1 Resolve(ProductionInputRateCandidatesV1 candidates) =>
        ProductionInputExchangeRatePriorityCalculatorV1.Resolve(candidates);

    [Fact]
    public void The_order_rate_wins_over_the_first_goods_receipt_rate()
    {
        var resolution = Resolve(Foreign(IssueA, CurrencyUsd, 0.8m,
            ProductionInputOrderRateV1.Rated(32.5m), ProductionInputFirstReceiptRateV1.Rated(30m)));

        Assert.Equal((ProductionInputRateSourceV1.OrderRate, (decimal?)32.5m, (string?)null),
            (resolution.Source, resolution.Rate, resolution.MissingReason));
        Assert.Equal(new IssueCostCurrencyInput(IssueCostCurrencyStatus.Converted, CurrencyUsd, 0.8m, 32.5m),
            resolution.ToCurrencyInput());
    }

    [Fact]
    public void Without_an_order_the_first_goods_receipt_rate_is_used()
    {
        var resolution = Resolve(Foreign(IssueA, CurrencyUsd, 0.8m,
            ProductionInputOrderRateV1.NoOrder, ProductionInputFirstReceiptRateV1.Rated(30m)));

        Assert.Equal((ProductionInputRateSourceV1.FirstGoodsReceiptRate, (decimal?)30m),
            (resolution.Source, resolution.Rate));
    }

    [Fact]
    public void An_order_whose_rate_cannot_be_read_refuses_the_conversion_and_never_falls_back_to_the_receipt_rate()
    {
        var resolution = Resolve(Foreign(IssueA, CurrencyUsd, 0.8m,
            ProductionInputOrderRateV1.Unavailable("purchase_order_exchange_rate_not_specified"),
            ProductionInputFirstReceiptRateV1.Rated(30m)));

        Assert.Equal((ProductionInputRateSourceV1.Missing, (decimal?)null, ProductionInputRateCodesV1.OrderRateUnavailable),
            (resolution.Source, resolution.Rate, resolution.MissingReason));
        Assert.Equal(new IssueCostCurrencyInput(IssueCostCurrencyStatus.RateMissing, CurrencyUsd, 0.8m, null),
            resolution.ToCurrencyInput());
    }

    [Theory]
    [InlineData(ProductionInputFirstReceiptRateStatusV1.Unavailable, ProductionInputRateCodesV1.FirstReceiptRateUnavailable)]
    [InlineData(ProductionInputFirstReceiptRateStatusV1.NotFound, ProductionInputRateCodesV1.FirstReceiptNotFound)]
    public void Without_an_order_and_without_a_readable_receipt_rate_the_conversion_is_not_computed(
        ProductionInputFirstReceiptRateStatusV1 receiptStatus, string expectedReason)
    {
        var receipt = receiptStatus == ProductionInputFirstReceiptRateStatusV1.Unavailable
            ? ProductionInputFirstReceiptRateV1.Unavailable
            : ProductionInputFirstReceiptRateV1.NotFound;

        var resolution = Resolve(Foreign(IssueA, CurrencyUsd, 0.8m, ProductionInputOrderRateV1.NoOrder, receipt));

        Assert.Equal((ProductionInputRateSourceV1.Missing, expectedReason), (resolution.Source, resolution.MissingReason));
    }

    [Fact]
    public void A_functional_currency_input_needs_no_rate_whatever_the_candidates_say()
    {
        var resolution = Resolve(new ProductionInputRateCandidatesV1(IssueA, null, null,
            ProductionInputOrderRateV1.Unavailable("purchase_order_unreadable"), ProductionInputFirstReceiptRateV1.NotFound));

        Assert.Equal(ProductionInputRateSourceV1.FunctionalCurrency, resolution.Source);
        Assert.Same(IssueCostCurrencyInput.FunctionalCurrency, resolution.ToCurrencyInput());
    }

    [Theory]
    [InlineData(0, 0.8, 32.5)]
    [InlineData(-1, 0.8, 32.5)]
    [InlineData(1, 0.8, 0)]
    [InlineData(1, 0.8, -1)]
    [InlineData(1, 0, 32.5)]
    [InlineData(1, -0.5, 32.5)]
    public void Malformed_candidates_are_never_given_a_rate(int identityKind, double foreignCost, double orderRate)
    {
        // identityKind: 1 = a valid issue and currency, 0 = an empty issue identity, -1 = an empty currency identity.
        var issue = identityKind == 0 ? Guid.Empty : IssueA;
        var currency = identityKind == -1 ? Guid.Empty : CurrencyUsd;

        var resolution = Resolve(Foreign(issue, currency, (decimal)foreignCost,
            ProductionInputOrderRateV1.Rated((decimal)orderRate), ProductionInputFirstReceiptRateV1.Rated(30m)));

        Assert.Equal((ProductionInputRateSourceV1.Missing, ProductionInputRateCodesV1.CandidateInvalid),
            (resolution.Source, resolution.MissingReason));
    }

    [Fact]
    public void A_rated_input_without_a_known_foreign_cost_is_not_converted()
    {
        var resolution = Resolve(new ProductionInputRateCandidatesV1(IssueA, CurrencyUsd, null,
            ProductionInputOrderRateV1.Rated(32.5m), ProductionInputFirstReceiptRateV1.NotFound));

        Assert.Equal((ProductionInputRateSourceV1.Missing, ProductionInputRateCodesV1.CandidateInvalid),
            (resolution.Source, resolution.MissingReason));
    }

    [Theory]
    [InlineData(null, null, ProductionInputRateKindV1.TcmbForexSelling, "2026-11-02")]
    [InlineData(ProductionInputRateKindV1.TcmbBanknoteBuying, null, ProductionInputRateKindV1.TcmbBanknoteBuying, "2026-11-02")]
    [InlineData(null, "2026-11-05", ProductionInputRateKindV1.TcmbForexSelling, "2026-11-05")]
    [InlineData(ProductionInputRateKindV1.TcmbForexBuying, "2026-11-05", ProductionInputRateKindV1.TcmbForexBuying, "2026-11-05")]
    public void The_order_rate_key_is_the_named_kind_and_date_each_completed_from_tcmb_forex_selling_on_the_order_date(
        ProductionInputRateKindV1? namedKind, string? namedDate, ProductionInputRateKindV1 expectedKind, string expectedDate)
    {
        var terms = new ProductionInputOrderRateTermsV1(new DateOnly(2026, 11, 2), namedKind,
            namedDate is null ? null : DateOnly.Parse(namedDate, System.Globalization.CultureInfo.InvariantCulture));

        var key = ProductionInputExchangeRatePriorityCalculatorV1.OrderRateKey(terms);

        Assert.Equal(new ProductionInputRateKeyV1(expectedKind,
            DateOnly.Parse(expectedDate, System.Globalization.CultureInfo.InvariantCulture)), key);
        Assert.Equal(ProductionInputRateKindV1.TcmbForexSelling, ProductionInputExchangeRatePriorityCalculatorV1.DefaultOrderRateKind);
    }

    [Fact]
    public void A_multi_input_output_converts_each_input_with_its_own_rate_and_sums_them_golden()
    {
        // A: 1 000 kg × 0.8 USD × 32.5 (order rate) = 26 000; B: 500 kg × 0.9 EUR × 35 (first receipt rate) = 15 750;
        // C: functional currency, no conversion. Output: 41 750.
        var a = Resolve(Foreign(IssueA, CurrencyUsd, 0.8m,
            ProductionInputOrderRateV1.Rated(32.5m), ProductionInputFirstReceiptRateV1.Rated(30m)));
        var b = Resolve(Foreign(IssueB, CurrencyEur, 0.9m,
            ProductionInputOrderRateV1.NoOrder, ProductionInputFirstReceiptRateV1.Rated(35m)));
        var c = Resolve(new ProductionInputRateCandidatesV1(IssueC, null, null,
            ProductionInputOrderRateV1.NoOrder, ProductionInputFirstReceiptRateV1.NotFound));

        var result = ProductionInputExchangeRatePriorityCalculatorV1.SumConvertedInputCost(
        [
            new(new Kilograms(500m), b),
            new(new Kilograms(250m), c),
            new(new Kilograms(1_000m), a),
        ]);

        Assert.Equal(ProductionOutputConvertedCostStatusV1.Computed, result.Status);
        Assert.Equal(41_750m, result.ConvertedTotal);
        Assert.Empty(result.InputReasons);
        Assert.Collection(result.Lines,
            line => Assert.Equal(new ProductionInputConvertedAmountV1(IssueA, ProductionInputRateSourceV1.OrderRate,
                1_000m, 800m, 32.5m, 26_000m), line),
            line => Assert.Equal(new ProductionInputConvertedAmountV1(IssueB,
                ProductionInputRateSourceV1.FirstGoodsReceiptRate, 500m, 450m, 35m, 15_750m), line));
    }

    [Fact]
    public void One_input_without_a_readable_rate_makes_the_whole_output_not_computed()
    {
        var a = Resolve(Foreign(IssueA, CurrencyUsd, 0.8m,
            ProductionInputOrderRateV1.Rated(32.5m), ProductionInputFirstReceiptRateV1.NotFound));
        var b = Resolve(Foreign(IssueB, CurrencyEur, 0.9m,
            ProductionInputOrderRateV1.NoOrder, ProductionInputFirstReceiptRateV1.NotFound));

        var result = ProductionInputExchangeRatePriorityCalculatorV1.SumConvertedInputCost(
            [new(new Kilograms(1_000m), a), new(new Kilograms(500m), b)]);

        Assert.Equal((ProductionOutputConvertedCostStatusV1.Unreadable, (decimal?)null),
            (result.Status, result.ConvertedTotal));
        Assert.Equal(ProductionInputRateCodesV1.FirstReceiptNotFound, Assert.Single(result.InputReasons).Value);
        Assert.Equal(IssueB, Assert.Single(result.InputReasons).Key);
    }

    [Fact]
    public void The_same_input_given_twice_is_not_summed()
    {
        var a = Resolve(Foreign(IssueA, CurrencyUsd, 0.8m,
            ProductionInputOrderRateV1.Rated(32.5m), ProductionInputFirstReceiptRateV1.NotFound));

        var result = ProductionInputExchangeRatePriorityCalculatorV1.SumConvertedInputCost(
            [new(new Kilograms(1_000m), a), new(new Kilograms(1_000m), a)]);

        Assert.Equal(ProductionOutputConvertedCostStatusV1.Unreadable, result.Status);
        Assert.Equal(ProductionInputRateCodesV1.DuplicateInput, result.InputReasons[IssueA]);
    }

    [Fact]
    public void A_rate_missing_input_reaches_the_receipt_cost_calculator_without_a_tl_cost()
    {
        var resolution = Resolve(Foreign(IssueA, CurrencyUsd, 0.8m,
            ProductionInputOrderRateV1.NoOrder, ProductionInputFirstReceiptRateV1.Unavailable));
        var stamped = new IssueCostInput(IssueA, new Kilograms(1_000m), IssueCostReadStatus.Value, 26m,
            IssueCostCurrencyInput.FunctionalCurrency);

        var applied = resolution.ApplyTo(stamped);

        Assert.Null(applied.UnitCostPerKg);
        Assert.Equal(new IssueCostCurrencyInput(IssueCostCurrencyStatus.RateMissing, CurrencyUsd, 0.8m, null),
            applied.Currency);
        Assert.Throws<ArgumentException>(() => resolution.ApplyTo(stamped with { IssueMovementId = IssueB }));
    }

    [Fact]
    public void A_rated_input_keeps_its_stamped_tl_cost_and_carries_the_chosen_rate()
    {
        var resolution = Resolve(Foreign(IssueA, CurrencyUsd, 0.8m,
            ProductionInputOrderRateV1.Rated(32.5m), ProductionInputFirstReceiptRateV1.NotFound));
        var stamped = new IssueCostInput(IssueA, new Kilograms(1_000m), IssueCostReadStatus.Value, 26m,
            IssueCostCurrencyInput.FunctionalCurrency);

        var applied = resolution.ApplyTo(stamped);

        Assert.Equal(26m, applied.UnitCostPerKg);
        Assert.Equal(new IssueCostCurrencyInput(IssueCostCurrencyStatus.Converted, CurrencyUsd, 0.8m, 32.5m),
            applied.Currency);
    }
}
