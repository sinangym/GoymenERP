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
/// DEC-SAL-006: the parts of the invoice grouping key. The decision names company, customer, currency, tax, exchange rate,
/// payment terms / commercial conditions and e-document compatibility; a part is <c>resolved</c> only when its owner answers it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesInvoiceGroupKeyPart>))]
public enum SalesInvoiceGroupKeyPart
{
    Company = 1,
    Customer = 2,
    Currency = 3,
    Tax = 4,
    ExchangeRate = 5,
    PaymentTerms = 6,
    EDocument = 7,
}

/// <summary>An owner reference at the version the order recorded it (e.g. the payment terms), or null when the order holds none.</summary>
public sealed record SalesInvoiceGroupReferenceView(Guid Id, string Version);

/// <summary>
/// The resolved part of a group's DEC-SAL-006 key: company, customer (party + company account) and the order currency are always
/// read; the payment terms reference only when the order holds one. Tax, exchange rate and e-document compatibility have no owner
/// on main: they are listed in <see cref="SalesInvoiceCandidateGroupView.UnresolvedKeyParts"/>, never assumed equal.
/// </summary>
public sealed record SalesInvoiceGroupKeyView(
    Guid CompanyId,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    SalesInvoiceGroupReferenceView? PaymentTerms);

/// <summary>One unit's total of a group's shipped quantities (units are never added together; Ö2 text) with the order unit's code.</summary>
public sealed record SalesInvoiceQuantityTotalView(Guid UnitId, string UnitCode, string Quantity);

/// <summary>
/// One posted share an invoice may take: the approved note (system number, optional e-İrsaliye number, approval time), its line,
/// the shipment and its time, the Sales order line (document number, line number, product as recorded), the unit and the exact
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
/// API-31: one candidate group — every share with the same resolved key, i.e. the largest compatible group per customer (Sinan's
/// S5 answer of 2026-10-28: the default proposal; splitting a group and taking partial quantities belong to the invoice draft,
/// API-32). <c>complete</c> is true only when no key part is
/// unresolved; until the tax, rate and e-document owners exist it is false, so a group is a preparation view, never a claim that
/// its shares may share one invoice.
/// </summary>
public sealed record SalesInvoiceCandidateGroupView(
    SalesInvoiceGroupKeyView Key,
    SalesDispatchCustomerView Customer,
    IReadOnlyList<SalesInvoiceGroupKeyPart> UnresolvedKeyParts,
    bool Complete,
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
/// The Sales-own read of the order facts a dispatch share needs (document number, customer, currency, payment terms reference,
/// and per line its number, product and order unit). Implemented over the Sales store's company-filtered tables; an unknown or
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
    Guid? PaymentTermsId,
    string? PaymentTermsVersion,
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
