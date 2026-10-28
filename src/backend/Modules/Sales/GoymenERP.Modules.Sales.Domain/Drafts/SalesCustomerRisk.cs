using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>Whether the owner of one risk input answered it (MOD-01 / API-06).</summary>
public enum SalesRiskInputState
{
    /// <summary>The owner answered: the amounts are its recorded figure (an empty list is a recorded zero).</summary>
    Available = 1,

    /// <summary>The owner module or record of this input does not exist on main yet: never zero, never "within".</summary>
    NoSource = 2,

    /// <summary>The owner exists but is not composed, failed, or answered a malformed value: never zero, never "within".</summary>
    Unavailable = 3,
}

/// <summary>Whether the amounts of an exposure input include the taxes (KDV) of the documents they come from.</summary>
public enum SalesRiskTaxBasis
{
    /// <summary>Invoice-based figures: the installments a customer owes, taxes included.</summary>
    IncludingTax = 1,

    /// <summary>
    /// A figure without taxes. SAT-D46 puts the gross risk on the VAT-included basis, so such a figure is never added to it
    /// (an owner answering one is refused with <c>tax_basis_mismatch</c>); Sales itself never feeds one.
    /// </summary>
    ExcludingTax = 2,
}

/// <summary>The result of the read-only risk check (DEC-FIN-007 gross risk against the credit limit).</summary>
public enum SalesRiskOutcome
{
    /// <summary>Every input answered in one currency and tax basis, and the gross risk does not exceed the limit.</summary>
    Within = 1,

    /// <summary>Every input answered in one currency and tax basis, and the gross risk exceeds the limit.</summary>
    Over = 2,

    /// <summary>At least one input is missing, unreadable or not comparable: fail-closed, never "within".</summary>
    Unavailable = 3,
}

/// <summary>One amount of an input in one currency, exact (never rounded here).</summary>
public sealed record SalesRiskAmount(Guid CurrencyId, decimal Amount);

/// <summary>
/// One input of the risk check as its owner answered it. <paramref name="ReasonCode"/> is the owner's (or the reader's) stable
/// reason when the input is not <see cref="SalesRiskInputState.Available"/>; <paramref name="TaxBasis"/> is required for an
/// available exposure input and absent for the credit limit.
/// </summary>
public sealed record SalesRiskInput(
    SalesRiskInputState State,
    IReadOnlyList<SalesRiskAmount> Amounts,
    SalesRiskTaxBasis? TaxBasis,
    string? ReasonCode)
{
    public static SalesRiskInput Available(IReadOnlyList<SalesRiskAmount> amounts, SalesRiskTaxBasis? taxBasis) =>
        new(SalesRiskInputState.Available, amounts, taxBasis, null);

    public static SalesRiskInput NoSource(string reasonCode) => new(SalesRiskInputState.NoSource, [], null, reasonCode);

    public static SalesRiskInput Unavailable(string reasonCode) => new(SalesRiskInputState.Unavailable, [], null, reasonCode);
}

/// <summary>The stable names of the parts a reason is about (the wire's <c>component</c>).</summary>
public static class SalesRiskComponents
{
    public const string OpenReceivables = "OpenReceivables";
    public const string ShippedNotInvoiced = "ShippedNotInvoiced";
    public const string ApprovedUnshippedRemainder = "ApprovedUnshippedRemainder";
    public const string CreditLimit = "CreditLimit";

    /// <summary>SAT-D48: the order being checked, as far as it is not already in the approved remainder.</summary>
    public const string ThisOrder = "ThisOrder";

    /// <summary>A reason about the comparison of the parts with each other (currency, tax basis), not about one part.</summary>
    public const string Check = "Check";
}

/// <summary>The stable reason codes of <see cref="SalesCustomerRiskCalculator"/>.</summary>
public static class SalesRiskReasonCodes
{
    /// <summary>The input's owner gave no own reason for a missing source.</summary>
    public const string NoSource = "no_source";

    /// <summary>The input's owner gave no own reason for an unreadable answer.</summary>
    public const string Unavailable = "unavailable";

    /// <summary>
    /// SAT-D47: an amount is in another currency than the check's (the order's). It is converted only with the check day's TCMB
    /// selling rate from the Finance/Settings rate record; no such record exists on main, so it is refused — never converted
    /// with an invented rate and never added across currencies.
    /// </summary>
    public const string ExchangeRateUnavailable = "exchange_rate_unavailable";

    /// <summary>An amount carries more decimals than the check currency's approved money scale; it is never rounded to fit.</summary>
    public const string AmountScaleExceeded = "amount_scale_exceeded";

    /// <summary>A negative amount, or a sum that does not fit an exact decimal: a malformed answer.</summary>
    public const string AmountInvalid = "amount_invalid";

