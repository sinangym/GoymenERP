namespace GoymenERP.Modules.Sales.Application.Contracts;

// ---- API-03: the order's totals (SAT-D11, delegation No 15) ---------------------------------------------------------------
//
// GET /api/v1/sales/drafts/{id}/totals under the draft read AND the price read (sales.orders.price.read): every amount is the
// one backend calculator's (SalesOrderTotalsCalculator) — the screen never recomputes. Amounts are exact invariant text padded
// to the money profile's amount scale ("1250.00"). A value that cannot be computed is null beside a state that says why; it is
// never 0.

/// <summary>The stable wire names of the totals states.</summary>
public static class SalesDraftTotalsStatesV1
{
    /// <summary>The amount (line) or the sums (document) are computed.</summary>
    public const string Computed = "Computed";

    /// <summary>Line: no unit price — no amount, not zero.</summary>
    public const string Unpriced = "Unpriced";

    /// <summary>Line: priced in another unit than its order unit; no conversion contract exists, so no amount is computed.</summary>
    public const string PriceUnitNotOrderUnit = "PriceUnitNotOrderUnit";

    /// <summary>
    /// Line: its exact amount cannot be held exactly in the technical decimal range of a stored amount (too large, or more
    /// significant digits than it carries); document: a sum cannot. Nothing of it is computed — fail-closed, never a truncated or
    /// silently rounded amount and never a 500.
    /// </summary>
    public const string AmountOutOfRange = "AmountOutOfRange";

    /// <summary>
    /// Document: at least one line has no amount, or the order has no line: no sum is shown (a partial sum would understate the
    /// order; no line is nothing to total, never a computed 0).
    /// </summary>
    public const string Incomplete = "Incomplete";

    /// <summary>Tax: no VAT master answered (MOD-03 not on main) — no VAT, withholding, gross or payable amount.</summary>
    public const string NoSource = "NoSource";

    /// <summary>Tax: the base is missing (the line or the document has no amount).</summary>
    public const string NotComputed = "NotComputed";
}

/// <summary>
/// The order's totals at its CURRENT state: <c>draftVersion</c> (the concurrency token the totals were computed at) and
/// <c>revision</c> ("Rev.N", the current commercial revision — while a revision awaits re-approval these are its unapproved
/// amounts). <c>currencyId</c> is the order's currency (no conversion); <c>amountScale</c> the money profile's amount scale the
/// amounts are rounded to; <c>taxState</c> whether a VAT master answered at all.
/// </summary>
public sealed record SalesOrderDraftTotalsResponse(
    string DraftVersion,
    string Revision,
    Guid CurrencyId,
    int AmountScale,
    string TaxState,
    SalesDraftDocumentTotalsV1 Totals,
    IReadOnlyList<SalesDraftLineTotalV1> Lines);

/// <summary>
/// One line: its quantity in the order unit (the detail's text), <c>state</c> (<c>Computed</c> / <c>Unpriced</c> /
/// <c>PriceUnitNotOrderUnit</c> / <c>AmountOutOfRange</c>), the exact net unit price (never rounded), the net amount (quantity × net unit price, rounded
/// once half away from zero), and — when <c>taxState</c> is <c>Computed</c> — the VAT rate and amount, the withheld share and
/// amount (zero where withholding does not apply), gross (KDV dahil) and payable (gross − withheld VAT).
/// </summary>
public sealed record SalesDraftLineTotalV1(
    Guid LineId,
    int LineNo,
    Guid OrderUnitId,
    string Quantity,
    string State,
    string? NetUnitPrice,
    string? NetAmount,
    string TaxState,
    string? VatPercent,
    string? VatAmount,
    string? WithholdingShare,
    string? WithheldVatAmount,
    string? GrossAmount,
    string? PayableAmount);

/// <summary>
/// The document: sums of the line amounts. <c>state</c> <c>Computed</c>, <c>Incomplete</c> (with the counts of lines that have
/// no amount, by reason; all zero when the order has no line) or <c>AmountOutOfRange</c> (every line has an amount but a sum
/// does not fit); the net only when <c>Computed</c>, the tax sums only when <c>taxState</c> is <c>Computed</c>.
/// </summary>
public sealed record SalesDraftDocumentTotalsV1(
    string State,
    int UnpricedLineCount,
    int PriceUnitNotOrderUnitLineCount,
    int AmountOutOfRangeLineCount,
    string? NetAmount,
    string TaxState,
    string? VatAmount,
    string? WithheldVatAmount,
    string? GrossAmount,
    string? PayableAmount);
