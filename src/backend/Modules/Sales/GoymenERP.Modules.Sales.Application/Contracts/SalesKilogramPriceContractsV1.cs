using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// SAT-D436 / SAT-D439 (AK-5): whether the kg price read is computed or still closed behind its formula golden (SAT-D265 (5)).
/// <c>FormulaGoldenPending</c> carries no figure at all — never a zero or an estimate.
/// </summary>
public static class SalesKilogramPriceStatesV1
{
    public const string Computed = "Computed";
    public const string FormulaGoldenPending = "FormulaGoldenPending";
}

/// <summary>One kg price: <c>state</c> is a <c>SalesKilogramPriceState</c> member name; <c>value</c> is set only when it is <c>Shown</c>
/// (invariant text at the document currency's 0,01; the currency is the document's, no rate — SAT-D439).</summary>
public sealed record SalesKilogramPriceViewV1(string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Value = null);

/// <summary>
/// One order line's kg facts (SAT-D436): its theoretical kg (quantity × the kg/unit pinned at approval, SAT-D437; a kg line's
/// quantity) and theoretical kg price, and its realization — invoiced quantity, invoiced net amount, the invoiced shares' scale kg
/// and actual kg price, and the difference to the invoiced quantity's theoretical kg (kg and signed percent, TK8).
/// </summary>
public sealed record SalesOrderLineKilogramPricesV1(
    Guid LineId,
    int LineNo,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TheoreticalKg,
    SalesKilogramPriceViewV1 TheoreticalPrice,
    string InvoicedQuantity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? InvoicedAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ActualKg,
    SalesKilogramPriceViewV1 ActualPrice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? KgDifference,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DeviationPercent);

/// <summary>
/// GET …/drafts/{id}/kilogram-prices (draft read AND price read): the order's kg prices and realization. While the formula golden
/// is not in main <c>state</c> is <c>FormulaGoldenPending</c> and nothing else is set. The document figures are shown only while
/// every (invoiced) line has that kind of kg (the fail-closed interim rule; question 28 / N2 open).
/// </summary>
public sealed record SalesOrderKilogramPricesResponse(
    string State,
    Guid CurrencyId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SalesOrderLineKilogramPricesV1>? Lines = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SalesKilogramPriceViewV1? DocumentTheoreticalPrice = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SalesKilogramPriceViewV1? DocumentActualPrice = null);

/// <summary>One invoice line's kg prices (theoretical and actual, SAT-D434/D436).</summary>
public sealed record SalesInvoiceLineKilogramPricesV1(Guid LineId, SalesKilogramPriceViewV1 TheoreticalPrice,
    SalesKilogramPriceViewV1 ActualPrice);

/// <summary>
/// AK-5: an invoice's kg prices — present only on an invoice with a piece line. <c>FormulaGoldenPending</c> carries no figure.
/// </summary>
public sealed record SalesInvoiceKilogramPricesV1(
    string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SalesInvoiceLineKilogramPricesV1>? Lines = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SalesKilogramPriceViewV1? DocumentTheoreticalPrice = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SalesKilogramPriceViewV1? DocumentActualPrice = null);