    /// <summary>An available exposure input without a tax basis: it cannot be added to another one.</summary>
    public const string TaxBasisMissing = "tax_basis_missing";

    /// <summary>
    /// The exposure inputs do not share one tax basis. SAT-D46 puts every part on the VAT-included basis; a part on another
    /// basis is never added (a guard: the Sales callers never feed a VAT-excluded amount).
    /// </summary>
    public const string TaxBasisMismatch = "tax_basis_mismatch";

    /// <summary>The credit-limit owner answered no single amount: no limit is not an unlimited (or a zero) limit.</summary>
    public const string CreditLimitNotDefined = "credit_limit_not_defined";

    /// <summary>SAT-D53: the credit limit's currency has no approved money scale here: the limit-currency totals are not made.</summary>
    public const string LimitCurrencyUnavailable = "limit_currency_unavailable";
}

/// <summary>Why the check is not a plain "within" / "over": the part it is about and a stable code.</summary>
public sealed record SalesRiskReason(string Component, string Code);

/// <summary>A currency a risk total is made in, at the approved money scale its owner (Settings) published for it.</summary>
public sealed record SalesRiskCurrency(Guid CurrencyId, int Scale);

/// <summary>
/// The risk totals in one currency: <paramref name="GrossRisk"/> (DEC-FIN-007, the three stages) and
/// <paramref name="GrossRiskWithThisOrder"/> (SAT-D48), each present only when every part it needs answered, on one tax basis,
/// in (or convertible to) that currency.
/// </summary>
public sealed record SalesRiskTotals(SalesRiskCurrency Currency, Money? GrossRisk, Money? GrossRiskWithThisOrder);

/// <summary>
/// The calculator's answer (SAT-D53): the totals in the company's LOCAL currency (TL; for accounting and reporting) and in the
/// CREDIT LIMIT's currency (the comparison, so the limit never drifts with a rate); the outcome is decided on the latter alone
/// (SAT-D80), and <paramref name="Reasons"/> are only the reasons of that decision. A totals record is null when its currency is
/// not known; a local amount that cannot be made (no rate) is null as information and never blocks the outcome.
/// <paramref name="CreditLimit"/> is present only when the limit is one amount.
/// </summary>
public sealed record SalesRiskEvaluation(
    SalesRiskOutcome Outcome,
    IReadOnlyList<SalesRiskReason> Reasons,
    SalesRiskTotals? InLocalCurrency,
    SalesRiskTotals? InLimitCurrency,
    Money? CreditLimit);

