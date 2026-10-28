using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// API-30 / API-31 (Satış hazırlık planı v1 §3.3): the wire constants of the two read-only Sales routes over the Logistics dispatch
/// reads. Both live under <c>/api/v1/sales/</c>; neither writes anything in any module.
/// </summary>
public static class SalesDispatchApiV1
{
    public const string DispatchesPath = "/api/v1/sales/dispatches";
    public const string InvoiceCandidatesPath = "/api/v1/sales/invoice-candidates";

    /// <summary>The list's default and maximum page size (the Sales list convention, 1–100).</summary>
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 100;

    /// <summary>
    /// API-31: the most posted shares one answer groups. A technical read bound (not a business threshold): when more exist the
    /// answer says <c>truncated</c> and the screen narrows by customer, so a group is never silently cut.
    /// </summary>
    public const int MaximumCandidateShares = 500;
}

/// <summary>API-30 / API-31 refusal codes beside the shared Sales ones.</summary>
public static class SalesDispatchProblemCodesV1
{
    /// <summary>503: the Logistics dispatch owner is not composed, refused or answered something malformed.</summary>
    public const string DispatchProviderUnavailable = "dispatch_provider_unavailable";
}

/// <summary>
/// The permission bindings of the two reads (the Sales owner's names). API-30 reads under the order read name (plan §3.3);
/// API-31 under its own <see cref="SalesPermissionManifestV1.InvoicesRead"/>. A null or unregistered name is a 503 setup problem.
/// </summary>
public sealed record SalesDispatchReadBindings(
    string? DispatchReadPermission = SalesPermissionManifestV1.DraftsRead,
    string? InvoiceCandidateReadPermission = SalesPermissionManifestV1.InvoicesRead);

/// <summary>API-30 query: the page size, the opaque company-bound cursor and an optional exact dispatch-note state.</summary>
public sealed record SalesDispatchListQuery(int PageSize, string? Cursor, string? State);

/// <summary>API-31 query: an optional customer company account narrowing the candidates.</summary>
public sealed record SalesInvoiceCandidateQuery(Guid? CompanyAccountId);

/// <summary>The dispatch note's lifecycle on the wire.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesDispatchNoteStateView>))]
public enum SalesDispatchNoteStateView
{
    Draft = 1,
    Approved = 2,
    Cancelled = 3,
}

/// <summary>
/// Whether the invoicing side of a share is known. Only <see cref="NoSource"/> exists today: no invoice owner records which shares
/// were invoiced (API-32…36 are later S5 slices), so nothing is shown as invoiced or as not invoiced.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesInvoicedStateView>))]
public enum SalesInvoicedStateView
{
    NoSource = 0,
}

/// <summary>The customer as the Cari owner presents it now (code, legal name, optional trade name) beside its identities.</summary>
public sealed record SalesDispatchCustomerView(
    Guid PartyId,
    Guid CompanyAccountId,
    string Code,
    string LegalName,
    string? TradeName);

/// <summary>
/// One dispatch-note line (one definite shipment) with the Sales order reference composed from the Sales store: the order's
/// document number, the line number and the product as the order line recorded it, and the shipped quantity exactly as Logistics
/// recorded it in the line's unit (Ö2 text, never converted or rounded). No price.
/// </summary>
public sealed record SalesDispatchLineView(
    int LineNo,
    Guid SalesOrderId,
    string SalesOrderDocumentNo,
    Guid SalesOrderLineId,
    int SalesOrderLineNo,
    Guid ShipmentId,
    DateTimeOffset ShippedAtUtc,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid UnitId,
    string UnitCode,
    string Quantity);

