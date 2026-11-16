using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Modules.Manufacturing.Domain.Costing;

/// <summary>
/// The TCMB rate kinds an order may name for a cost conversion (FNS-D73: the system never picks one of them on its own;
/// FNS-D83: the order names the kind and the date). Only <see cref="TcmbForexSelling"/> has a default role
/// (<see cref="ProductionInputExchangeRatePriorityCalculatorV1.DefaultOrderRateKind"/>).
/// </summary>
public enum ProductionInputRateKindV1
{
    /// <summary>TCMB daily forex selling rate (döviz satış), the kind URT-D105 and FNS-D83 name.</summary>
    TcmbForexSelling = 1,

    /// <summary>TCMB daily forex buying rate (döviz alış).</summary>
    TcmbForexBuying = 2,

    /// <summary>TCMB daily banknote selling rate (efektif satış).</summary>
    TcmbBanknoteSelling = 3,

    /// <summary>TCMB daily banknote buying rate (efektif alış).</summary>
    TcmbBanknoteBuying = 4,
}

/// <summary>The rate an order's conversion is read at: one TCMB rate kind on one rate date (FNS-D83).</summary>
public readonly record struct ProductionInputRateKeyV1(ProductionInputRateKindV1 Kind, DateOnly RateDate);

/// <summary>
/// What the linked order says about its conversion rate (FNS-D83): the order date, and the rate kind and rate date the
/// order names, each null when the order does not name it.
/// </summary>
public sealed record ProductionInputOrderRateTermsV1(
    DateOnly OrderDate,
    ProductionInputRateKindV1? RateKind,
    DateOnly? RateDate);

/// <summary>How the rate of the order an input is linked to was read.</summary>
public enum ProductionInputOrderRateStatusV1
{
    /// <summary>The input is linked to no purchase or sales order; the next step of the priority applies (FNS-D77).</summary>
    NoOrder = 1,

    /// <summary>The order's rate was read (FNS-D73, FNS-D83).</summary>
    Rated = 2,

    /// <summary>
    /// The input is linked to an order but its rate cannot be read (the order is not approved, its rate is not
    /// readable at its kind and date, or the stored order is unreadable). FNS-D73: the conversion is refused; another
    /// rate never stands in for the order's.
    /// </summary>
    Unavailable = 3,
}

/// <summary>The read of the order rate for one input; built only through its factories.</summary>
public sealed record ProductionInputOrderRateV1
{
    private ProductionInputOrderRateV1(ProductionInputOrderRateStatusV1 status, decimal? rate, string? unavailableReason)
    {
        Status = status;
        Rate = rate;
        UnavailableReason = unavailableReason;
    }

    public ProductionInputOrderRateStatusV1 Status { get; }

    /// <summary>Set only for <see cref="ProductionInputOrderRateStatusV1.Rated"/>: one foreign unit in TL.</summary>
    public decimal? Rate { get; }

    /// <summary>The order owner's reason code, set only for <see cref="ProductionInputOrderRateStatusV1.Unavailable"/>.</summary>
    public string? UnavailableReason { get; }

    public static ProductionInputOrderRateV1 NoOrder { get; } = new(ProductionInputOrderRateStatusV1.NoOrder, null, null);

    public static ProductionInputOrderRateV1 Rated(decimal rate) => new(ProductionInputOrderRateStatusV1.Rated, rate, null);

    public static ProductionInputOrderRateV1 Unavailable(string? reason) =>
        new(ProductionInputOrderRateStatusV1.Unavailable, null, reason);
}

/// <summary>How the approved rate stored on the input's first stock-entry goods receipt was read (FNS-D77).</summary>
public enum ProductionInputFirstReceiptRateStatusV1
{
    /// <summary>The goods receipt the material first entered stock with stores an approved rate.</summary>
    Rated = 1,

    /// <summary>That goods receipt exists but its approved rate cannot be read.</summary>
    Unavailable = 2,

    /// <summary>No first stock-entry goods receipt is known for the input.</summary>
    NotFound = 3,
}

