using System.Text.Json.Serialization;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// API-41 / API-42 (Satış hazırlık planı v1 §3.4, UI-80): the wire constants of the three read-only Sales reports. All live under
/// <c>/api/v1/sales/reports/</c> and need <see cref="SalesPermissionManifestV1.ReportsRead"/>; none writes anything in any module
/// (no attempt, audit event or version effect).
/// </summary>
public static class SalesReportApiV1
{
    /// <summary>API-41 (KPI No 124): the open quantity of the approved, neither closed nor cancelled orders.</summary>
    public const string OpenOrdersPath = "/api/v1/sales/reports/open-orders";

    /// <summary>API-42 (SAT-D25): the lost quotes with their loss reason and their quoted net amount.</summary>
    public const string LostOpportunitiesPath = "/api/v1/sales/reports/lost-opportunities";

    /// <summary>API-42 (No 25): the invoice-to-order unit price variance. No invoice owner exists yet: <c>NoSource</c>.</summary>
    public const string PriceVariancePath = "/api/v1/sales/reports/price-variance";

    /// <summary>The page size default and maximum (the Sales list convention, 1–100).</summary>
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 100;

    /// <summary>
    /// API-41 technical read bounds (not business thresholds), so one request's work is bounded whatever the history holds:
    /// <list type="bullet">
    /// <item><see cref="MaximumOrdersPerGroup"/> — a group whose scope holds more orders is answered as
    /// <see cref="SalesOpenOrderGroupStateView.TooManyOrders"/> (no sums, never a cut sum) and the cursor moves past it, so the
    /// report stays reachable; the screen asks to narrow the dates for that group.</item>
    /// <item><see cref="MaximumOrdersPerRequest"/> — the orders one request measures; when the next group would exceed it the
    /// page ends early with a cursor (possibly with fewer groups, or none, than asked) and the screen continues from there.</item>
    /// <item><see cref="OrderBatchSize"/> — orders read from the store and from the Logistics owner per round trip (one order batch:
    /// one store read and at most one Logistics read). Consecutive groups of few orders share a batch: their orders are read together
    /// (at most <see cref="OrderBatchSize"/> by their scanned counts) and each group is measured over its own lines.</item>
    /// <item><see cref="MaximumOrderBatchesPerRequest"/> — the order batches one request makes, whatever the groups' sizes; when the
    /// next group (or batch of groups) would exceed it the page ends early with a cursor, like the order bound. It is the batches of
    /// one group at <see cref="MaximumOrdersPerGroup"/> plus one: a request's first group always fits, even when a shared batch
    /// found its scope grown while it was read and the first group is read again on its own — so the report always advances.</item>
    /// <item><see cref="MaximumGroupsScannedPerRequest"/> — group keys one request looks at (groups with nothing open are skipped).</item>
    /// </list>
    /// </summary>
    public const int MaximumOrdersPerGroup = 1000;
    public const int MaximumOrdersPerRequest = 1000;
    public const int OrderBatchSize = 100;
    public const int MaximumOrderBatchesPerRequest = MaximumOrdersPerGroup / OrderBatchSize + 1;
    public const int MaximumGroupsScannedPerRequest = 500;
}

/// <summary>
/// API-41 query. <paramref name="From"/> / <paramref name="To"/> bound the ORDER business date (inclusive, both optional);
/// <paramref name="CompanyAccountId"/> narrows to one customer account.
/// </summary>
public sealed record SalesOpenOrderReportQuery(DateOnly? From, DateOnly? To, Guid? CompanyAccountId, int PageSize, string? Cursor);

/// <summary>
/// API-42 lost opportunities query. <paramref name="From"/> / <paramref name="To"/> bound the LOSS day (the Europe/Istanbul
/// calendar day of the recorded loss, SAT-D10; inclusive, both optional); <paramref name="ReasonId"/> narrows to one SAT-D25 list
/// reason.
/// </summary>
public sealed record SalesLostOpportunityReportQuery(DateOnly? From, DateOnly? To, Guid? CompanyAccountId, Guid? ReasonId, int PageSize,
    string? Cursor);

/// <summary>API-42 price variance query (the invoice date range and customer; validated now, answered by the invoice owner later).</summary>
public sealed record SalesPriceVarianceReportQuery(DateOnly? From, DateOnly? To, Guid? CompanyAccountId, int PageSize, string? Cursor);

/// <summary>The customer as the Cari owner presents it now (code, legal name, optional trade name) beside its identities.</summary>
public sealed record SalesReportCustomerView(Guid PartyId, Guid CompanyAccountId, string Code, string LegalName, string? TradeName);