/// <summary>
/// API-30: one sales-direction dispatch note (irsaliye). <c>id</c> is the note's id; the system number is null while a draft;
/// <c>invoicing</c> is <see cref="SalesInvoicedStateView.NoSource"/> until an invoice owner exists.
/// </summary>
public sealed record SalesDispatchSummaryView(
    Guid Id,
    Guid CompanyId,
    Guid InstructionId,
    SalesDispatchNoteStateView State,
    string? SystemNumber,
    string? EDispatchNumber,
    SalesDispatchCustomerView Customer,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    SalesInvoicedStateView Invoicing,
    IReadOnlyList<SalesDispatchLineView> Lines);

/// <summary>API-30: one page, newest note first; <c>nextCursor</c> null at the end.</summary>
public sealed record SalesDispatchListResponse(
    IReadOnlyList<SalesDispatchSummaryView> Items,
    string? NextCursor);

/// <summary>
/// SAT-D101: the parts of the invoice candidate key whose owner does not answer yet. Only these two can be unresolved; company,
/// customer (which also covers the customer's e-Fatura / e-Arşiv status, SAT-D102) and currency are always read, and a missing
/// due date is its own "vade eksik" group (<see cref="SalesInvoiceCandidateGroupView.DueDateMissing"/>), never an unresolved part.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesInvoiceGroupKeyPart>))]
public enum SalesInvoiceGroupKeyPart
{
    /// <summary>The rate VALUE of the share's rate day (no rate record yet); the group is still split by the shipment day.</summary>
    ExchangeRate = 1,

    /// <summary>The UBL-TR invoice type (SATIS / TEVKIFAT / ISTISNA / IHRACKAYITLI) — no source until the line tax decision (SAT-D93).</summary>
    InvoiceType = 2,
}

/// <summary>Whether the invoice rate applies to a group and how far it is known (SAT-D90, SAT-D101).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesInvoiceRateStateView>))]
public enum SalesInvoiceRateStateView
{
    /// <summary>A Turkish-lira order: no rate (SAT-D101 (b) "TL siparişte yok sayılır").</summary>
    NotApplicable = 0,

    /// <summary>The rate of the business day before the shipment day could not be resolved (no rate record): grouped by that day.</summary>
    Unresolved = 1,

    /// <summary>The previous-business-day TCMB rate (SAT-D54/D55 resolver) is known: grouped by its bulletin day and value.</summary>
    Resolved = 2,
}

/// <summary>
/// SAT-D90 / SAT-D101 (b): the invoice rate of a group — the TCMB (DEC-FIN-010) rate of the business day before the SHIPMENT day
/// (Europe/Istanbul, SAT-D10), resolved by the SAT-D54/D55 resolver. <c>shipmentDate</c> is set while the value is unresolved (the
/// group never merges two shipment days then); <c>bulletinDate</c> and <c>rate</c> once resolved (the group is the rate day and
/// value); <c>reasonCode</c> is the resolver's stable reason when unresolved. All null for a Turkish-lira order.
/// </summary>
public sealed record SalesInvoiceGroupRateView(
    SalesInvoiceRateStateView State,
    DateOnly? ShipmentDate,
    DateOnly? BulletinDate,
    string? Rate,
    string? ReasonCode);

/// <summary>
/// SAT-D101: a group's candidate key — (company, customer account, currency, rate day/value, due date, invoice type). The customer
/// covers the e-Fatura / e-Arşiv status (SAT-D102). <c>dueDate</c> is null while the order has no due-date source (no vade model,
/// API-04) — the group is then "vade eksik" and not invoiceable; <c>invoiceType</c> is null until its source exists.
/// </summary>
public sealed record SalesInvoiceGroupKeyView(
    Guid CompanyId,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    SalesInvoiceGroupRateView ExchangeRate,
    DateOnly? DueDate,
    string? InvoiceType);

/// <summary>One unit's total of a group's shipped quantities (units are never added together; Ö2 text) with the order unit's code.</summary>
public sealed record SalesInvoiceQuantityTotalView(Guid UnitId, string UnitCode, string Quantity);