/// <summary>The read of the first goods-receipt rate for one input; built only through its factories.</summary>
public sealed record ProductionInputFirstReceiptRateV1
{
    private ProductionInputFirstReceiptRateV1(ProductionInputFirstReceiptRateStatusV1 status, decimal? rate)
    {
        Status = status;
        Rate = rate;
    }

    public ProductionInputFirstReceiptRateStatusV1 Status { get; }

    /// <summary>Set only for <see cref="ProductionInputFirstReceiptRateStatusV1.Rated"/>: one foreign unit in TL.</summary>
    public decimal? Rate { get; }

    public static ProductionInputFirstReceiptRateV1 Unavailable { get; } =
        new(ProductionInputFirstReceiptRateStatusV1.Unavailable, null);

    public static ProductionInputFirstReceiptRateV1 NotFound { get; } =
        new(ProductionInputFirstReceiptRateStatusV1.NotFound, null);

    public static ProductionInputFirstReceiptRateV1 Rated(decimal rate) =>
        new(ProductionInputFirstReceiptRateStatusV1.Rated, rate);
}

/// <summary>
/// The rate candidates of one production input (one issue movement). <see cref="CurrencyId"/> is the input's foreign
/// currency, null when the input is carried in the functional currency and needs no conversion.
/// <see cref="ForeignUnitCostPerKg"/> is its foreign cost per kg when known. Each input carries its own candidates:
/// FNS-D78 converts every input with its own rate, never with another input's.
/// </summary>
public sealed record ProductionInputRateCandidatesV1(
    Guid IssueMovementId,
    Guid? CurrencyId,
    decimal? ForeignUnitCostPerKg,
    ProductionInputOrderRateV1 OrderRate,
    ProductionInputFirstReceiptRateV1 FirstReceiptRate);

/// <summary>Which step of the priority gave an input its rate.</summary>
public enum ProductionInputRateSourceV1
{
    /// <summary>The input is in the functional currency; no rate is needed.</summary>
    FunctionalCurrency = 1,

    /// <summary>The rate of the order the input is linked to (FNS-D73, FNS-D83).</summary>
    OrderRate = 2,

    /// <summary>The approved rate of the input's first stock-entry goods receipt (FNS-D77, FNS-D78).</summary>
    FirstGoodsReceiptRate = 3,

    /// <summary>No step gave a rate: the conversion is "hesaplanamadı" (fail-closed).</summary>
    Missing = 4,
}

/// <summary>The machine-readable reasons of a <see cref="ProductionInputRateSourceV1.Missing"/> resolution.</summary>
public static class ProductionInputRateCodesV1
{
    /// <summary>The input is linked to an order whose rate cannot be read (FNS-D73: no other rate stands in).</summary>
    public const string OrderRateUnavailable = "input_order_rate_unavailable";

    /// <summary>No order; the first stock-entry goods receipt exists but its approved rate cannot be read (FNS-D77).</summary>
    public const string FirstReceiptRateUnavailable = "input_first_receipt_rate_unavailable";

    /// <summary>No order and no first stock-entry goods receipt is known for the input (FNS-D77).</summary>
    public const string FirstReceiptNotFound = "input_first_receipt_not_found";

    /// <summary>The candidates themselves are malformed (an empty identity, a rate or cost of zero or below, an undefined status).</summary>
    public const string CandidateInvalid = "input_rate_candidate_invalid";

    /// <summary>The same issue movement was given twice to <see cref="ProductionInputExchangeRatePriorityCalculatorV1.SumConvertedInputCost"/>.</summary>
    public const string DuplicateInput = "input_rate_duplicate_input";

    /// <summary>A converted amount does not fit System.Decimal exactly.</summary>
    public const string ConversionOverflow = "input_rate_conversion_overflow";
}

