namespace GoymenERP.BuildingBlocks.Monetary;

/// <summary>Whether a line's amount could be computed.</summary>
public enum CommercialLineAmountState
{
    /// <summary>The line amount is quantity × the exact net unit price, rounded once (SAT-D11).</summary>
    Computed = 1,

    /// <summary>The line carries no price: it has no amount — not zero, not free ("fiyat bekliyor").</summary>
    Unpriced = 2,

    /// <summary>An exact amount of the line cannot be held by System.Decimal: nothing of the line is computed (fail-closed).</summary>
    AmountOutOfRange = 3,
}

/// <summary>Whether the VAT of a line (or of the document) could be computed.</summary>
public enum CommercialVatState
{
    /// <summary>A VAT rate was answered and the VAT and the gross amount are computed.</summary>
    Computed = 1,

    /// <summary>No usable VAT rate for the line (see its reason): no VAT and no gross amount — "hesaplanamıyor", never 0.</summary>
    NotComputable = 2,

    /// <summary>The base is missing: the line (or a line of the document) has no amount. Nothing is computed.</summary>
    NotComputed = 3,
}

/// <summary>Whether the withheld VAT (tevkifat) of a line (or of the document) could be computed.</summary>
public enum CommercialWithholdingState
{
    /// <summary>A withholding share applies and the withheld VAT and the payable amount are computed.</summary>
    Computed = 1,

    /// <summary>No withholding applies: the withheld VAT is 0 and the payable amount is the gross amount.</summary>
    NotApplied = 2,

    /// <summary>
    /// The withholding cannot be decided (see its reason; e.g. Alış until the mali müşavir confirms N-20): no withheld VAT and no
    /// payable amount — "hesaplanamıyor", never 0.
    /// </summary>
    NotComputable = 3,

    /// <summary>The VAT itself is not computed, so nothing of the withholding is.</summary>
    NotComputed = 4,
}

/// <summary>Whether the document sums could be computed.</summary>
public enum CommercialDocumentState
{
    /// <summary>Every line has an amount and the net sum is computed.</summary>
    Computed = 1,

    /// <summary>A line has no amount, or there is no line: no sum is shown (a partial sum would understate the document).</summary>
    Incomplete = 2,

    /// <summary>Every line has an amount but a sum cannot be held exactly by System.Decimal: no sum is shown (fail-closed).</summary>
    AmountOutOfRange = 3,
}

/// <summary>
/// A line's price input: the unit price, at most one discount and at most one maturity surcharge (vade farkı), all exact.
/// </summary>
public sealed record CommercialLinePriceInput(decimal UnitPrice, decimal? DiscountPercent = null, decimal? DiscountAmount = null,
    decimal? SurchargePercent = null, decimal? SurchargeAmount = null);

/// <summary>
/// A line's tax input, as answered by the consumer's own tax source: a VAT percent (0 for exempt) or none with a reason; and the
/// withholding as <see cref="CommercialWithholdingState.Computed"/> (with the withheld share of the VAT, 0 &lt; share ≤ 1),
/// <see cref="CommercialWithholdingState.NotApplied"/> or <see cref="CommercialWithholdingState.NotComputable"/> (with a reason).
/// </summary>
public sealed record CommercialLineTaxInput(
    decimal? VatPercent,
    string? VatReason,
    CommercialWithholdingState Withholding,
    decimal? WithholdingShare = null,
    string? WithholdingReason = null)
{
    public static CommercialLineTaxInput Vat(decimal vatPercent, decimal? withholdingShare) =>
        new(vatPercent, null, withholdingShare is null ? CommercialWithholdingState.NotApplied : CommercialWithholdingState.Computed,
            withholdingShare);

    public static CommercialLineTaxInput VatWithWithholdingNotComputable(decimal vatPercent, string reason) =>
        new(vatPercent, null, CommercialWithholdingState.NotComputable, null, reason);

    public static CommercialLineTaxInput VatNotComputable(string reason) =>
        new(null, reason, CommercialWithholdingState.NotComputed);
}

/// <summary>One line's input: its identity, quantity in the price unit, price (null = unpriced) and tax.</summary>
public sealed record CommercialTotalsLineInput(Guid LineId, decimal Quantity, CommercialLinePriceInput? Price, CommercialLineTaxInput Tax);

/// <summary>One line's totals at the amount scale; each amount is null exactly when its state says it is not computed.</summary>
public sealed record CommercialLineTotal(
    Guid LineId,
    CommercialLineAmountState State,
    decimal? NetUnitPrice,
    decimal? NetAmount,
    CommercialVatState VatState,
    decimal? VatPercent,
    decimal? VatAmount,
    string? VatReason,
    decimal? GrossAmount,
    CommercialWithholdingState WithholdingState,
    decimal? WithheldVatAmount,
    string? WithholdingReason,
    decimal? PayableAmount);

