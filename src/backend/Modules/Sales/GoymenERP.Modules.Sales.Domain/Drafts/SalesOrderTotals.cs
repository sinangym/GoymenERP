using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// One line's VAT rate and, when VAT withholding (tevkifat) applies to it, the withheld share of that VAT — data, never a code
/// constant (delegation No 15: "Tevkifat uygulanabilirliği, oranı ve yürürlük tarihi sürümlü KDV masterında veri olarak tutulur
/// … kodda sabit oran yoktur"). The source shape is decided (<c>SINAN_KARARLARI_2026-10-28_SATIS_EK12.md</c>):
/// <list type="bullet">
/// <item><b>SAT-D73 / SAT-D82 — <see cref="VatPercent"/>:</b> the product card carries a VAT CLASS (e.g. genel, indirimli), not a
/// rate (Product owner); the class's rate is read from the versioned VAT table in Settings for the order's business date, so a
/// rate change (e.g. %18 → %20) never rewrites an earlier record. A Settings exception per transaction / customer type (e.g.
/// export) replaces it when one applies to the order.</item>
/// <item><b>SAT-D74 — <see cref="WithholdingShare"/>:</b> present only when the product carries a withholding code (e.g.
/// demir-çelik 2/10) AND the buyer is subject to withholding (Counterparty owner); the share for that code is the Settings
/// rate. Otherwise null (no withholding).</item>
/// </list>
/// None of these sources exists on main yet (the product card VAT class and withholding code, the Settings versioned VAT table,
/// exception and withholding rate records, the buyer flag — MOD-03), so today no rate reaches <see cref="SalesOrderTotalsCalculator"/> and the tax is
/// <see cref="SalesTaxState.NoSource"/> (SAT-D73/D74: "kaynak yokken … 'kaynak yok' kalır"). The caller resolves the rate per line
/// for the order's business date and passes it in; this record only carries it.
/// </summary>
/// <param name="VatPercent">The VAT rate in percent, 0..100.</param>
/// <param name="WithholdingShare">The withheld share of the line VAT, 0 &lt; share ≤ 1 (e.g. 0.5 for 5/10); null = no withholding.</param>
public sealed record SalesLineTaxRate(decimal VatPercent, decimal? WithholdingShare)
{
    public static SalesLineTaxRate Of(decimal vatPercent, decimal? withholdingShare)
    {
        if (vatPercent is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(vatPercent), "KDV oranı 0 ile 100 arasında olmalıdır.");
        }

        if (withholdingShare is { } share && (share <= 0m || share > 1m))
        {
            throw new ArgumentOutOfRangeException(nameof(withholdingShare), "Tevkifat payı 0'dan büyük ve en fazla 1 olmalıdır.");
        }

        return new SalesLineTaxRate(vatPercent, withholdingShare);
    }
}

/// <summary>Whether a line's amount could be computed.</summary>
public enum SalesLineTotalState
{
    /// <summary>The line amount is quantity × net unit price, rounded once (SAT-D11).</summary>
    Computed = 1,

    /// <summary>The line carries no unit price: it has no amount — not zero, not free.</summary>
    Unpriced = 2,

    /// <summary>
    /// The line is priced in another unit than its order unit. Product publishes no conversion contract to Sales, so the amount
    /// is not computed (no factor is guessed).
    /// </summary>
    PriceUnitNotOrderUnit = 3,

    /// <summary>
    /// The line's exact amount (net, VAT, gross or payable) cannot be held EXACTLY by System.Decimal — too large, or more
    /// significant digits than its 28–29 — the technical limit of every stored amount; so nothing of the line is computed:
    /// fail-closed, never a wrong, truncated or silently rounded amount (and never a 500).
    /// </summary>
    AmountOutOfRange = 4,
}

/// <summary>Whether the document totals could be computed.</summary>
public enum SalesDocumentTotalState
{
    /// <summary>Every line has an amount and the sums are computed.</summary>
    Computed = 1,

    /// <summary>
    /// At least one line has no amount (<see cref="SalesLineTotalState"/>), or the order has no line at all: no sum is shown —
    /// a partial sum would understate the order, and an order without lines has nothing to total (never a computed 0).
    /// </summary>
    Incomplete = 2,

