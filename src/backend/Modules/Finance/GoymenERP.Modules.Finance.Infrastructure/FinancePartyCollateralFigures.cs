using System.Globalization;
using System.Numerics;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// API-85b: the one deterministic calculator of the collateral figures, used by the collateral master's account view and
/// by the Sales risk port alike (one backend calculator; no screen recomputes it).
/// <list type="bullet">
/// <item><b>Weighted collateral (FNS-D31):</b> for every held received collateral in force on the read date, its amount x
/// the Settings rate of its type / 100, exact (no rounding), summed per currency (never converted). No collateral in
/// force is a recorded zero (an empty list). A catalog that cannot answer, a type it does not list exactly once, a type
/// marked a cheque/note class, a rate that is not exact decimal text within 0–100, or a stored amount that does not
/// parse makes the whole figure unavailable — never a partial sum and never an assumed rate. A type deactivated after
/// the collateral was recorded still answers its rate: the collateral itself is held until it is released.</item>
/// <item><b>Documentary risk (FNS-D58):</b> every customer instrument of the account whose status is neither Collected
/// nor ReturnedToCustomer, summed per currency; a status Finance does not know makes the figure unavailable.</item>
/// <item><b>Held collateral (F018 §L3, "mevcut teminat"):</b> the held received collaterals in force on the read date at
/// their recorded amount, summed per currency; a stored amount that does not parse makes it unavailable.</item>
/// <item><b>Collateral utilization (F018 §L3, FNS-D31 (4), "teminat doluluğu"):</b> the open receivables taken from the
/// weighted collateral — per currency the smaller of the two (a currency with nothing used is left out). FNS-D74: collateral
/// covers only receivables in its own currency; collateral in another currency is zero cover, never converted and never a
/// reason to withhold the figure, so a leftover in one currency beside an uncovered receivable in another is still a
/// complete per-currency answer. Either input unavailable makes it unavailable.</item>
/// </list>
/// </summary>
internal static class FinancePartyCollateralFigures
{
    public const string TypeRateUnavailable = "finance_collateral_type_rate_unavailable";
    public const string CollateralUnreadable = "finance_collateral_unreadable";
    public const string DocumentaryRiskStatusUnknown = "finance_documentary_risk_status_unknown";
    public const string DocumentaryRiskUnreadable = "finance_documentary_risk_unreadable";
    public const string DocumentaryRiskWithheld = "finance_documentary_risk_withheld";
    public const string UtilizationSourceUnavailable = "finance_collateral_utilization_source_unavailable";

    private const int RateTextMaximumLength = 64;
    private const int PercentScale = 2;
    private static readonly FinanceExactDecimalText HundredPercent = new(100, 0);

    internal sealed record HeldCollateral(Guid CollateralTypeId, Guid CurrencyId, string AmountText, DateOnly ValidFrom,
        DateOnly? ValidUntil);

    internal sealed record PortfolioInstrument(int Status, Guid CurrencyId, string AmountText);

    internal sealed record CurrencyAmount(Guid CurrencyId, string Amount);

    /// <summary>Available: <see cref="Amounts"/> is complete (empty is a recorded zero). Otherwise the reason.</summary>
    internal sealed record Figure(bool IsAvailable, IReadOnlyList<CurrencyAmount> Amounts, string? ReasonCode)
    {
        public static Figure Unavailable(string reasonCode) => new(false, [], reasonCode);
    }

    /// <summary>In force on the date: started on or before it and not ended before it (an open end never ends).</summary>
    internal static bool IsInForce(DateOnly validFrom, DateOnly? validUntil, DateOnly asOf) =>
        validFrom <= asOf && (validUntil is not { } until || until >= asOf);

    /// <summary>The Settings rate text as an exact percentage within 0–100, or nothing.</summary>
    internal static bool TryRate(string? ratePercent, out FinanceExactDecimalText rate)
    {
        rate = default;
        if (ratePercent is null || ratePercent.Length > RateTextMaximumLength
            || !FinanceExactDecimalText.TryParseNonNegative(ratePercent, out var parsed)
            || parsed.CompareTo(HundredPercent) > 0)
        {
            return false;
        }

        rate = parsed;
        return true;
    }

    internal static Figure Weigh(IReadOnlyList<HeldCollateral> held, FinanceCollateralTypeCatalogAnswer? catalog,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(held);
        var inForce = held.Where(collateral => IsInForce(collateral.ValidFrom, collateral.ValidUntil, asOf)).ToList();
        if (inForce.Count == 0)
        {
            return new Figure(true, [], null);
        }

        if (catalog is not { State: FinanceCollateralTypeCatalogState.Available, Items: { } items }
            || items.Any(item => item is null))
        {
            return Figure.Unavailable(TypeRateUnavailable);
        }

        var sums = new Dictionary<Guid, FinanceExactDecimalText>();
        foreach (var collateral in inForce)
        {
            var matches = items.Where(item => item.CollateralTypeId == collateral.CollateralTypeId).Take(2).ToList();
            if (matches.Count != 1 || matches[0].IsNegotiableInstrument || !TryRate(matches[0].RatePercent, out var rate))
            {
                return Figure.Unavailable(TypeRateUnavailable);
            }

            if (collateral.CurrencyId == Guid.Empty
                || !FinanceExactDecimalText.TryParsePositive(collateral.AmountText, out var amount))
            {
                return Figure.Unavailable(CollateralUnreadable);
            }

            var weighted = Percent(amount, rate);
            sums[collateral.CurrencyId] = sums.TryGetValue(collateral.CurrencyId, out var sum) ? sum.Add(weighted) : weighted;
        }

        return new Figure(true, Ordered(sums), null);
    }