/// <summary>
/// API-41: what the open quantity measures. Only <see cref="ApprovedMinusShipped"/>: the quantity of the last internally approved
/// revision the customer ACCEPTED (or of the last approved revision when acceptance is not required — the owner's own rule, as the
/// shipping line standing and the invoicing gate apply it, Q9 / SAT-D113…D115) minus the definitely shipped total the Logistics
/// owner recorded, never below zero; a line the SAT-D5 realization axis judges <c>Completed</c> (shipped short WITHIN the delivery
/// tolerance) has nothing open (SAT-D130, EK18). An approved revision that awaits customer acceptance adds nothing to the open
/// quantity: what it would add is shown apart as <c>awaitingAcceptanceQuantity</c> (SAT-D137,
/// SINAN_KARARLARI_2026-10-31_SATIS_EK19.md). Lines with nothing open and nothing awaiting (fully shipped, or completed within the
/// tolerance) are closed history and not in the report.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesOpenOrderMeasureView>))]
public enum SalesOpenOrderMeasureView
{
    ApprovedMinusShipped = 1,
}

/// <summary>Why an open order line is not in a group's sums.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesOpenOrderUnmeasuredReasonView>))]
public enum SalesOpenOrderUnmeasuredReasonView
{
    /// <summary>
    /// The line in the accepted or the approved revision cannot be compared exactly with the current line (another product or unit,
    /// an unreadable quantity): nothing is measured against it.
    /// </summary>
    PendingReapproval = 1,

    /// <summary>
    /// SAT-D130: the line was shipped short and only the SAT-D5 delivery tolerance can say whether its remainder is still open, but
    /// the Settings tolerance policy for the order's business date is not readable (not composed, refused, inactive, out of window,
    /// another company's or malformed): the line is neither counted open nor closed (fail-closed).
    /// </summary>
    TolerancePolicyUnavailable = 2,

    /// <summary>The Logistics owner's answer for this order is malformed (another company or order, an unknown line, another unit,
    /// delivered above shipped): its lines show no number rather than a wrong one; the rest of the report stands.</summary>
    DeliveryAnswerInvalid = 3,
}

/// <summary>Whether a group was measured.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesOpenOrderGroupStateView>))]
public enum SalesOpenOrderGroupStateView
{
    /// <summary>Every order of the group's scope was measured (its unmeasured lines, if any, are listed).</summary>
    Measured = 1,

    /// <summary>
    /// The group's scope holds more than <see cref="SalesReportApiV1.MaximumOrdersPerGroup"/> orders: not measured, no sums (never a
    /// cut sum); narrow the order dates.
    /// </summary>
    TooManyOrders = 2,
}

/// <summary>
/// API-41: one order line of a group with something open or awaiting customer acceptance — the order, the accepted revision its
/// open quantity is measured on (null when no revision is accepted yet), and the quantities exactly in the line's order unit (Ö2
/// text: kilograms ≤ 3 decimals, pieces whole; never rounded). <c>completedWithinTolerance</c> (SAT-D130): shipped short but within
/// the SAT-D5 tolerance — remaining 0. <c>awaitingAcceptanceQuantity</c>: what the approved revision awaiting customer acceptance
/// (<c>awaitingAcceptanceRevision</c>) adds above the accepted quantity — max(0, awaiting − accepted); "0" and null when none.
/// </summary>
public sealed record SalesOpenOrderLineView(
    Guid OrderId,
    string DocumentNo,
    DateOnly BusinessDate,
    DateOnly? RequestedDeliveryDate,
    string? BasisRevision,
    Guid LineId,
    int LineNo,
    string ApprovedQuantity,
    string ShippedQuantity,
    string RemainingQuantity,
    bool CompletedWithinTolerance,
    string AwaitingAcceptanceQuantity,
    string? AwaitingAcceptanceRevision);

/// <summary>API-41: an open order line of the group that is NOT in its sums, and why.</summary>
public sealed record SalesOpenOrderUnmeasuredLineView(
    Guid OrderId,
    string DocumentNo,
    Guid LineId,
    int LineNo,
    SalesOpenOrderUnmeasuredReasonView Reason);

