using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Modules.Sales.Domain.CustomerReturns;

/// <summary>
/// The amounts of one customer return line in one currency (SAT-D292 (5)): the net (matrah), the VAT, the withholding (tevkifat) and
/// the line total, each proportioned on its own from the same amount of the original line. <see cref="Gross"/> and
/// <see cref="DerivedPayable"/> are derived (SAT-D225: net + VAT, net + VAT − withholding); <see cref="Drift"/> is the kuruş
/// difference between the proportioned line total and the derived payable (EK58 implementation detail: shown, never corrected).
/// </summary>
public sealed record SalesCustomerReturnAmounts(Money Net, Money Vat, Money Withholding, Money LineTotal)
{
    public Money Gross => Net.Add(Vat);

    public Money DerivedPayable => Gross.Subtract(Withholding);

    public Money Drift => LineTotal.Subtract(DerivedPayable);

    public Money Pick(SalesCustomerReturnAmountType type) => type switch
    {
        SalesCustomerReturnAmountType.Net => Net,
        SalesCustomerReturnAmountType.Vat => Vat,
        SalesCustomerReturnAmountType.Withholding => Withholding,
        SalesCustomerReturnAmountType.LineTotal => LineTotal,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>The exact sums of the parts, type by type (document totals; nothing is re-rounded).</summary>
    public static SalesCustomerReturnAmounts Sum(IEnumerable<SalesCustomerReturnAmounts> parts, Guid currencyId, int scale)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var zero = Money.Zero(currencyId, scale);
        var net = zero;
        var vat = zero;
        var withholding = zero;
        var lineTotal = zero;
        foreach (var part in parts)
        {
            net = net.Add(part.Net);
            vat = vat.Add(part.Vat);
            withholding = withholding.Add(part.Withholding);
            lineTotal = lineTotal.Add(part.LineTotal);
        }

        return new SalesCustomerReturnAmounts(net, vat, withholding, lineTotal);
    }

    /// <summary>The stored amounts of an original invoice line: the line total is the line's payable (matrah + KDV − tevkifat).</summary>
    public static SalesCustomerReturnAmounts Of(SalesInvoiceAmountSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return new SalesCustomerReturnAmounts(set.Net, set.Vat, set.Withholding, set.Payable);
    }
}

/// <summary>The amount types SAT-D292 (5) proportions separately (each also in TL for a foreign-currency invoice).</summary>
public enum SalesCustomerReturnAmountType
{
    Net = 1,
    Vat = 2,
    Withholding = 3,
    LineTotal = 4,
}

/// <summary>
/// The base of one original invoice line (SAT-D292 implementation detail): its invoiced quantity and its STORED amounts — the invoice
/// currency amounts and, for a foreign-currency invoice, the TL amounts after the SAT-D223 group difference. The SAT-D223 grouping is
/// never run again on a return.
/// </summary>
public sealed record SalesCustomerReturnOriginalLine(Guid LineId, decimal Quantity, SalesCustomerReturnAmounts Amounts,
    SalesCustomerReturnAmounts? Functional)
{
    public static SalesCustomerReturnOriginalLine Of(SalesInvoiceLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new SalesCustomerReturnOriginalLine(line.Id, line.Share.Quantity, SalesCustomerReturnAmounts.Of(line.Amounts),
            line.Functional is { } functional ? SalesCustomerReturnAmounts.Of(functional.Amounts) : null);
    }
}

/// <summary>One FINALISED earlier return of the same original line (SAT-D292 (6)): drafts, pending and approved returns never count.</summary>
public sealed record SalesCustomerReturnPrior(decimal Quantity, SalesCustomerReturnAmounts Amounts, SalesCustomerReturnAmounts? Functional,
    bool IsShipmentLinked);

/// <summary>
/// One computed return line: the quantity, whether it is the line's last return (it brings the remaining returnable quantity to zero,
/// SAT-D289 implementation detail), the amounts and the TL amounts, and <see cref="NeedsReview"/> when a last return's residue of
/// some amount type has the opposite sign of the original amount (the per-type rule is applied as decided and flagged, never refused).
/// </summary>
public sealed record SalesCustomerReturnLineResult(decimal Quantity, bool IsLastReturn, SalesCustomerReturnAmounts Amounts,
    SalesCustomerReturnAmounts? Functional, bool NeedsReview);

/// <summary>SAT-D241: the separate payable, VAT and withholding TL differences between the return and the official document.</summary>
public sealed record SalesCustomerReturnMatchEvaluation(decimal PayableDifference, decimal VatDifference, decimal WithholdingDifference)
{
    /// <summary>Any one of the three above the (inclusive) tolerance sends the difference to the approval (SAT-D89, SAT-D241).</summary>
    public bool Exceeds(decimal tolerance) =>
        PayableDifference > tolerance || VatDifference > tolerance || WithholdingDifference > tolerance;
}

