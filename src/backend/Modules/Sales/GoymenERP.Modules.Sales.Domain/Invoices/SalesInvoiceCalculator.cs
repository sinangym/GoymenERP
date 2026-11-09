using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Domain.Invoices;

/// <summary>One invoice line's input: the invoiced quantity in the order unit, the price basis line's commercial fields and tax.</summary>
public sealed record SalesInvoiceLineInput(
    Guid LineId,
    int LineNo,
    Guid OrderUnitId,
    decimal Quantity,
    CommercialDraftFields Commercial,
    SalesLineTaxSnapshot? Tax);

/// <summary>
/// The conversion of a foreign-currency invoice into the functional currency (TL): the invoice's ONE rate (SAT-D90/SAT-D170, fixed
/// on the invoice by SAT-D172; SAT-D226) and the functional currency's money scale (the Settings TRY money profile; SAT-D223 "2
/// ondalık" is that profile's scale, never a code constant). Absent for a TL invoice (no conversion, EK42).
/// </summary>
public sealed record SalesInvoiceFunctionalConversion(ExchangeRate Rate, int FunctionalScale);

/// <summary>Whether the invoice amounts could be computed; anything but <see cref="Computed"/> carries no amount at all.</summary>
public enum SalesInvoiceAmountsState
{
    Computed = 1,

    /// <summary>A line has no amount (unpriced, priced in another unit) or there is no line.</summary>
    Incomplete = 2,

    /// <summary>A line's tax has no usable source: no VAT, withholding or payable is computed (never 0).</summary>
    TaxUnavailable = 3,

    /// <summary>An exact amount cannot be held by System.Decimal: nothing is computed (never a rounded amount).</summary>
    AmountOutOfRange = 4,
}

/// <summary>The amount types the SAT-D223 / SAT-D224 difference is placed on (SAT-D225: payable is never converted).</summary>
public enum SalesInvoiceAmountType
{
    Net = 1,
    Vat = 2,
    Withholding = 3,
}

/// <summary>One set of amounts: net (matrah), VAT, withheld VAT (tevkifat), gross (KDV dahil) and payable (ödenecek).</summary>
public sealed record SalesInvoiceAmountSet(Money Net, Money Vat, Money Withholding, Money Gross, Money Payable);

/// <summary>
/// A line's functional (TL) amounts: <see cref="Amounts"/> after the group difference, the amount types that took a difference
/// (<see cref="Adjusted"/>), and <see cref="NeedsReview"/> when a difference flipped the sign of the line's TL amount or exceeded
/// it (EK42 "işaret değişimi ve aşma": never refused, flagged for the Kontrol Merkezi).
/// </summary>
public sealed record SalesInvoiceFunctionalLine(SalesInvoiceAmountSet Amounts, IReadOnlyList<SalesInvoiceAmountType> Adjusted, bool NeedsReview);

/// <summary>One line's amounts in the invoice currency, the rate it was taxed at, and its TL amounts for a foreign-currency invoice.</summary>
public sealed record SalesInvoiceLineAmounts(
    Guid LineId,
    int LineNo,
    decimal NetUnitPrice,
    decimal VatPercent,
    decimal? WithholdingShare,
    SalesInvoiceAmountSet Amounts,
    SalesInvoiceFunctionalLine? Functional);

/// <summary>One VAT rate group (SAT-D223: an exemption / 0 % is its own group): its lines' sums and its TL totals.</summary>
public sealed record SalesInvoiceVatGroup(decimal VatPercent, IReadOnlyList<Guid> LineIds, SalesInvoiceAmountSet Amounts,
    SalesInvoiceAmountSet? Functional);

/// <summary>The calculator's answer. Every amount member is null unless <see cref="State"/> is Computed.</summary>
public sealed record SalesInvoiceAmounts(
    SalesInvoiceAmountsState State,
    Guid CurrencyId,
    int AmountScale,
    IReadOnlyList<SalesInvoiceLineAmounts> Lines,
    IReadOnlyList<SalesInvoiceVatGroup> Groups,
    SalesInvoiceAmountSet? Totals,
    SalesInvoiceAmountSet? FunctionalTotals,
    IReadOnlyList<string> TaxReasons)
{
    public bool IsComputed => State == SalesInvoiceAmountsState.Computed;
}