/// <summary>
/// API-41: one group — one customer account, one product, one order unit. The sums are taken in that ONE unit over the lines listed
/// (different units are different groups, never added); <c>complete</c> is false whenever a line of the group is unmeasured or the
/// group was not measured, so a partial sum is never shown as the whole. <c>scopeOrderCount</c> is the orders of the group's scope
/// (open-status orders, history included); <c>orderCount</c> the orders with a listed line. A <c>TooManyOrders</c> group carries
/// null sums and no lines.
/// </summary>
public sealed record SalesOpenOrderGroupView(
    SalesReportCustomerView Customer,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid UnitId,
    string UnitCode,
    SalesOpenOrderGroupStateView State,
    int ScopeOrderCount,
    int OrderCount,
    string? ApprovedQuantity,
    string? ShippedQuantity,
    string? RemainingQuantity,
    string? AwaitingAcceptanceQuantity,
    bool Complete,
    IReadOnlyList<SalesOpenOrderLineView> Lines,
    IReadOnlyList<SalesOpenOrderUnmeasuredLineView> Unmeasured);

/// <summary>
/// API-41: one page of groups with something open, awaiting acceptance or unmeasured, ordered by (customer account, party, product,
/// unit) identities. A page may hold fewer groups than asked — even none — when the request's work bound ended it: a non-null
/// <c>nextCursor</c> then continues the scan; null is the end.
/// </summary>
public sealed record SalesOpenOrderReportResponse(
    SalesOpenOrderMeasureView Measure,
    IReadOnlyList<SalesOpenOrderGroupView> Groups,
    string? NextCursor);

/// <summary>API-42: the SAT-D25 Settings list entry the loss was recorded with, as the quote snapshotted it.</summary>
public sealed record SalesLostReasonView(Guid ReasonId, string Code, string Label, Guid ProfileId, long ProfileVersion);

/// <summary>
/// API-42: the lost quote's NET amount (KDV hariç) — <see cref="SalesOrderTotalsCalculator"/>'s answer over the quote's current
/// lines and its money profile snapshot, in the quote's own currency (never converted, never added to another quote's).
/// <c>state</c> is the calculator's document state (<c>Computed</c> / <c>Incomplete</c> / <c>AmountOutOfRange</c>); the net is null
/// unless computed.
/// </summary>
public sealed record SalesLostAmountView(
    string State,
    Guid CurrencyId,
    int AmountScale,
    string? NetAmount,
    int UnpricedLineCount,
    int PriceUnitNotOrderUnitLineCount);

/// <summary>
/// API-42: one lost quote. <c>reason</c> is null for a loss recorded before SAT-D25 (its free text is then the
/// <c>explanation</c>); <c>amount</c> is null exactly when the response's <c>amountDisclosure</c> is <c>Withheld</c>.
/// </summary>
public sealed record SalesLostOpportunityView(
    Guid QuoteId,
    string DocumentNo,
    string Revision,
    DateOnly BusinessDate,
    DateOnly? ValidUntil,
    SalesReportCustomerView Customer,
    Guid CurrencyId,
    DateTimeOffset LostAtUtc,
    DateOnly LostOn,
    string LostBy,
    SalesLostReasonView? Reason,
    string? Explanation,
    int LineCount,
    SalesLostAmountView? Amount);

/// <summary>
/// API-42: one page of lost quotes, latest loss first. The amounts are prices: shown only to a holder of
/// <see cref="SalesPermissionManifestV1.OrdersPriceRead"/> (<c>Visible</c>), otherwise <c>Withheld</c> and every amount null.
/// </summary>
public sealed record SalesLostOpportunityReportResponse(
    SalesPriceDisclosure AmountDisclosure,
    IReadOnlyList<SalesLostOpportunityView> Items,
    string? NextCursor);

/// <summary>
/// API-42 (No 25): whether the invoice-to-order price variance has a source. Today <see cref="NoSource"/>: no Sales invoice owner
/// exists (S5 API-32), so no variance is known — it is NOT "no variance". <see cref="Available"/> is the contract of the invoice
/// owner's answer.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesPriceVarianceSourceStateView>))]
public enum SalesPriceVarianceSourceStateView
{
    NoSource = 0,
    Available = 1,
}

/// <summary>No 25 / SAT-D92: an invoice unit price below the order's (warning + variance record) or above it (needs an order revision).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesPriceVarianceDirectionView>))]
public enum SalesPriceVarianceDirectionView
{
    Below = 1,
    Above = 2,
}

/// <summary>
/// API-42 (No 25), the invoice owner's answer once it exists: one invoice line priced differently from its order line — both net
/// unit prices exactly as recorded, their difference and the line's difference amount in the ORDER's currency (No 26: the invoice
/// is issued in the order currency), all from the invoice calculator, never recomputed by the screen.
/// </summary>
public sealed record SalesPriceVarianceView(
    Guid InvoiceId,
    string InvoiceNo,
    DateOnly InvoiceDate,
    SalesReportCustomerView Customer,
    Guid OrderId,
    string OrderDocumentNo,
    string OrderRevision,
    Guid OrderLineId,
    int OrderLineNo,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid UnitId,
    string UnitCode,
    string InvoicedQuantity,
    Guid CurrencyId,
    string OrderNetUnitPrice,
    string InvoiceNetUnitPrice,
    string UnitPriceDifference,
    string DifferenceAmount,
    SalesPriceVarianceDirectionView Direction);