    /// <summary>
    /// Every line has an amount but a document sum cannot be held exactly by System.Decimal (too large, or more significant
    /// digits than it carries): no sum is shown (fail-closed), never a silently rounded one.
    /// </summary>
    AmountOutOfRange = 3,
}

/// <summary>Whether the VAT and withholding of a line (or the document) could be computed.</summary>
public enum SalesTaxState
{
    /// <summary>A VAT rate was answered for the line and VAT, withholding, gross and payable are computed.</summary>
    Computed = 1,

    /// <summary>No VAT master answered (MOD-03 does not exist yet): no VAT, withholding, gross or payable amount — never 0.</summary>
    NoSource = 2,

    /// <summary>
    /// The base the tax is computed on is missing: the line has no amount (<see cref="SalesLineTotalState"/>), or, on the
    /// document, a line has none. Nothing is computed, whatever the VAT master says.
    /// </summary>
    NotComputed = 3,
}

/// <summary>One line's input to the totals calculator: its identity, quantity in the order unit and commercial fields.</summary>
public sealed record SalesTotalsLineInput(Guid LineId, int LineNo, Guid OrderUnitId, decimal Quantity, CommercialDraftFields Commercial);

/// <summary>
/// One line's totals. Every amount is <see cref="Money"/> in the document currency at the amount scale; each is null exactly when
/// the state says it cannot be computed.
/// </summary>
public sealed record SalesLineTotal(
    Guid LineId,
    int LineNo,
    SalesLineTotalState State,
    decimal? NetUnitPrice,
    Money? NetAmount,
    SalesTaxState TaxState,
    SalesLineTaxRate? TaxRate,
    Money? VatAmount,
    Money? WithheldVatAmount,
    Money? GrossAmount,
    Money? PayableAmount);

/// <summary>
/// The document totals: the sums of the line amounts (SAT-D11 "toplamlar satırlardan"), never recomputed from a document net.
/// <see cref="State"/> is <see cref="SalesDocumentTotalState.Incomplete"/> when any line has no amount or there is no line, and
/// <see cref="SalesDocumentTotalState.AmountOutOfRange"/> when a sum does not fit; in both the net is null. The tax sums need a
/// computed document AND every line's tax computed.
/// </summary>
public sealed record SalesDocumentTotal(
    SalesDocumentTotalState State,
    int UnpricedLineCount,
    int PriceUnitNotOrderUnitLineCount,
    int AmountOutOfRangeLineCount,
    Money? NetAmount,
    SalesTaxState TaxState,
    Money? VatAmount,
    Money? WithheldVatAmount,
    Money? GrossAmount,
    Money? PayableAmount)
{
    public bool IsComplete => State == SalesDocumentTotalState.Computed;
}

/// <summary>The order's totals in its own currency at the money profile's amount scale.</summary>
public sealed record SalesOrderTotals(
    Guid CurrencyId,
    int AmountScale,
    SalesTaxState TaxState,
    IReadOnlyList<SalesLineTotal> Lines,
    SalesDocumentTotal Document);

