namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// MOD-07: the commercial facts the sales order PDF prints beside the detail read — under the draft read, with the
/// price facts released ONLY under the existing <c>sales.orders.price.read</c> decision (K3 P2), asked as the final
/// authority await. A caller without the price read is not refused: the answer is <see cref="SalesPriceDisclosure.Withheld"/>
/// with no currency, no line and no totals, and the document is printed without prices. The per-line net is the one
/// <c>SalesLinePriceCalculator</c>'s, exactly as the line-pricing read answers it; the line and document amounts with VAT and
/// withholding (API-03 / SAT-D11 / SAT-D75) are the one <c>SalesOrderTotalsCalculator</c>'s, exactly as the totals read answers
/// them — the PDF prints them verbatim and never recomputes. No exchange is computed.
/// </summary>
public interface ISalesOrderPrintReadServiceV1
{
    Task<SalesOrderPrintPricingV1> GetOrderPrintPricingAsync(Guid draftId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The draft's concurrency <paramref name="Version"/> at the read (the caller binds it to the detail it prints), the
/// price disclosure and — only when <see cref="SalesPriceDisclosure.Visible"/> — the draft currency's code (the Settings
/// money profile the draft was created under) and every line's pricing in line order. SAT-D165 (EK23) "kabul edilen
/// revizyonu bassın": <paramref name="AcceptedRevision"/> is set when the customer rejected the current approved revision and the
/// order continues on the revision the customer last accepted (same customer and currency) — the PDF then prints THAT revision
/// (its lines, quantities and, with the price read, the <paramref name="Lines"/> prices, which are then its prices), never the
/// rejected one. Not a price fact: set with or without the price read. <paramref name="Totals"/> (MOD-07 → API-03): only when
/// <see cref="SalesPriceDisclosure.Visible"/> — the printed revision's line and document totals with VAT and withholding from the
/// one totals calculator; null when withheld, and null under a visible answer only when the printed revision's amounts cannot be
/// read (the PDF is then not printed, never printed without or with guessed totals).
/// </summary>
public sealed record SalesOrderPrintPricingV1(
    string Version,
    SalesPriceDisclosure PriceDisclosure,
    string? CurrencyCode,
    IReadOnlyList<SalesDraftLinePricingV1> Lines,
    SalesOrderPrintRevisionV1? AcceptedRevision = null,
    SalesOrderPrintTotalsV1? Totals = null);

/// <summary>
/// MOD-07 → API-03 (SAT-D11, SAT-D75): the printed revision's totals — <paramref name="Revision"/> names the revision they were
/// computed for (the current one, or SAT-D165's accepted one), <paramref name="Document"/> and every line in line order exactly as
/// the totals read wires them (amounts in the order currency padded to the money profile's scale; a state that is not
/// <c>Computed</c> has no amount, never 0).
/// </summary>
public sealed record SalesOrderPrintTotalsV1(
    string Revision,
    SalesDraftDocumentTotalsV1 Document,
    IReadOnlyList<SalesOrderPrintLineTotalV1> Lines);

/// <summary>
/// One printed line's totals (<paramref name="Total"/>) with the line tax snapshot's printable facts: the withholding percent the
/// share was taken from (only when withholding applies) and the exemption / withholding codes — each only when the Settings
/// resolution says <c>Resolved</c>, verbatim. Nothing here is computed.
/// </summary>
public sealed record SalesOrderPrintLineTotalV1(
    SalesDraftLineTotalV1 Total,
    string? WithholdingPercent,
    string? ExemptionCode,
    string? WithholdingCode);

/// <summary>
/// SAT-D165: the revision the PDF prints instead of the current one ("Rev.N"), its lines in line order and its OWN approval
/// facts (who submitted and approved that revision, when) — never the rejected revision's.
/// </summary>
public sealed record SalesOrderPrintRevisionV1(string Revision, IReadOnlyList<SalesOrderPrintRevisionLineV1> Lines,
    SalesDraftApprovalV1? Approval = null);

/// <summary>SAT-D165: one line as the printed revision has it: identity, line number, product, order unit and quantity text.</summary>
public sealed record SalesOrderPrintRevisionLineV1(Guid LineId, int LineNo, Guid ProductId, Guid OrderUnitId, string Quantity);