/// <summary>
/// API-32: the ONE deterministic calculator of a sales invoice's amounts (AGENTS.md "Formula/UI/report aynı deterministic backend
/// calculator'ı kullanır"). The screen, the Finance admission and the Accounting admission take its answer; nobody recomputes.
/// <list type="number">
/// <item><b>Invoice currency</b> — per line exactly the order totals calculator (<see cref="SalesOrderTotalsCalculator"/>,
/// SAT-D11 / SAT-D75 / SAT-D76): net = quantity × net unit price rounded once; VAT and withholding per line from the rounded
/// net / VAT; gross = net + VAT; payable = gross − withholding; document totals are sums of the lines.</item>
/// <item><b>TL equivalent</b> (foreign currency only; EK42 SAT-D223…D226): lines are grouped by VAT rate. For each group and
/// each amount type (net, VAT, withholding — SAT-D224 converts the withholding itself) the group's TL total is the group's
/// invoice-currency sum × the invoice rate rounded once (SAT-D11, <see cref="Money.Round"/>); each line's TL is its own amount ×
/// rate rounded the same way; the difference (plus or minus) goes to the LAST line (invoice order) of the group that carries a
/// non-zero amount of that type. Gross TL = net TL + VAT TL; payable TL = net TL + VAT TL − withholding TL (SAT-D225), on line,
/// group and document — never converted on its own. Document TL totals are the sums of the groups.</item>
/// </list>
/// Fail-closed: an unpriced line, a price in another unit, a missing tax source or an amount outside System.Decimal gives no
/// amount at all (never a partial or zero sum).
/// </summary>
public static class SalesInvoiceCalculator
{
    public static SalesInvoiceAmounts Calculate(IReadOnlyList<SalesInvoiceLineInput> lines, Guid currencyId, int amountScale,
        SalesInvoiceFunctionalConversion? conversion)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (conversion is not null)
        {
            if (conversion.Rate.SourceCurrencyId != currencyId)
            {
                throw new ArgumentException("Kurun kaynak para birimi faturanın para birimi olmalıdır.", nameof(conversion));
            }

            if (conversion.FunctionalScale is < 0 or > Money.MaximumScale)
            {
                throw new ArgumentOutOfRangeException(nameof(conversion), "TL tutar ölçeği geçersiz.");
            }
        }

        var inputs = lines.Select(line => new SalesTotalsLineInput(line.LineId, line.LineNo, line.OrderUnitId, line.Quantity,
            line.Commercial, line.Tax)).ToList();
        var totals = SalesOrderTotalsCalculator.CalculateWithLineTax(inputs, currencyId, amountScale);
        var reasons = totals.Lines.Where(line => line.TaxReason is not null).Select(line => line.TaxReason!)
            .Distinct(StringComparer.Ordinal).ToArray();
        switch (totals.Document.State)
        {
            case SalesDocumentTotalState.Incomplete:
                return Empty(SalesInvoiceAmountsState.Incomplete, currencyId, amountScale, reasons);
            case SalesDocumentTotalState.AmountOutOfRange:
                return Empty(SalesInvoiceAmountsState.AmountOutOfRange, currencyId, amountScale, reasons);
        }

        if (totals.Document.TaxState != SalesTaxState.Computed)
        {
            return Empty(SalesInvoiceAmountsState.TaxUnavailable, currencyId, amountScale, reasons);
        }