/// <summary>
/// API-03 / SAT-D11 / delegation No 15: the ONE deterministic calculator of the order's amounts; the API, the screens, the risk
/// read (SAT-D46) and any report use it and never recompute (AGENTS.md "Formula/UI/report aynı deterministic backend
/// calculator'ı kullanır"). Per line, in order:
/// <list type="number">
/// <item><b>Net amount</b> = quantity × the exact net unit price (<see cref="SalesLinePriceCalculator"/>: base − discount +
/// maturity surcharge), rounded ONCE half away from zero to the amount scale (SAT-D11: "satır tutarı 2 ondalık yarı-yukarı";
/// the 2 is the Settings money profile's amount scale, Ö2 — not a code constant). SAT-D76: the net unit price itself is NOT
/// rounded; only the line amount is.</item>
/// <item><b>VAT</b> = net amount × VAT rate / 100 (rate per SAT-D73), rounded per line the same way (No 15).</item>
/// <item><b>Withheld VAT</b> (tevkifat, only where SAT-D74 says it applies) = the ROUNDED line VAT × the withheld share, and
/// that result rounded per line (SAT-D75 — research-based and reversible; matches the line-based e-Fatura practice; a kuruş
/// difference to the official invoice shows in invoice matching, S5); zero where withholding does not apply.</item>
/// <item><b>Gross</b> (KDV dahil, SAT-D46) = net + VAT; <b>payable</b> (Ödenecek Tutar, No 15 / UBL-TR PayableAmount) = gross −
/// withheld VAT.</item>
/// </list>
/// Document totals are the sums of the rounded line amounts (No 15: "belge toplamları satır toplamlarından türer"), so the
/// document never re-rounds. Banker's rounding is never used. No rate, scale or rounding mode is a code constant: the scale is
/// the caller's (the money profile snapshot), the rates are the VAT master's, the mode is <see cref="Money.Round"/>'s.
/// </summary>
public static class SalesOrderTotalsCalculator
{
    /// <summary>The totals of <paramref name="draft"/>'s current lines at its money profile's amount scale.</summary>
    /// <param name="taxRates">The VAT master's answer per line; null while no VAT master exists (every tax is NoSource).</param>
    public static SalesOrderTotals Calculate(SalesOrderDraft draft, Func<Guid, SalesLineTaxRate?>? taxRates = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return Calculate(
            [.. draft.Lines.Select(line => new SalesTotalsLineInput(line.Id, line.LineNo, line.OrderUnit.UnitId, line.Quantity.Value, line.Commercial))],
            draft.Header.CurrencyId,
            // A profile scale beyond System.Decimal's own 28 decimals allows nothing more than 28 does (SalesLineMoney's rule).
            Math.Min(draft.Money.Scale, Money.MaximumScale),
            taxRates);
    }

    /// <summary>The totals of <paramref name="lines"/> in <paramref name="currencyId"/> at <paramref name="amountScale"/>.</summary>
    public static SalesOrderTotals Calculate(IReadOnlyList<SalesTotalsLineInput> lines, Guid currencyId, int amountScale,
        Func<Guid, SalesLineTaxRate?>? taxRates = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (currencyId == Guid.Empty)
        {
            throw new ArgumentException("Para birimi kimliği boş olamaz.", nameof(currencyId));
        }

        if (amountScale is < 0 or > Money.MaximumScale)
        {
            throw new ArgumentOutOfRangeException(nameof(amountScale), $"Tutar ölçeği 0 ile {Money.MaximumScale} arasında olmalıdır.");
        }

        var totals = lines.Select(line => Line(line, currencyId, amountScale, taxRates)).ToList();
        var documentTax = taxRates is null ? SalesTaxState.NoSource : SalesTaxState.Computed;
        return new SalesOrderTotals(currencyId, amountScale, documentTax, totals.AsReadOnly(),
            Document(totals, currencyId, amountScale, documentTax));
    }

    private static SalesLineTotal Line(SalesTotalsLineInput line, Guid currencyId, int scale, Func<Guid, SalesLineTaxRate?>? taxRates)
    {
        ArgumentNullException.ThrowIfNull(line);
        var commercial = line.Commercial ?? throw new ArgumentException("Satırın ticari alanları boş olamaz.", nameof(line));
        if (commercial.UnitPrice is null)
        {
            return Uncomputed(line, SalesLineTotalState.Unpriced);
        }

        if (commercial.PriceUnitId != line.OrderUnitId)
        {
            return Uncomputed(line, SalesLineTotalState.PriceUnitNotOrderUnit);
        }

        try
        {
            var netUnitPrice = SalesLinePriceCalculator.NetUnitPrice(commercial);
            var net = Money.Of(SalesLinePriceCalculator.LineAmount(commercial, line.Quantity, scale)!.Value, currencyId, scale);
            if (taxRates?.Invoke(line.LineId) is not { } rate)
            {
                return new SalesLineTotal(line.LineId, line.LineNo, SalesLineTotalState.Computed, netUnitPrice, net,
                    SalesTaxState.NoSource, null, null, null, null, null);
            }

            var vat = Money.Of(SalesLinePriceCalculator.RoundedPercentOf(net.Amount, rate.VatPercent, scale), currencyId, scale);
            var withheld = rate.WithholdingShare is { } share
                ? Money.Of(SalesLinePriceCalculator.RoundedShareOf(vat.Amount, share, scale), currencyId, scale)
                : Money.Zero(currencyId, scale);
            // Review B P2-3: gross and payable are exact (System.Decimal's + / - would silently round past 28–29 digits).
            var gross = Money.Of(SalesLinePriceCalculator.ExactSum([net.Amount, vat.Amount]), currencyId, scale);
            var payable = Money.Of(SalesLinePriceCalculator.ExactSum([gross.Amount], [withheld.Amount]), currencyId, scale);
            return new SalesLineTotal(line.LineId, line.LineNo, SalesLineTotalState.Computed, netUnitPrice, net,
                SalesTaxState.Computed, rate, vat, withheld, gross, payable);
        }
        catch (OverflowException)
        {
            // The exact value is beyond System.Decimal (too large, or more digits than it holds exactly): the line has no amount
            // (never a truncated or silently rounded one, never a 500).
            return Uncomputed(line, SalesLineTotalState.AmountOutOfRange);
        }
    }