/// <summary>
/// The document sums of the line amounts (SAT-D11 "toplamlar satırlardan"). The VAT and gross sums need every line's VAT computed;
/// the withheld VAT and payable sums need every line's withholding computed or not applied.
/// </summary>
public sealed record CommercialDocumentTotal(
    CommercialDocumentState State,
    int UnpricedLineCount,
    int AmountOutOfRangeLineCount,
    decimal? NetAmount,
    CommercialVatState VatState,
    int VatNotComputableLineCount,
    decimal? VatAmount,
    decimal? GrossAmount,
    CommercialWithholdingState WithholdingState,
    int WithholdingNotComputableLineCount,
    decimal? WithheldVatAmount,
    decimal? PayableAmount);

public sealed record CommercialTotals(int AmountScale, IReadOnlyList<CommercialLineTotal> Lines, CommercialDocumentTotal Document);

/// <summary>
/// A2a-2 (TEK-01 / TEK-03, Alış planı; ALS-D74): the shared document totals calculator for a module that has no calculator of its own
/// yet — Purchasing is its first consumer. It applies the SAME rules as the Sales <c>SalesOrderTotalsCalculator</c> (delegation No 15,
/// SAT-D11, SAT-D75, SAT-D76), pinned to it by a golden parity test in the integration project:
/// <list type="number">
/// <item><b>Net amount</b> = quantity × the exact net unit price (<see cref="CommercialLinePriceCalculator"/>: base − discount +
/// maturity surcharge), rounded ONCE half away from zero to the amount scale; the net unit price is never rounded (SAT-D76).</item>
/// <item><b>VAT</b> = net amount × VAT % / 100, rounded per line the same way (No 15).</item>
/// <item><b>Withheld VAT</b> = the ROUNDED line VAT × the withheld share, rounded (SAT-D75). The calculator supports it; whether a
/// consumer may ask is its own rule (Alış: never until N-20 — it passes <see cref="CommercialWithholdingState.NotComputable"/>).</item>
/// <item><b>Gross</b> = net + VAT; <b>payable</b> = gross − withheld VAT; exact sums, never System.Decimal's silently rounding
/// <c>+</c>.</item>
/// </list>
/// A value that cannot be held exactly makes the line (or the sum) <c>AmountOutOfRange</c>, never a rounded amount. The Sales calculator
/// is not changed; moving Sales onto this type is the Sales lane's follow-up (<c>docs/handoffs/ALIS_SATIS_DEVIR_v1.md</c>, TEK-03).
/// </summary>
public static class CommercialTaxTotalsCalculator
{
    public static CommercialTotals Calculate(IReadOnlyList<CommercialTotalsLineInput> lines, int amountScale)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (amountScale is < 0 or > Money.MaximumScale)
        {
            throw new ArgumentOutOfRangeException(nameof(amountScale), $"Tutar ölçeği 0 ile {Money.MaximumScale} arasında olmalıdır.");
        }

