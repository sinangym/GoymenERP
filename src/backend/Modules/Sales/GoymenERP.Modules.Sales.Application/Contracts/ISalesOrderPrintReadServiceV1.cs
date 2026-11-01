namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// MOD-07: the commercial facts the sales order PDF prints beside the detail read — under the draft read, with the
/// price facts released ONLY under the existing <c>sales.orders.price.read</c> decision (K3 P2), asked as the final
/// authority await. A caller without the price read is not refused: the answer is <see cref="SalesPriceDisclosure.Withheld"/>
/// with no currency and no line, and the document is printed without prices. Nothing here computes a total, a tax or an
/// exchange; the per-line net is the one <c>SalesLinePriceCalculator</c>'s, exactly as the line-pricing read answers it.
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
/// rejected one. Not a price fact: set with or without the price read.
/// </summary>
public sealed record SalesOrderPrintPricingV1(
    string Version,
    SalesPriceDisclosure PriceDisclosure,
    string? CurrencyCode,
    IReadOnlyList<SalesDraftLinePricingV1> Lines,
    SalesOrderPrintRevisionV1? AcceptedRevision = null);

/// <summary>SAT-D165: the revision the PDF prints instead of the current one ("Rev.N") and its lines in line order.</summary>
public sealed record SalesOrderPrintRevisionV1(string Revision, IReadOnlyList<SalesOrderPrintRevisionLineV1> Lines);

/// <summary>SAT-D165: one line as the printed revision has it: identity, line number, product, order unit and quantity text.</summary>
public sealed record SalesOrderPrintRevisionLineV1(Guid LineId, int LineNo, Guid ProductId, Guid OrderUnitId, string Quantity);