/// <summary>
/// API-42 (No 25): <c>state</c> = <c>NoSource</c> carries null <c>items</c> and <c>nextCursor</c> — never an empty list that would
/// read as "no variance"; <c>Available</c> carries the page.
/// </summary>
public sealed record SalesPriceVarianceReportResponse(
    SalesPriceVarianceSourceStateView State,
    IReadOnlyList<SalesPriceVarianceView>? Items,
    string? NextCursor);

/// <summary>The three report reads (all under <see cref="SalesPermissionManifestV1.ReportsRead"/>).</summary>
public interface ISalesReportService
{
    Task<SalesOpenOrderReportResponse> GetOpenOrderReportAsync(SalesOpenOrderReportQuery query, CancellationToken cancellationToken = default);

    Task<SalesLostOpportunityReportResponse> GetLostOpportunityReportAsync(SalesLostOpportunityReportQuery query,
        CancellationToken cancellationToken = default);

    Task<SalesPriceVarianceReportResponse> GetPriceVarianceReportAsync(SalesPriceVarianceReportQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>API-41 store filter: the order business date range (inclusive) and an optional customer account.</summary>
public sealed record SalesOpenOrderReportFilter(DateOnly? From, DateOnly? To, Guid? CompanyAccountId);

/// <summary>API-41: the identity of one group (customer account of its party, product, order unit).</summary>
public sealed record SalesOpenOrderGroupKey(Guid CompanyAccountId, Guid PartyId, Guid ProductId, Guid UnitId);

/// <summary>API-41 store answer: one group key and how many open-status orders of the filter have a current line in it.</summary>
public sealed record SalesOpenOrderGroupScope(SalesOpenOrderGroupKey Key, int OrderCount);

/// <summary>API-41 store answer: one id-ordered batch of restored open-status orders and whether another order follows it.</summary>
public sealed record SalesOpenOrderBatch(IReadOnlyList<SalesOrderDraft> Orders, bool HasMore);

/// <summary>API-42 store filter: the loss instant range [from, before) in UTC, an optional customer account and SAT-D25 reason.</summary>
public sealed record SalesLostQuoteReportFilter(DateTimeOffset? LostFromUtc, DateTimeOffset? LostBeforeUtc, Guid? CompanyAccountId,
    Guid? ReasonId);

/// <summary>
/// The Sales store's read-only report queries, under the tenant filter (another company's rows are invisible). Malformed, foreign or
/// other-filter cursors are <see cref="SalesDraftStoreException"/> with <see cref="SalesDraftStoreCodes.InvalidCursor"/>.
/// </summary>
public interface ISalesReportReadStore
{
    /// <summary>
    /// API-41: the group keys of the open-status orders — lifecycle Draft (neither cancelled nor closed) and either the current
    /// revision approved or an earlier approved revision archived — under <paramref name="filter"/>, in key order after
    /// <paramref name="after"/>, at most <paramref name="take"/>, each with its order count (one aggregate statement).
    /// </summary>
    Task<IReadOnlyList<SalesOpenOrderGroupScope>> ListOpenOrderGroupsAsync(SalesOpenOrderReportFilter filter, SalesOpenOrderGroupKey? after,
        int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// API-41: the open-status orders of the filter with a current line in a group key from <paramref name="first"/> through
    /// <paramref name="last"/> (both inclusive, in the key order of <see cref="ListOpenOrderGroupsAsync"/>; one key when they are
    /// equal), each once, in id order after <paramref name="afterOrderId"/>, at most <paramref name="take"/>, restored through the
    /// domain; <see cref="SalesOpenOrderBatch.HasMore"/> says whether another such order follows (no extra read to learn the end).
    /// </summary>
    Task<SalesOpenOrderBatch> ListOpenOrdersOfGroupsAsync(SalesOpenOrderReportFilter filter, SalesOpenOrderGroupKey first,
        SalesOpenOrderGroupKey last, Guid? afterOrderId, int take, CancellationToken cancellationToken = default);

    /// <summary>API-42: the lost quotes under <paramref name="filter"/>, latest loss first (loss instant, then id).</summary>
    Task<SalesQuotePage> ListLostQuotesAsync(SalesLostQuoteReportFilter filter, int pageSize, string? cursor,
        CancellationToken cancellationToken = default);
}