        var totals = lines.Select(line => Line(line, amountScale)).ToList().AsReadOnly();
        return new CommercialTotals(amountScale, totals, Document(totals));
    }

    private static CommercialLineTotal Line(CommercialTotalsLineInput line, int scale)
    {
        ArgumentNullException.ThrowIfNull(line);
        var tax = line.Tax ?? throw new ArgumentException("Satırın vergi girdisi boş olamaz.", nameof(line));
        if (line.Price is not { } price)
        {
            return Uncomputed(line.LineId, CommercialLineAmountState.Unpriced);
        }

        try
        {
            var netUnitPrice = CommercialLinePriceCalculator.NetUnitPrice(price.UnitPrice, price.DiscountPercent, price.DiscountAmount,
                price.SurchargePercent, price.SurchargeAmount);
            var net = CommercialLinePriceCalculator.LineAmount(line.Quantity, price.UnitPrice, price.DiscountPercent, price.DiscountAmount,
                price.SurchargePercent, price.SurchargeAmount, scale);
            if (tax.VatPercent is not { } vatPercent)
            {
                return new CommercialLineTotal(line.LineId, CommercialLineAmountState.Computed, netUnitPrice, net,
                    CommercialVatState.NotComputable, null, null, tax.VatReason, null, CommercialWithholdingState.NotComputed, null, null, null);
            }

            if (vatPercent is < 0m or > 100m)
            {
                throw new ArgumentOutOfRangeException(nameof(line), "KDV oranı 0 ile 100 arasında olmalıdır.");
            }

            var vat = CommercialLinePriceCalculator.RoundedPercentOf(net, vatPercent, scale);
            var gross = CommercialLinePriceCalculator.ExactSum([net, vat]);
            switch (tax.Withholding)
            {
                case CommercialWithholdingState.Computed:
                    var share = tax.WithholdingShare is { } value and > 0m and <= 1m
                        ? value
                        : throw new ArgumentOutOfRangeException(nameof(line), "Tevkifat payı 0'dan büyük ve en fazla 1 olmalıdır.");
                    var withheld = CommercialLinePriceCalculator.RoundedShareOf(vat, share, scale);
                    return new CommercialLineTotal(line.LineId, CommercialLineAmountState.Computed, netUnitPrice, net, CommercialVatState.Computed,
                        vatPercent, vat, null, gross, CommercialWithholdingState.Computed, withheld, null,
                        CommercialLinePriceCalculator.ExactSum([gross], [withheld]));
                case CommercialWithholdingState.NotApplied:
                    return new CommercialLineTotal(line.LineId, CommercialLineAmountState.Computed, netUnitPrice, net, CommercialVatState.Computed,
                        vatPercent, vat, null, gross, CommercialWithholdingState.NotApplied, 0m, null, gross);
                case CommercialWithholdingState.NotComputable:
                    return new CommercialLineTotal(line.LineId, CommercialLineAmountState.Computed, netUnitPrice, net, CommercialVatState.Computed,
                        vatPercent, vat, null, gross, CommercialWithholdingState.NotComputable, null, tax.WithholdingReason, null);
                default:
                    throw new ArgumentException("Satırın tevkifat durumu geçersiz.", nameof(line));
            }
        }
        catch (OverflowException)
        {
            return Uncomputed(line.LineId, CommercialLineAmountState.AmountOutOfRange);
        }
    }

    private static CommercialLineTotal Uncomputed(Guid lineId, CommercialLineAmountState state) =>
        new(lineId, state, null, null, CommercialVatState.NotComputed, null, null, null, null, CommercialWithholdingState.NotComputed, null,
            null, null);

    private static CommercialDocumentTotal Document(IReadOnlyList<CommercialLineTotal> lines)
    {
        var unpriced = lines.Count(line => line.State == CommercialLineAmountState.Unpriced);
        var outOfRange = lines.Count(line => line.State == CommercialLineAmountState.AmountOutOfRange);
        if (lines.Count == 0 || unpriced > 0 || outOfRange > 0)
        {
            return Uncomputed(CommercialDocumentState.Incomplete, unpriced, outOfRange);
        }

        try
        {
            var net = CommercialLinePriceCalculator.ExactSum(lines.Select(line => line.NetAmount!.Value));
            var vatMissing = lines.Count(line => line.VatState != CommercialVatState.Computed);
            if (vatMissing > 0)
            {
                return new CommercialDocumentTotal(CommercialDocumentState.Computed, 0, 0, net, CommercialVatState.NotComputable, vatMissing,
                    null, null, CommercialWithholdingState.NotComputed, 0, null, null);
            }

            var vat = CommercialLinePriceCalculator.ExactSum(lines.Select(line => line.VatAmount!.Value));
            var gross = CommercialLinePriceCalculator.ExactSum(lines.Select(line => line.GrossAmount!.Value));
            var withholdingMissing = lines.Count(line => line.WithholdingState == CommercialWithholdingState.NotComputable);
            if (withholdingMissing > 0)
            {
                return new CommercialDocumentTotal(CommercialDocumentState.Computed, 0, 0, net, CommercialVatState.Computed, 0, vat, gross,
                    CommercialWithholdingState.NotComputable, withholdingMissing, null, null);
            }

            var withholding = lines.Any(line => line.WithholdingState == CommercialWithholdingState.Computed)
                ? CommercialWithholdingState.Computed
                : CommercialWithholdingState.NotApplied;
            return new CommercialDocumentTotal(CommercialDocumentState.Computed, 0, 0, net, CommercialVatState.Computed, 0, vat, gross,
                withholding, 0, CommercialLinePriceCalculator.ExactSum(lines.Select(line => line.WithheldVatAmount!.Value)),
                CommercialLinePriceCalculator.ExactSum(lines.Select(line => line.PayableAmount!.Value)));
        }
        catch (OverflowException)
        {
            return Uncomputed(CommercialDocumentState.AmountOutOfRange, 0, 0);
        }
    }

    private static CommercialDocumentTotal Uncomputed(CommercialDocumentState state, int unpriced, int outOfRange) =>
        new(state, unpriced, outOfRange, null, CommercialVatState.NotComputed, 0, null, null, CommercialWithholdingState.NotComputed, 0, null,
            null);
}