/// <summary>
/// MOD-01 / API-06: the ONE deterministic backend calculator of the customer risk check; the API, the screen (UI-15) and any
/// report read its answer and never recompute it.
/// <list type="bullet">
/// <item><b>DEC-FIN-007</b> (KILITLI): gross risk = open receivables + shipped-not-invoiced + approved-but-unshipped order
/// remainder. The callers feed each stage once (the remainder is ordered − shipped; the shipped part belongs to the second stage),
/// so no stage is counted twice.</item>
/// <item><b>SAT-D48</b>: the order-time check compares the gross risk PLUS this order's part (the order as far as it is not
/// already in the approved remainder) with the limit; both totals are answered.</item>
/// <item><b>SAT-D53</b>: the totals are made twice — in the company's local currency and in the credit limit's currency — and the
/// outcome is the limit currency's. The order's own currency is never the target.</item>
/// <item><b>SAT-D80</b>: the local (TL) total is information only. When it cannot be made (no rate, no TL scale) the decision is
/// still made in the limit's currency and the local amounts stay null; such a local-only reason is not a reason of the decision.
/// When the limit-currency total itself cannot be made, the outcome stays <see cref="SalesRiskOutcome.Unavailable"/>.</item>
/// <item><b>Fail-closed</b>: an input that is <see cref="SalesRiskInputState.NoSource"/> or
/// <see cref="SalesRiskInputState.Unavailable"/> makes the outcome <see cref="SalesRiskOutcome.Unavailable"/> — never zero and
/// never "within". Every reason is listed once, not only the first.</item>
/// <item><b>Money</b>: every amount becomes a <see cref="Money"/> in the target currency at the scale the caller passes (the
/// Settings profile). An amount in another currency needs the DEC-FIN-010 rate (SAT-D47: the previous business day's TCMB selling
/// rate in force on the check day; hour and holidays OPEN-012); no rate record exists on main, so it is refused
/// (<c>exchange_rate_unavailable</c>) — never converted with an invented rate. More decimals than the scale are refused (never
/// rounded); amounts on different tax bases are never added (SAT-D46: all VAT-included).</item>
/// <item><b>Boundary</b> (SAT-D49): "over" means the total EXCEEDS the limit (strictly greater); a total equal to the limit is
/// within it.</item>
/// <item>Collateral, DBS and net risk are NOT inputs: they belong to the net-risk policy (DEC-FIN-007/008, OPEN-010) and stay out
/// of the gross risk and of this outcome.</item>
/// </list>
/// The warning at order time and the hard check at shipment (DEC-FIN-009) are their callers' (API-07, Logistics), not this read's.
/// </summary>
public static class SalesCustomerRiskCalculator
{
    /// <param name="local">
    /// The company's local currency and its scale (SAT-D53); null when Settings could not answer it — then there is no local total,
    /// and the decision is still made in the limit's currency (SAT-D80).
    /// </param>
    /// <param name="limitCurrency">
    /// The credit limit's currency and its scale; null when there is no single limit amount or its currency's scale is unknown.
    /// </param>
    public static SalesRiskEvaluation Evaluate(
        SalesRiskCurrency? local,
        SalesRiskCurrency? limitCurrency,
        SalesRiskInput openReceivables,
        SalesRiskInput shippedNotInvoiced,
        SalesRiskInput approvedUnshippedRemainder,
        SalesRiskInput thisOrder,
        SalesRiskInput creditLimit)
    {
        ArgumentNullException.ThrowIfNull(openReceivables);
        ArgumentNullException.ThrowIfNull(shippedNotInvoiced);
        ArgumentNullException.ThrowIfNull(approvedUnshippedRemainder);
        ArgumentNullException.ThrowIfNull(thisOrder);
        ArgumentNullException.ThrowIfNull(creditLimit);
        RequireCurrency(local, nameof(local));
        RequireCurrency(limitCurrency, nameof(limitCurrency));

        var reasons = new List<SalesRiskReason>();
        var exposures = new[]
        {
            (Component: SalesRiskComponents.OpenReceivables, Input: openReceivables),
            (Component: SalesRiskComponents.ShippedNotInvoiced, Input: shippedNotInvoiced),
            (Component: SalesRiskComponents.ApprovedUnshippedRemainder, Input: approvedUnshippedRemainder),
        };
        var thisPart = (Component: SalesRiskComponents.ThisOrder, Input: thisOrder);

        // 1. Whether each part answered, on which tax basis and with well-formed amounts — once, whatever the target currency.
        var answered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (component, input) in exposures.Append(thisPart))
        {
            if (!Answered(component, input, reasons))
            {
                continue;
            }

            if (input.TaxBasis is null)
            {
                reasons.Add(new SalesRiskReason(component, SalesRiskReasonCodes.TaxBasisMissing));
                continue;
            }

            // A malformed amount is the answer's own fault, whatever the target currency: listed for the decision even when only
            // the (information-only) local total would have met it (SAT-D80).
            if (input.Amounts.Any(amount => amount is null || amount.Amount < 0m))
            {
                reasons.Add(new SalesRiskReason(component, SalesRiskReasonCodes.AmountInvalid));
                continue;
            }

            answered.Add(component);
        }

        var basesAgree = exposures.Append(thisPart)
            .Where(part => answered.Contains(part.Component))
            .Select(part => part.Input.TaxBasis)
            .Distinct()
            .Count() <= 1;
        if (!basesAgree)
        {
            reasons.Add(new SalesRiskReason(SalesRiskComponents.Check, SalesRiskReasonCodes.TaxBasisMismatch));
        }

        // 2. The totals in each target currency (SAT-D53). The reasons of a total go to the list given for it: the decision's own
        //    list for the limit currency, a discarded one for the local currency (SAT-D80: information only, never blocking).
        SalesRiskTotals Totals(SalesRiskCurrency target, List<SalesRiskReason> totalReasons)
        {
            var sums = new Dictionary<string, Money>(StringComparer.Ordinal);
            foreach (var (component, input) in exposures.Append(thisPart))
            {
                if (answered.Contains(component) && Sum(component, input, target.CurrencyId, target.Scale, totalReasons) is { } sum)
                {
                    sums[component] = sum;
                }
            }

            Money? gross = null;
            if (basesAgree && exposures.All(part => sums.ContainsKey(part.Component)))
            {
                gross = Total(exposures.Select(part => sums[part.Component]), target.CurrencyId, target.Scale, totalReasons);
            }

            Money? withThisOrder = gross is { } grossRisk && sums.TryGetValue(thisPart.Component, out var order)
                ? Total([grossRisk, order], target.CurrencyId, target.Scale, totalReasons)
                : null;
            return new SalesRiskTotals(target, gross, withThisOrder);
        }

        // SAT-D80: the local (TL) total is information only — whatever stops it (no TL scale, no rate, too many decimals) leaves
        // its amounts null and never reaches the decision's reasons.
        var localTotals = local is null ? null : Totals(local, []);