/// <summary>
/// The rate one input is converted with and where it came from. <see cref="Rate"/> is set for
/// <see cref="ProductionInputRateSourceV1.OrderRate"/> and <see cref="ProductionInputRateSourceV1.FirstGoodsReceiptRate"/>
/// only; <see cref="MissingReason"/> for <see cref="ProductionInputRateSourceV1.Missing"/> only.
/// </summary>
public sealed record ProductionInputRateResolutionV1(
    Guid IssueMovementId,
    ProductionInputRateSourceV1 Source,
    Guid? CurrencyId,
    decimal? ForeignUnitCostPerKg,
    decimal? Rate,
    string? MissingReason)
{
    /// <summary>
    /// The currency input <see cref="ProductionReceiptCostCalculatorV1"/> reads: functional currency, converted with the
    /// chosen rate, or rate missing (the cost stays "hesaplanamadı", <see cref="ProductionReceiptCostCodesV1.InputExchangeRateMissing"/>).
    /// </summary>
    public IssueCostCurrencyInput ToCurrencyInput() => Source switch
    {
        ProductionInputRateSourceV1.FunctionalCurrency => IssueCostCurrencyInput.FunctionalCurrency,
        ProductionInputRateSourceV1.OrderRate or ProductionInputRateSourceV1.FirstGoodsReceiptRate =>
            new IssueCostCurrencyInput(IssueCostCurrencyStatus.Converted, CurrencyId, ForeignUnitCostPerKg, Rate),
        _ => new IssueCostCurrencyInput(IssueCostCurrencyStatus.RateMissing, CurrencyId, ForeignUnitCostPerKg, null),
    };

    /// <summary>
    /// The issue cost input with this resolution's currency. A missing rate also drops the TL unit cost: no rate, no TL
    /// cost (URT-D55 (2)), so the receipt cost is "hesaplanamadı" and never valued at a cost converted without a rate.
    /// </summary>
    /// <exception cref="ArgumentException">The issue is another movement's.</exception>
    public IssueCostInput ApplyTo(IssueCostInput issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        if (issue.IssueMovementId != IssueMovementId)
        {
            throw new ArgumentException("Kur çözümü başka bir girdi çıkışına ait.", nameof(issue));
        }

        return Source == ProductionInputRateSourceV1.Missing
            ? issue with { UnitCostPerKg = null, Currency = ToCurrencyInput() }
            : issue with { Currency = ToCurrencyInput() };
    }
}

/// <summary>One converted input of an output: its kept kg and the rate resolution it is converted with (FNS-D78).</summary>
public sealed record ProductionInputConversionLineV1(Kilograms KeptMass, ProductionInputRateResolutionV1 Resolution);

/// <summary>One input's converted amount: kept kg × its own foreign cost per kg × its own rate (FNS-D78).</summary>
public sealed record ProductionInputConvertedAmountV1(
    Guid IssueMovementId,
    ProductionInputRateSourceV1 Source,
    decimal KeptKilograms,
    decimal ForeignAmount,
    decimal Rate,
    decimal ConvertedAmount);

/// <summary>Whether the converted cost of an output could be computed.</summary>
public enum ProductionOutputConvertedCostStatusV1
{
    Computed = 1,

    /// <summary>"Hesaplanamadı": at least one input has no readable rate or the lines are malformed (FNS-D78).</summary>
    Unreadable = 2,
}

/// <summary>
/// The converted cost of one output (FNS-D78): the sum of its foreign-currency inputs, each converted with its own rate.
/// <see cref="ConvertedTotal"/> is set only when <see cref="Status"/> is Computed; <see cref="InputReasons"/> names every
/// input that made it unreadable, ordered by issue movement.
/// </summary>
public sealed record ProductionOutputConvertedCostV1(
    ProductionOutputConvertedCostStatusV1 Status,
    decimal? ConvertedTotal,
    IReadOnlyList<ProductionInputConvertedAmountV1> Lines,
    IReadOnlyDictionary<Guid, string> InputReasons);