    internal static Figure DocumentaryRisk(IReadOnlyList<PortfolioInstrument> instruments)
    {
        ArgumentNullException.ThrowIfNull(instruments);
        var sums = new Dictionary<Guid, FinanceExactDecimalText>();
        foreach (var instrument in instruments)
        {
            if (!Enum.IsDefined((CustomerInstrumentStatus)instrument.Status))
            {
                return Figure.Unavailable(DocumentaryRiskStatusUnknown);
            }

            // FNS-D58: only a collected or a returned instrument leaves the risk; one in the portfolio, at the bank but
            // not yet collected, bounced or protested stays in it.
            if ((CustomerInstrumentStatus)instrument.Status is CustomerInstrumentStatus.Collected
                or CustomerInstrumentStatus.ReturnedToCustomer)
            {
                continue;
            }

            if (instrument.CurrencyId == Guid.Empty
                || !FinanceExactDecimalText.TryParsePositive(instrument.AmountText, out var amount))
            {
                return Figure.Unavailable(DocumentaryRiskUnreadable);
            }

            sums[instrument.CurrencyId] = sums.TryGetValue(instrument.CurrencyId, out var sum) ? sum.Add(amount) : amount;
        }

        return new Figure(true, Ordered(sums), null);
    }

    /// <summary>F018 §L3 "mevcut teminat": the in-force held collaterals at their recorded amount, per currency.</summary>
    internal static Figure Gross(IReadOnlyList<HeldCollateral> held, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(held);
        var sums = new Dictionary<Guid, FinanceExactDecimalText>();
        foreach (var collateral in held.Where(collateral => IsInForce(collateral.ValidFrom, collateral.ValidUntil, asOf)))
        {
            if (collateral.CurrencyId == Guid.Empty
                || !FinanceExactDecimalText.TryParsePositive(collateral.AmountText, out var amount))
            {
                return Figure.Unavailable(CollateralUnreadable);
            }

            sums[collateral.CurrencyId] = sums.TryGetValue(collateral.CurrencyId, out var sum) ? sum.Add(amount) : amount;
        }

        return new Figure(true, Ordered(sums), null);
    }

    /// <summary>
    /// F018 §L3 "teminat doluluğu": per currency the smaller of the open receivables and the weighted collateral; a leftover
    /// in one currency beside an uncovered receivable in another is zero cover there (FNS-D74), not a withheld figure.
    /// </summary>
    internal static Figure Utilization(Figure openReceivables, Figure weighted)
    {
        ArgumentNullException.ThrowIfNull(openReceivables);
        ArgumentNullException.ThrowIfNull(weighted);
        if (!openReceivables.IsAvailable || !weighted.IsAvailable
            || !TryPerCurrency(openReceivables.Amounts, out var owed) || !TryPerCurrency(weighted.Amounts, out var held))
        {
            return Figure.Unavailable(UtilizationSourceUnavailable);
        }

        var zero = new FinanceExactDecimalText(BigInteger.Zero, 0);
        var used = new Dictionary<Guid, FinanceExactDecimalText>();
        foreach (var currencyId in owed.Keys.Union(held.Keys))
        {
            var open = owed.TryGetValue(currencyId, out var openAmount) ? openAmount : zero;
            var cover = held.TryGetValue(currencyId, out var coverAmount) ? coverAmount : zero;
            var comparison = open.CompareTo(cover);
            var smaller = comparison <= 0 ? open : cover;
            if (!smaller.IsZero)
            {
                used[currencyId] = smaller;
            }
        }

        return new Figure(true, Ordered(used), null);
    }

    private static bool TryPerCurrency(IReadOnlyList<CurrencyAmount> amounts,
        out Dictionary<Guid, FinanceExactDecimalText> perCurrency)
    {
        perCurrency = new Dictionary<Guid, FinanceExactDecimalText>();
        foreach (var amount in amounts)
        {
            if (amount is null || amount.CurrencyId == Guid.Empty || perCurrency.ContainsKey(amount.CurrencyId)
                || !FinanceExactDecimalText.TryParseNonNegative(amount.Amount, out var parsed))
            {
                return false;
            }

            perCurrency[amount.CurrencyId] = parsed;
        }

        return true;
    }

    /// <summary>The exact CLR decimal of an amount text, or nothing when it does not fit one exactly.</summary>
    internal static bool TryExactDecimal(string amount, out decimal value) =>
        decimal.TryParse(amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
        && FinanceExactDecimalText.Equal(amount, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>amount x rate / 100, exact: the unscaled values multiply and the scales add (plus two for the percent).</summary>
    internal static FinanceExactDecimalText Percent(FinanceExactDecimalText amount, FinanceExactDecimalText rate) =>
        Normalize(new FinanceExactDecimalText(amount.Unscaled * rate.Unscaled,
            checked(amount.Scale + rate.Scale + PercentScale)));

    private static FinanceExactDecimalText Normalize(FinanceExactDecimalText value)
    {
        var unscaled = value.Unscaled;
        var scale = value.Scale;
        if (unscaled.IsZero)
        {
            return new FinanceExactDecimalText(BigInteger.Zero, 0);
        }

        while (scale > 0 && (unscaled % 10).IsZero)
        {
            unscaled /= 10;
            scale--;
        }

        return new FinanceExactDecimalText(unscaled, scale);
    }

    private static IReadOnlyList<CurrencyAmount> Ordered(Dictionary<Guid, FinanceExactDecimalText> sums) =>
        [.. sums.OrderBy(pair => pair.Key).Select(pair => new CurrencyAmount(pair.Key, Normalize(pair.Value).Text))];
}