        try
        {
            return Compute(totals, conversion);
        }
        catch (OverflowException)
        {
            return Empty(SalesInvoiceAmountsState.AmountOutOfRange, currencyId, amountScale, reasons);
        }
    }

    private static SalesInvoiceAmounts Compute(SalesOrderTotals totals, SalesInvoiceFunctionalConversion? conversion)
    {
        var lines = totals.Lines.OrderBy(line => line.LineNo).ToList();
        var groups = lines.GroupBy(line => line.TaxRate!.VatPercent).Select(group => group.ToList()).ToList();
        var functional = new Dictionary<Guid, SalesInvoiceFunctionalLine>();
        var vatGroups = new List<SalesInvoiceVatGroup>(groups.Count);
        foreach (var group in groups)
        {
            var amounts = Sum(group.Select(Amounts).ToList(), totals.CurrencyId, totals.AmountScale);
            SalesInvoiceAmountSet? groupFunctional = null;
            if (conversion is not null)
            {
                groupFunctional = ConvertGroup(group, conversion, functional);
            }

            vatGroups.Add(new SalesInvoiceVatGroup(group[0].TaxRate!.VatPercent, [.. group.Select(line => line.LineId)], amounts,
                groupFunctional));
        }

        var result = lines.Select(line => new SalesInvoiceLineAmounts(line.LineId, line.LineNo, line.NetUnitPrice!.Value,
            line.TaxRate!.VatPercent, line.TaxRate.WithholdingShare, Amounts(line),
            conversion is null ? null : functional[line.LineId])).ToList();
        var documentTotals = Sum([.. vatGroups.Select(group => group.Amounts)], totals.CurrencyId, totals.AmountScale);
        var functionalTotals = conversion is null
            ? null
            : Sum([.. vatGroups.Select(group => group.Functional!)], conversion.Rate.TargetCurrencyId, conversion.FunctionalScale);
        return new SalesInvoiceAmounts(SalesInvoiceAmountsState.Computed, totals.CurrencyId, totals.AmountScale, result, vatGroups,
            documentTotals, functionalTotals, []);
    }

    /// <summary>SAT-D223 / SAT-D224 / SAT-D225 for one VAT rate group; fills each line's functional amounts.</summary>
    private static SalesInvoiceAmountSet ConvertGroup(IReadOnlyList<SalesLineTotal> group, SalesInvoiceFunctionalConversion conversion,
        IDictionary<Guid, SalesInvoiceFunctionalLine> functional)
    {
        var rate = conversion.Rate;
        var scale = conversion.FunctionalScale;
        var target = rate.TargetCurrencyId;
        var perType = new Dictionary<SalesInvoiceAmountType, Money[]>();
        var groupTotals = new Dictionary<SalesInvoiceAmountType, Money>();
        var adjusted = group.ToDictionary(line => line.LineId, _ => new List<SalesInvoiceAmountType>());
        var review = group.ToDictionary(line => line.LineId, _ => false);
        foreach (var type in new[] { SalesInvoiceAmountType.Net, SalesInvoiceAmountType.Vat, SalesInvoiceAmountType.Withholding })
        {
            var source = group.Select(line => Pick(line, type)).ToArray();
            var groupSource = Money.Of(SalesLinePriceCalculator.ExactSum(source.Select(amount => amount.Amount)), source[0].CurrencyId,
                source[0].Scale);
            var groupFunctional = rate.Convert(groupSource, scale);
            var converted = source.Select(amount => rate.Convert(amount, scale)).ToArray();
            var difference = groupFunctional.Subtract(converted.Aggregate(Money.Zero(target, scale), (sum, amount) => sum.Add(amount)));
            if (!difference.IsZero)
            {
                // EK42: the LAST line in invoice order whose amount of this type is not zero; none means the group sum is zero too.
                var last = Array.FindLastIndex(source, amount => !amount.IsZero);
                if (last < 0)
                {
                    throw new InvalidOperationException("Grup farkı, o türde tutarı olmayan bir grupta oluşamaz.");
                }

                var before = converted[last];
                var after = before.Add(difference);
                converted[last] = after;
                adjusted[group[last].LineId].Add(type);
                if (Math.Sign(after.Amount) != Math.Sign(before.Amount) || Math.Abs(difference.Amount) > Math.Abs(before.Amount))
                {
                    review[group[last].LineId] = true;
                }
            }

            perType[type] = converted;
            groupTotals[type] = groupFunctional;
        }

        for (var index = 0; index < group.Count; index++)
        {
            var line = group[index];
            functional[line.LineId] = new SalesInvoiceFunctionalLine(
                Set(perType[SalesInvoiceAmountType.Net][index], perType[SalesInvoiceAmountType.Vat][index],
                    perType[SalesInvoiceAmountType.Withholding][index]),
                adjusted[line.LineId].AsReadOnly(), review[line.LineId]);
        }

        return Set(groupTotals[SalesInvoiceAmountType.Net], groupTotals[SalesInvoiceAmountType.Vat],
            groupTotals[SalesInvoiceAmountType.Withholding]);
    }

    /// <summary>SAT-D225: gross = net + VAT and payable = net + VAT − withholding — derived, never converted or rounded on their own.</summary>
    public static SalesInvoiceAmountSet Set(Money net, Money vat, Money withholding)
    {
        var gross = net.Add(vat);
        return new SalesInvoiceAmountSet(net, vat, withholding, gross, gross.Subtract(withholding));
    }

    private static Money Pick(SalesLineTotal line, SalesInvoiceAmountType type) => type switch
    {
        SalesInvoiceAmountType.Net => line.NetAmount!.Value,
        SalesInvoiceAmountType.Vat => line.VatAmount!.Value,
        _ => line.WithheldVatAmount!.Value,
    };

    private static SalesInvoiceAmountSet Amounts(SalesLineTotal line) =>
        new(line.NetAmount!.Value, line.VatAmount!.Value, line.WithheldVatAmount!.Value, line.GrossAmount!.Value,
            line.PayableAmount!.Value);

    /// <summary>The exact sums of a list of amount sets (No 15: totals come from the parts, never re-rounded).</summary>
    public static SalesInvoiceAmountSet Sum(IReadOnlyList<SalesInvoiceAmountSet> sets, Guid currencyId, int scale)
    {
        var zero = Money.Zero(currencyId, scale);
        var net = zero;
        var vat = zero;
        var withholding = zero;
        foreach (var set in sets)
        {
            net = net.Add(set.Net);
            vat = vat.Add(set.Vat);
            withholding = withholding.Add(set.Withholding);
        }

        return Set(net, vat, withholding);
    }

    private static SalesInvoiceAmounts Empty(SalesInvoiceAmountsState state, Guid currencyId, int scale, IReadOnlyList<string> reasons) =>
        new(state, currencyId, scale, [], [], null, null, reasons);
}
