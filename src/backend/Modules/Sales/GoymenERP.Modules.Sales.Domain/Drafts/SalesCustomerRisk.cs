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

    /// <summary>Order-based figures before the totals calculator (API-03): net line amounts, taxes not included.</summary>
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
    /// An amount is in another currency than the check's (the order's). No exchange-rate decision exists for the risk check, so
    /// nothing is converted or added across currencies (NEEDS-SINAN).
    /// </summary>
    public const string CurrencyMismatch = "currency_mismatch";

    /// <summary>An amount carries more decimals than the check currency's approved money scale; it is never rounded to fit.</summary>
    public const string AmountScaleExceeded = "amount_scale_exceeded";

    /// <summary>A negative amount, or a sum that does not fit an exact decimal: a malformed answer.</summary>
    public const string AmountInvalid = "amount_invalid";

    /// <summary>An available exposure input without a tax basis: it cannot be added to another one.</summary>
    public const string TaxBasisMissing = "tax_basis_missing";

    /// <summary>
    /// The exposure inputs do not share one tax basis (invoice-based receivables include taxes; order remainders before the
    /// totals calculator do not). Which basis the gross risk uses is not decided (NEEDS-SINAN): nothing is added across bases.
    /// </summary>
    public const string TaxBasisMismatch = "tax_basis_mismatch";

    /// <summary>The credit-limit owner answered no single amount: no limit is not an unlimited (or a zero) limit.</summary>
    public const string CreditLimitNotDefined = "credit_limit_not_defined";
}

/// <summary>Why the check is not a plain "within" / "over": the part it is about and a stable code.</summary>
public sealed record SalesRiskReason(string Component, string Code);

/// <summary>
/// The calculator's answer. <paramref name="GrossRisk"/> is present only when the three exposure inputs are available, in the
/// check currency and on one tax basis; <paramref name="CreditLimit"/> only when the limit is one amount in the check currency.
/// </summary>
public sealed record SalesRiskEvaluation(
    SalesRiskOutcome Outcome,
    IReadOnlyList<SalesRiskReason> Reasons,
    Money? GrossRisk,
    Money? CreditLimit);

/// <summary>
/// MOD-01 / API-06: the ONE deterministic backend calculator of the customer risk check; the API, the screen (UI-15) and any
/// report read its answer and never recompute it.
/// <list type="bullet">
/// <item><b>DEC-FIN-007</b> (KILITLI): gross risk = open receivables + shipped-not-invoiced + approved-but-unshipped order
/// remainder. The callers feed each stage once (the remainder is ordered − shipped; the shipped part belongs to the second stage),
/// so no stage is counted twice.</item>
/// <item><b>Fail-closed</b>: an input that is <see cref="SalesRiskInputState.NoSource"/> or
/// <see cref="SalesRiskInputState.Unavailable"/> makes the outcome <see cref="SalesRiskOutcome.Unavailable"/> — never zero and
/// never "within". Every reason is listed, not only the first.</item>
/// <item><b>Money</b>: every amount becomes a <see cref="Money"/> in the check currency at the approved money scale the caller
/// passes (the order's Settings profile); a different currency is refused (no rate decision exists), more decimals than the scale
/// are refused (never rounded), and amounts on different tax bases are never added.</item>
/// <item><b>Boundary</b>: "over" means the gross risk EXCEEDS the limit (strictly greater); a gross risk equal to the limit is
/// within it.</item>
/// <item>Collateral, DBS and net risk are NOT inputs: they belong to the net-risk policy (DEC-FIN-007/008, OPEN-010) and stay out
/// of the gross risk and of this outcome.</item>
/// </list>
/// The warning at order time and the hard check at shipment (DEC-FIN-009) are their callers' (API-07, Logistics), not this read's.
/// </summary>
public static class SalesCustomerRiskCalculator
{
    public static SalesRiskEvaluation Evaluate(
        Guid currencyId,
        int scale,
        SalesRiskInput openReceivables,
        SalesRiskInput shippedNotInvoiced,
        SalesRiskInput approvedUnshippedRemainder,
        SalesRiskInput creditLimit)
    {
        ArgumentNullException.ThrowIfNull(openReceivables);
        ArgumentNullException.ThrowIfNull(shippedNotInvoiced);
        ArgumentNullException.ThrowIfNull(approvedUnshippedRemainder);
        ArgumentNullException.ThrowIfNull(creditLimit);
        if (currencyId == Guid.Empty)
        {
            throw new ArgumentException("Risk kontrolünün para birimi boş olamaz.", nameof(currencyId));
        }

        if (scale is < 0 or > Money.MaximumScale)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), $"Para ölçeği 0 ile {Money.MaximumScale} arasında olmalıdır.");
        }

        var reasons = new List<SalesRiskReason>();
        var exposures = new[]
        {
            (Component: SalesRiskComponents.OpenReceivables, Input: openReceivables),
            (Component: SalesRiskComponents.ShippedNotInvoiced, Input: shippedNotInvoiced),
            (Component: SalesRiskComponents.ApprovedUnshippedRemainder, Input: approvedUnshippedRemainder),
        };

        var sums = new List<(Money Sum, SalesRiskTaxBasis? Basis)>();
        foreach (var (component, input) in exposures)
        {
            if (Sum(component, input, currencyId, scale, reasons) is { } sum)
            {
                if (input.TaxBasis is null)
                {
                    reasons.Add(new SalesRiskReason(component, SalesRiskReasonCodes.TaxBasisMissing));
                    continue;
                }

                sums.Add((sum, input.TaxBasis));
            }
        }

        // Bases are compared among every part that answered, so the mismatch is reported even while another part is missing.
        var basesAgree = sums.Select(entry => entry.Basis).Distinct().Count() <= 1;
        if (!basesAgree)
        {
            reasons.Add(new SalesRiskReason(SalesRiskComponents.Check, SalesRiskReasonCodes.TaxBasisMismatch));
        }

        Money? gross = null;
        if (sums.Count == exposures.Length && basesAgree)
        {
            gross = Total(sums.Select(entry => entry.Sum), currencyId, scale, reasons);
        }

        var limit = Limit(creditLimit, currencyId, scale, reasons);
        if (gross is { } risk && limit is { } bound && reasons.Count == 0)
        {
            return new SalesRiskEvaluation(risk.IsGreaterThan(bound) ? SalesRiskOutcome.Over : SalesRiskOutcome.Within, [], risk, bound);
        }

        return new SalesRiskEvaluation(SalesRiskOutcome.Unavailable, reasons, gross, limit);
    }

    /// <summary>The input's amounts as one <see cref="Money"/> in the check currency, or null with its reasons.</summary>
    private static Money? Sum(string component, SalesRiskInput input, Guid currencyId, int scale, List<SalesRiskReason> reasons)
    {
        if (!Answered(component, input, reasons))
        {
            return null;
        }

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

    private static Money? Limit(SalesRiskInput input, Guid currencyId, int scale, List<SalesRiskReason> reasons)
    {
        const string component = SalesRiskComponents.CreditLimit;
        if (!Answered(component, input, reasons))
        {
            return null;
        }

        if (input.Amounts.Count != 1)
        {
            reasons.Add(new SalesRiskReason(component, SalesRiskReasonCodes.CreditLimitNotDefined));
            return null;
        }

        return ToMoney(component, input.Amounts[0], currencyId, scale, reasons);
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
            reasons.Add(new SalesRiskReason(component, SalesRiskReasonCodes.CurrencyMismatch));
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