/// <summary>
/// One posted share an invoice may take: the approved note (system number, optional e-İrsaliye number, approval time), its line,
/// the shipment, its time and its Europe/Istanbul shipment day (the rate day's reference, SAT-D90), the Sales order line (document number, line number, product as recorded), the unit and the exact
/// shipped quantity; <c>invoicing</c> stays <see cref="SalesInvoicedStateView.NoSource"/> until an invoice owner exists. No price.
/// </summary>
public sealed record SalesInvoiceCandidateShareView(
    Guid DispatchNoteId,
    string DispatchSystemNumber,
    string? EDispatchNumber,
    DateTimeOffset ApprovedAtUtc,
    int DispatchLineNo,
    Guid ShipmentId,
    DateTimeOffset ShippedAtUtc,
    DateOnly ShipmentDate,
    Guid SalesOrderId,
    string SalesOrderDocumentNo,
    Guid SalesOrderLineId,
    int SalesOrderLineNo,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid UnitId,
    string UnitCode,
    string Quantity,
    SalesInvoicedStateView Invoicing);

/// <summary>
/// API-31: one candidate group — every share with the same SAT-D101 key, i.e. the largest compatible group per customer (SAT-D87;
/// splitting a group and taking partial quantities belong to the invoice draft, API-32). <c>dueDateMissing</c> marks a "vade eksik"
/// group (no due-date source): it is never invoiceable (fail-closed). <c>invoiceable</c> is true only when the due date is known and
/// no key part is unresolved — today never, so a group is a preparation view, never a claim that its shares may share one invoice.
/// </summary>
public sealed record SalesInvoiceCandidateGroupView(
    SalesInvoiceGroupKeyView Key,
    SalesDispatchCustomerView Customer,
    bool DueDateMissing,
    IReadOnlyList<SalesInvoiceGroupKeyPart> UnresolvedKeyParts,
    bool Invoiceable,
    int DispatchNoteCount,
    IReadOnlyList<SalesInvoiceQuantityTotalView> QuantityTotals,
    IReadOnlyList<SalesInvoiceCandidateShareView> Shares);

/// <summary>
/// API-31 response: the groups (oldest first share's approval first), whether the owner had more shares than one answer holds,
/// and the invoiced-state source (no invoice owner: every approved share is listed). Prices and totals are not part of this
/// version: the order totals calculator (API-03) is not on main, and the screen never computes money.
/// </summary>
public sealed record SalesInvoiceCandidatesResponse(
    IReadOnlyList<SalesInvoiceCandidateGroupView> Groups,
    bool Truncated,
    SalesInvoicedStateView InvoicedState);

/// <summary>API-30 / API-31 use cases (read-only).</summary>
public interface ISalesDispatchReadService
{
    Task<SalesDispatchListResponse> GetDispatchesAsync(SalesDispatchListQuery query, CancellationToken cancellationToken = default);

    Task<SalesInvoiceCandidatesResponse> GetInvoiceCandidatesAsync(SalesInvoiceCandidateQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The Sales-own read of the order facts a dispatch share needs (document number, customer, currency, and per line its number, product and order unit). Implemented over the Sales store's company-filtered tables; an unknown or
/// another company's order is simply absent from the answer.
/// </summary>
public interface ISalesOrderDispatchReferenceReader
{
    Task<IReadOnlyList<SalesOrderDispatchReference>> ReadOrderReferencesAsync(IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken = default);
}

/// <summary>One order's facts as the Sales store holds them now.</summary>
public sealed record SalesOrderDispatchReference(
    Guid OrderId,
    Guid CompanyId,
    string DocumentNo,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    IReadOnlyList<SalesOrderDispatchLineReference> Lines);

/// <summary>One order line's number, product (as recorded on the line) and order unit (id, code, dimension).</summary>
public sealed record SalesOrderDispatchLineReference(
    Guid LineId,
    int LineNo,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid OrderUnitId,
    string OrderUnitCode,
    bool OrderUnitIsKilogram);