/// <summary>
/// API-40 — the ONE deterministic calculator of a customer return line (AGENTS.md "Formula/UI/report aynı deterministic backend
/// calculator'ı kullanır"; SAT-D289, SAT-D292, SAT-D293 (3)).
/// <list type="number">
/// <item><b>Line based (SAT-D292 (1), (3)):</b> each return line is computed from its original invoice line only; the document totals
/// are the sums of the lines and carry no rounding difference of their own.</item>
/// <item><b>Per amount type (SAT-D292 (5)):</b> the line total, matrah, KDV and tevkifat — and, for a foreign-currency invoice, each of
/// their TL amounts — are proportioned separately: <c>original × quantity / original quantity</c>, rounded once with SAT-D11
/// (<see cref="Money.Round"/>, AwayFromZero, at the money profile's scale — never a code constant).</item>
/// <item><b>Last return (SAT-D292 (2)):</b> the return that brings the line's remaining returnable quantity to zero is not
/// proportioned: each type is the original amount minus the sum of the prior FINALISED returns of that type (SAT-D292 (6)), so the
/// line's kuruş residue closes on that line.</item>
/// <item><b>Base (EK58 implementation detail):</b> the stored original line amounts, TL after the SAT-D223 group difference; the
/// grouping is not run again.</item>
/// <item><b>Quantity (SAT-D293 (3)):</b> positive, at most the remaining returnable quantity (original minus the finalised returns).
/// The unit price, discount, VAT rate, withholding share and rate are the original line's and are not inputs here.</item>
/// </list>
/// </summary>
public static class SalesCustomerReturnCalculator
{
    /// <summary>
    /// The widest quantity scale Ö2 allows (the kilogram cap). The invoice line carries no unit dimension, so a return quantity is
    /// refused above this cap; the per-unit (piece = 0 decimals) check stays with the remaining-quantity rule (OPEN: follow-up).
    /// </summary>
    public static readonly int MaximumQuantityScale = Quantity.AllowedScale(QuantityUnit.Kilogram);

    private static readonly SalesCustomerReturnAmountType[] Types =
    [
        SalesCustomerReturnAmountType.Net, SalesCustomerReturnAmountType.Vat, SalesCustomerReturnAmountType.Withholding,
        SalesCustomerReturnAmountType.LineTotal,
    ];

    /// <summary>The quantity still returnable on the original line: invoiced minus the finalised returns (SAT-D292 (6), SAT-D293 (3)).</summary>
    public static decimal RemainingQuantity(SalesCustomerReturnOriginalLine original, IReadOnlyList<SalesCustomerReturnPrior> priors)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(priors);
        var returned = 0m;
        foreach (var prior in priors)
        {
            returned += prior.Quantity;
        }