/// <summary>
/// URT-SC36: the one deterministic rate priority of a production input's foreign currency → TL cost conversion.
/// <list type="number">
/// <item>The input is linked to a purchase or sales order: the order's rate (FNS-D73) — the rate kind and rate date the
/// order names, a missing kind or date completed from the default, TCMB forex selling on the order date
/// (FNS-D83, <see cref="OrderRateKey"/>; URT-D105 the same kind). An order whose rate cannot be read refuses the
/// conversion; the next step never stands in for an order's rate.</item>
/// <item>No order: the approved rate stored on the goods receipt the material first entered stock with (FNS-D77).</item>
/// <item>Neither: "hesaplanamadı" (fail-closed). No daily, UTC, default or other input's rate is guessed.</item>
/// </list>
/// Where a supplier invoice rate exists for a receipt, SAT-D295/BS-1 stays the receipt's cost rate (FNS-D73, FNS-D83);
/// that is the receipt cost the input carries in, not a step of this priority. FNS-D78: every input of a multi-input
/// output is resolved on its own candidates and converted with its own rate; one unreadable input makes the output's
/// converted cost unreadable (<see cref="SumConvertedInputCost"/>), as one rate-missing issue makes the receipt cost
/// unreadable (<see cref="ProductionReceiptCostCalculatorV1"/>). Reading the candidates is the caller's (the order and
/// goods-receipt owners' ports); this type chooses no rate kind beyond the decided default, reads no rate table and
/// throws on no content: anything malformed is <see cref="ProductionInputRateCodesV1.CandidateInvalid"/>.
/// </summary>
public static class ProductionInputExchangeRatePriorityCalculatorV1
{
    public const int ContractVersion = 1;

    /// <summary>FNS-D83 with URT-D105: the kind an order rate is read at when the order names none.</summary>
    public const ProductionInputRateKindV1 DefaultOrderRateKind = ProductionInputRateKindV1.TcmbForexSelling;

    /// <summary>
    /// FNS-D83: the rate key of an order's conversion — the kind and date the order names; a missing kind is
    /// <see cref="DefaultOrderRateKind"/>, a missing date the order date (the reversible reading of FNS-D83 for an order
    /// that names only one of them). An undefined kind is kept as named; no rate source answers it, so the read is
    /// unavailable and the conversion refused.
    /// </summary>
    public static ProductionInputRateKeyV1 OrderRateKey(ProductionInputOrderRateTermsV1 terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        return new ProductionInputRateKeyV1(terms.RateKind ?? DefaultOrderRateKind, terms.RateDate ?? terms.OrderDate);
    }

    /// <summary>The rate of one input by the priority above.</summary>
    public static ProductionInputRateResolutionV1 Resolve(ProductionInputRateCandidatesV1 candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var id = candidates.IssueMovementId;
        if (id == Guid.Empty)
        {
            return Missing(candidates, ProductionInputRateCodesV1.CandidateInvalid);
        }

        if (candidates.CurrencyId is null)
        {
            return new ProductionInputRateResolutionV1(id, ProductionInputRateSourceV1.FunctionalCurrency, null, null, null, null);
        }

        if (candidates.CurrencyId == Guid.Empty || candidates.ForeignUnitCostPerKg is <= 0m
            || candidates.OrderRate is null || candidates.FirstReceiptRate is null)
        {
            return Missing(candidates, ProductionInputRateCodesV1.CandidateInvalid);
        }

        return candidates.OrderRate.Status switch
        {
            ProductionInputOrderRateStatusV1.Rated => Rated(candidates, ProductionInputRateSourceV1.OrderRate,
                candidates.OrderRate.Rate),
            ProductionInputOrderRateStatusV1.Unavailable => Missing(candidates, ProductionInputRateCodesV1.OrderRateUnavailable),
            ProductionInputOrderRateStatusV1.NoOrder => candidates.FirstReceiptRate.Status switch
            {
                ProductionInputFirstReceiptRateStatusV1.Rated => Rated(candidates,
                    ProductionInputRateSourceV1.FirstGoodsReceiptRate, candidates.FirstReceiptRate.Rate),
                ProductionInputFirstReceiptRateStatusV1.Unavailable => Missing(candidates,
                    ProductionInputRateCodesV1.FirstReceiptRateUnavailable),
                ProductionInputFirstReceiptRateStatusV1.NotFound => Missing(candidates,
                    ProductionInputRateCodesV1.FirstReceiptNotFound),
                _ => Missing(candidates, ProductionInputRateCodesV1.CandidateInvalid),
            },
            _ => Missing(candidates, ProductionInputRateCodesV1.CandidateInvalid),
        };
    }

