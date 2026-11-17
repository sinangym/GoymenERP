namespace GoymenERP.Modules.Inventory.Domain.Costing;

/// <summary>One whole input label's owner-proved kilograms and first-entry rate. A null rate means that the
/// first-entry rate is unavailable; neither another input's rate nor a daily rate may stand in for it.</summary>
public sealed record WeightedFirstReceiptExchangeRateInput(Guid LabelId, ExactCostRatio QuantityKg,
    ExactCostRatio? Rate, Guid SourceCurrencyId, Guid TargetCurrencyId);

/// <summary>The exact kg-weighted rate of one currency pair. The owner retains the input receipt and
/// transformation provenance; this arithmetic result neither chooses a rate source nor proves that lineage.</summary>
public sealed record WeightedFirstReceiptExchangeRateResult
{
    internal WeightedFirstReceiptExchangeRateResult(Guid sourceCurrencyId, Guid targetCurrencyId,
        ExactCostRatio quantityKg, ExactCostRatio rate)
    {
        SourceCurrencyId = sourceCurrencyId;
        TargetCurrencyId = targetCurrencyId;
        QuantityKg = quantityKg;
        Rate = rate;
    }

    public Guid SourceCurrencyId { get; }
    public Guid TargetCurrencyId { get; }
    public ExactCostRatio QuantityKg { get; }
    public ExactCostRatio Rate { get; }
}

/// <summary>
/// FNS-D104: a merged label's first-entry rate is sum(input kg * input first-entry rate) / sum(input kg).
/// Any unavailable input leaves the result unavailable. All inputs must name the same directed currency pair:
/// numbers for different currencies cannot be averaged. No rounding, inverse rate, cross rate, fallback,
/// Manufacturing inheritance or monetary valuation is introduced here. The caller proves whole-label physical
/// quantities and immutable first-entry sources; this pure calculator only preserves their exact arithmetic.
/// </summary>
public static class WeightedFirstReceiptExchangeRateCalculator
{
    public const int MaximumInputs = InventoryCostLineageShares.MaximumQuantityRows;
    public const string InvalidInput = "first_receipt_weighted_rate_invalid_input";
    public const string RateUnavailable = "first_receipt_weighted_rate_unavailable";
    public const string CurrencyPairMismatch = "first_receipt_weighted_rate_currency_pair_mismatch";
    public const string InputLimitExceeded = "first_receipt_weighted_rate_input_limit_exceeded";

    public static bool TryCalculate(IEnumerable<WeightedFirstReceiptExchangeRateInput>? inputs,
        out WeightedFirstReceiptExchangeRateResult? result, out string? reason)
    {
        result = null;
        reason = InvalidInput;
        if (inputs is null) return false;

        var rows = inputs.Take(MaximumInputs + 1).ToArray();
        if (rows.Length > MaximumInputs)
        {
            reason = InputLimitExceeded;
            return false;
        }

        if (rows.Length == 0) return false;

        var seen = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (row is null || row.LabelId == Guid.Empty || !seen.Add(row.LabelId)
                || row.QuantityKg is null || row.QuantityKg.Sign <= 0 || row.Rate?.Sign <= 0
                || row.SourceCurrencyId == Guid.Empty || row.TargetCurrencyId == Guid.Empty
                || row.SourceCurrencyId == row.TargetCurrencyId) return false;
        }

        var first = rows[0];
        if (rows.Any(row => row.SourceCurrencyId != first.SourceCurrencyId || row.TargetCurrencyId != first.TargetCurrencyId))
        {
            reason = CurrencyPairMismatch;
            return false;
        }

        if (rows.Any(row => row.Rate is null))
        {
            reason = RateUnavailable;
            return false;
        }

        var quantity = ExactCostRatio.Of(0m);
        var weightedRate = ExactCostRatio.Of(0m);
        foreach (var row in rows)
        {
            quantity = quantity.Add(row.QuantityKg);
            weightedRate = weightedRate.Add(row.QuantityKg.Multiply(row.Rate!));
        }

        result = new(first.SourceCurrencyId, first.TargetCurrencyId, quantity, weightedRate.Divide(quantity));
        reason = null;
        return true;
    }
}