        // 3. The limit, in its own currency, and the totals in that currency.
        Money? limit = null;
        SalesRiskTotals? limitTotals = null;
        if (Answered(SalesRiskComponents.CreditLimit, creditLimit, reasons))
        {
            if (creditLimit.Amounts.Count != 1)
            {
                reasons.Add(new SalesRiskReason(SalesRiskComponents.CreditLimit, SalesRiskReasonCodes.CreditLimitNotDefined));
            }
            else if (limitCurrency is null || limitCurrency.CurrencyId != creditLimit.Amounts[0]?.CurrencyId)
            {
                reasons.Add(new SalesRiskReason(SalesRiskComponents.CreditLimit, SalesRiskReasonCodes.LimitCurrencyUnavailable));
            }
            else
            {
                limit = ToMoney(SalesRiskComponents.CreditLimit, creditLimit.Amounts[0], limitCurrency.CurrencyId,
                    limitCurrency.Scale, reasons);
                limitTotals = Totals(limitCurrency, reasons);
            }
        }

        // Fail-closed: a decision needs every part answered and the limit-currency total made; the local total plays no part.
        var distinct = reasons.Distinct().ToList();
        if (distinct.Count == 0 && limitTotals?.GrossRiskWithThisOrder is { } risk && limit is { } bound)
        {
            return new SalesRiskEvaluation(risk.IsGreaterThan(bound) ? SalesRiskOutcome.Over : SalesRiskOutcome.Within, [],
                localTotals, limitTotals, bound);
        }

        return new SalesRiskEvaluation(SalesRiskOutcome.Unavailable, distinct, localTotals, limitTotals, limit);
    }

    private static void RequireCurrency(SalesRiskCurrency? currency, string name)
    {
        if (currency is null)
        {
            return;
        }

        if (currency.CurrencyId == Guid.Empty)
        {
            throw new ArgumentException("Risk toplamının para birimi boş olamaz.", name);
        }

        if (currency.Scale is < 0 or > Money.MaximumScale)
        {
            throw new ArgumentOutOfRangeException(name, $"Para ölçeği 0 ile {Money.MaximumScale} arasında olmalıdır.");
        }
    }

    /// <summary>The input's amounts as one <see cref="Money"/> in the check currency, or null with its reasons.</summary>
    private static Money? Sum(string component, SalesRiskInput input, Guid currencyId, int scale, List<SalesRiskReason> reasons)
    {
        var amounts = new List<Money>(input.Amounts.Count);
        foreach (var amount in input.Amounts)
        {
            if (ToMoney(component, amount, currencyId, scale, reasons) is not { } money)
            {
                return null;
            }

            amounts.Add(money);
        }

        return Total(amounts, currencyId, scale, reasons, component);
    }

    private static bool Answered(string component, SalesRiskInput input, List<SalesRiskReason> reasons)
    {
        ArgumentNullException.ThrowIfNull(input.Amounts);
        switch (input.State)
        {
            case SalesRiskInputState.Available:
                return true;
            case SalesRiskInputState.NoSource:
                reasons.Add(new SalesRiskReason(component, input.ReasonCode ?? SalesRiskReasonCodes.NoSource));
                return false;
            default:
                // An unknown state is an unreadable answer too: fail-closed.
                reasons.Add(new SalesRiskReason(component, input.ReasonCode ?? SalesRiskReasonCodes.Unavailable));
                return false;
        }
    }

    private static Money? ToMoney(string component, SalesRiskAmount? amount, Guid currencyId, int scale, List<SalesRiskReason> reasons)
    {
        if (amount is null || amount.Amount < 0m)
        {
            reasons.Add(new SalesRiskReason(component, SalesRiskReasonCodes.AmountInvalid));
            return null;
        }

        if (amount.CurrencyId != currencyId)
        {
            reasons.Add(new SalesRiskReason(component, SalesRiskReasonCodes.ExchangeRateUnavailable));
            return null;
        }

        if (!Money.TryOf(amount.Amount, currencyId, scale, out var money))
        {
            reasons.Add(new SalesRiskReason(component, SalesRiskReasonCodes.AmountScaleExceeded));
            return null;
        }

        return money;
    }

    private static Money? Total(IEnumerable<Money> amounts, Guid currencyId, int scale, List<SalesRiskReason> reasons,
        string component = SalesRiskComponents.Check)
    {
        try
        {
            return amounts.Aggregate(Money.Zero(currencyId, scale), (sum, next) => sum.Add(next));
        }
        catch (OverflowException)
        {
            reasons.Add(new SalesRiskReason(component, SalesRiskReasonCodes.AmountInvalid));
            return null;
        }
    }
}