        var remaining = original.Quantity - returned;
        return remaining >= 0m
            ? remaining
            : throw new InvalidOperationException("Kesinleşmiş iadeler orijinal satır miktarını aşıyor; kayıt tutarsız.");
    }

    /// <summary>Computes one return line of <paramref name="quantity"/> against the original line and its finalised priors.</summary>
    public static SalesCustomerReturnLineResult Calculate(SalesCustomerReturnOriginalLine original,
        IReadOnlyList<SalesCustomerReturnPrior> priors, decimal quantity)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(priors);
        if (original.Quantity <= 0m)
        {
            throw new InvalidOperationException("Orijinal fatura satırının miktarı pozitif olmalıdır.");
        }

        if (quantity <= 0m || Quantity.SignificantDecimals(quantity) > MaximumQuantityScale)
        {
            throw new SalesRuleException(SalesRuleCodes.CustomerReturnQuantityInvalid, "İade miktarı pozitif bir sayı olmalıdır.", original.LineId);
        }

        var remaining = RemainingQuantity(original, priors);
        if (quantity > remaining)
        {
            throw new SalesRuleException(SalesRuleCodes.CustomerReturnQuantityExceeded,
                "İade miktarı satırın kalan iade edilebilir miktarını aşamaz.", original.LineId);
        }

        var isLast = quantity == remaining;
        var amounts = Compute(original.Amounts, [.. priors.Select(prior => prior.Amounts)], original.Quantity, quantity, isLast,
            out var review);
        SalesCustomerReturnAmounts? functional = null;
        var functionalReview = false;
        if (original.Functional is { } originalFunctional)
        {
            var functionalPriors = new List<SalesCustomerReturnAmounts>(priors.Count);
            foreach (var prior in priors)
            {
                functionalPriors.Add(prior.Functional
                    ?? throw new InvalidOperationException("Dövizli satırın kesinleşmiş iadesinde TL tutarları eksik; kayıt tutarsız."));
            }

            functional = Compute(originalFunctional, functionalPriors, original.Quantity, quantity, isLast, out functionalReview);
        }
        else if (priors.Any(prior => prior.Functional is not null))
        {
            throw new InvalidOperationException("TL satırın kesinleşmiş iadesinde TL karşılığı olamaz; kayıt tutarsız.");
        }

        return new SalesCustomerReturnLineResult(quantity, isLast, amounts, functional, review || functionalReview);
    }

    /// <summary>
    /// SAT-D241 for a return: the payable (the sum of the line totals), VAT and withholding TL amounts of the return against the
    /// official document, each difference absolute and compared on its own. The tolerance is the caller's (Settings; never here).
    /// </summary>
    public static SalesCustomerReturnMatchEvaluation EvaluateMatch(SalesCustomerReturnAmounts functionalTotals, decimal officialPayable,
        decimal officialVat, decimal officialWithholding)
    {
        ArgumentNullException.ThrowIfNull(functionalTotals);
        return new SalesCustomerReturnMatchEvaluation(
            Math.Abs(functionalTotals.LineTotal.Amount - officialPayable),
            Math.Abs(functionalTotals.Vat.Amount - officialVat),
            Math.Abs(functionalTotals.Withholding.Amount - officialWithholding));
    }

    private static SalesCustomerReturnAmounts Compute(SalesCustomerReturnAmounts original, IReadOnlyList<SalesCustomerReturnAmounts> priors,
        decimal originalQuantity, decimal quantity, bool isLast, out bool needsReview)
    {
        needsReview = false;
        var values = new Dictionary<SalesCustomerReturnAmountType, Money>(Types.Length);
        foreach (var type in Types)
        {
            var source = original.Pick(type);
            Money value;
            if (isLast)
            {
                value = source;
                foreach (var prior in priors)
                {
                    value = value.Subtract(prior.Pick(type));
                }

                needsReview |= value.Amount != 0m && Math.Sign(value.Amount) != Math.Sign(source.Amount);
            }
            else
            {
                decimal exact;
                try
                {
                    exact = source.Amount * quantity / originalQuantity;
                }
                catch (OverflowException)
                {
                    throw new SalesRuleException(SalesRuleCodes.CustomerReturnAmountOutOfRange,
                        "Orantılı iade tutarı hesaplanamıyor; hiçbir tutar yazılmaz.");
                }

                value = Money.Round(exact, source.CurrencyId, source.Scale);
            }

            values[type] = value;
        }

        return new SalesCustomerReturnAmounts(values[SalesCustomerReturnAmountType.Net], values[SalesCustomerReturnAmountType.Vat],
            values[SalesCustomerReturnAmountType.Withholding], values[SalesCustomerReturnAmountType.LineTotal]);
    }
}

/// <summary>
/// SAT-D99 / SAT-D293 — the "invoiced, not shipped" balance of an original invoice line as Sales records it: invoiced quantity minus the
/// shipped quantity of the line minus the finalised returns WITHOUT a shipment link. A return linked to a shipment (or a physical
/// return) never decrements it, so a later physical return of the same goods cannot decrement it a second time (single decrement).
/// </summary>
public static class SalesCustomerReturnBalance
{
    public static decimal UnshippedRemaining(decimal invoicedQuantity, decimal shippedQuantity, IReadOnlyList<SalesCustomerReturnPrior> priors)
    {
        ArgumentNullException.ThrowIfNull(priors);
        var unlinked = 0m;
        foreach (var prior in priors)
        {
            if (!prior.IsShipmentLinked)
            {
                unlinked += prior.Quantity;
            }
        }

        var balance = invoicedQuantity - shippedQuantity - unlinked;
        return balance >= 0m
            ? balance
            : throw new InvalidOperationException("Faturalanmış-sevk edilmemiş bakiye eksiye inmiş; kayıt tutarsız.");
    }

    /// <summary>SAT-D293 (4): an unlinked return of <paramref name="quantity"/> must fit the balance; a linked return is not measured.</summary>
    public static void RequireFits(bool isShipmentLinked, decimal quantity, decimal invoicedQuantity, decimal shippedQuantity,
        IReadOnlyList<SalesCustomerReturnPrior> priors, Guid lineId)
    {
        if (isShipmentLinked)
        {
            return;
        }

        if (quantity > UnshippedRemaining(invoicedQuantity, shippedQuantity, priors))
        {
            throw new SalesRuleException(SalesRuleCodes.CustomerReturnUnshippedBalanceExceeded,
                "Sevke bağlı olmayan iade, satırın faturalanmış-sevk edilmemiş bakiyesini aşamaz.", lineId);
        }
    }
}