    /// <summary>
    /// FNS-D78: the converted cost of one output — Σ kept kg × foreign cost per kg × rate over its foreign-currency inputs,
    /// each with its own resolution. Functional-currency inputs need no conversion and add nothing here (their TL cost is
    /// <see cref="ProductionReceiptCostCalculatorV1"/>'s). Exact: nothing is rounded; an amount System.Decimal cannot hold
    /// is unreadable. One missing rate, malformed line or repeated input makes the whole result unreadable; the result
    /// does not depend on the order of the lines.
    /// </summary>
    public static ProductionOutputConvertedCostV1 SumConvertedInputCost(IReadOnlyCollection<ProductionInputConversionLineV1> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var reasons = new SortedDictionary<Guid, string>();
        var converted = new SortedDictionary<Guid, ProductionInputConvertedAmountV1>();
        var seen = new HashSet<Guid>();
        foreach (var line in lines)
        {
            if (line?.Resolution is not { } resolution)
            {
                reasons[Guid.Empty] = ProductionInputRateCodesV1.CandidateInvalid;
                continue;
            }

            var id = resolution.IssueMovementId;
            if (!seen.Add(id))
            {
                reasons[id] = ProductionInputRateCodesV1.DuplicateInput;
                converted.Remove(id);
                continue;
            }

            switch (resolution.Source)
            {
                case ProductionInputRateSourceV1.FunctionalCurrency when resolution is { CurrencyId: null, Rate: null }:
                    break;
                case ProductionInputRateSourceV1.OrderRate or ProductionInputRateSourceV1.FirstGoodsReceiptRate
                    when resolution is { CurrencyId: { } currency, ForeignUnitCostPerKg: { } foreign, Rate: { } rate }
                         && currency != Guid.Empty && foreign > 0m && rate > 0m:
                    try
                    {
                        var foreignAmount = checked(line.KeptMass.Value * foreign);
                        converted[id] = new ProductionInputConvertedAmountV1(id, resolution.Source, line.KeptMass.Value,
                            foreignAmount, rate, checked(foreignAmount * rate));
                    }
                    catch (OverflowException)
                    {
                        reasons[id] = ProductionInputRateCodesV1.ConversionOverflow;
                    }

                    break;
                case ProductionInputRateSourceV1.Missing:
                    reasons[id] = string.IsNullOrEmpty(resolution.MissingReason)
                        ? ProductionInputRateCodesV1.CandidateInvalid
                        : resolution.MissingReason;
                    break;
                default:
                    reasons[id] = ProductionInputRateCodesV1.CandidateInvalid;
                    break;
            }
        }

        var orderedLines = converted.Values.ToArray();
        if (reasons.Count > 0)
        {
            return new ProductionOutputConvertedCostV1(ProductionOutputConvertedCostStatusV1.Unreadable, null, orderedLines,
                reasons);
        }

        try
        {
            var total = orderedLines.Aggregate(0m, (sum, entry) => checked(sum + entry.ConvertedAmount));
            return new ProductionOutputConvertedCostV1(ProductionOutputConvertedCostStatusV1.Computed, total, orderedLines,
                reasons);
        }
        catch (OverflowException)
        {
            return new ProductionOutputConvertedCostV1(ProductionOutputConvertedCostStatusV1.Unreadable, null, orderedLines,
                new SortedDictionary<Guid, string> { [Guid.Empty] = ProductionInputRateCodesV1.ConversionOverflow });
        }
    }

    private static ProductionInputRateResolutionV1 Rated(ProductionInputRateCandidatesV1 candidates,
        ProductionInputRateSourceV1 source, decimal? rate) =>
        rate is > 0m && candidates.ForeignUnitCostPerKg is not null
            ? new ProductionInputRateResolutionV1(candidates.IssueMovementId, source, candidates.CurrencyId,
                candidates.ForeignUnitCostPerKg, rate, null)
            : Missing(candidates, ProductionInputRateCodesV1.CandidateInvalid);

    private static ProductionInputRateResolutionV1 Missing(ProductionInputRateCandidatesV1 candidates, string reason) =>
        new(candidates.IssueMovementId, ProductionInputRateSourceV1.Missing, candidates.CurrencyId,
            candidates.ForeignUnitCostPerKg is > 0m ? candidates.ForeignUnitCostPerKg : null, null, reason);
}