    private static SalesLineTotal Uncomputed(SalesTotalsLineInput line, SalesLineTotalState state) =>
        new(line.LineId, line.LineNo, state, null, null, SalesTaxState.NotComputed, null, null, null, null, null);

    private static SalesDocumentTotal Document(IReadOnlyList<SalesLineTotal> lines, Guid currencyId, int scale, SalesTaxState documentTax)
    {
        var unpriced = lines.Count(line => line.State == SalesLineTotalState.Unpriced);
        var unconvertible = lines.Count(line => line.State == SalesLineTotalState.PriceUnitNotOrderUnit);
        var outOfRange = lines.Count(line => line.State == SalesLineTotalState.AmountOutOfRange);
        // No line is nothing to total, never a computed 0 (the domain stores no draft without lines; the rule holds anyway).
        if (lines.Count == 0 || unpriced > 0 || unconvertible > 0 || outOfRange > 0)
        {
            return Uncomputed(SalesDocumentTotalState.Incomplete, unpriced, unconvertible, outOfRange);
        }

        try
        {
            var net = Sum(lines.Select(line => line.NetAmount!.Value), currencyId, scale);
            if (documentTax != SalesTaxState.Computed || lines.Any(line => line.TaxState != SalesTaxState.Computed))
            {
                return new SalesDocumentTotal(SalesDocumentTotalState.Computed, 0, 0, 0, net, SalesTaxState.NoSource,
                    null, null, null, null);
            }

            return new SalesDocumentTotal(SalesDocumentTotalState.Computed, 0, 0, 0, net, SalesTaxState.Computed,
                Sum(lines.Select(line => line.VatAmount!.Value), currencyId, scale),
                Sum(lines.Select(line => line.WithheldVatAmount!.Value), currencyId, scale),
                Sum(lines.Select(line => line.GrossAmount!.Value), currencyId, scale),
                Sum(lines.Select(line => line.PayableAmount!.Value), currencyId, scale));
        }
        catch (OverflowException)
        {
            // Every line fits but a sum does not fit EXACTLY (too large, or more digits than System.Decimal holds): no sum is shown
            // (fail-closed), never a truncated or silently rounded one (review B P2-3).
            return Uncomputed(SalesDocumentTotalState.AmountOutOfRange, 0, 0, 0);
        }
    }

    private static SalesDocumentTotal Uncomputed(SalesDocumentTotalState state, int unpriced, int unconvertible, int outOfRange) =>
        new(state, unpriced, unconvertible, outOfRange, null, SalesTaxState.NotComputed, null, null, null, null);

    /// <summary>The exact sum (review B P2-3: never System.Decimal's silently rounding <c>+</c>); not exactly representable → overflow.</summary>
    private static Money Sum(IEnumerable<Money> amounts, Guid currencyId, int scale) =>
        Money.Of(SalesLinePriceCalculator.ExactSum(amounts.Select(amount => amount.Amount)), currencyId, scale);
}
