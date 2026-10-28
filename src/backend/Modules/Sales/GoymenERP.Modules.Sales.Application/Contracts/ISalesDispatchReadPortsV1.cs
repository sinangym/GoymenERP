namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// MOD-06 / API-30 / API-31 (Satış hazırlık planı v1 §3.3, §5): how the dispatch owner answered one read. The two ports below
/// never throw for an expected refusal: a missing company context is <see cref="Denied"/>, an owner failure or a malformed stored
/// record is <see cref="Unavailable"/>, a cursor of another company or a broken cursor is <see cref="InvalidCursor"/>; only
/// <see cref="Found"/> carries records. The consumer reads anything else as fail-closed (never as an empty list).
/// </summary>
public enum SalesDispatchReadOutcomeV1
{
    /// <summary>The owner cannot answer: fail-closed, never a default.</summary>
    Unavailable = 0,

    /// <summary>No resolved company context (or the owner refused it): nothing was read.</summary>
    Denied = 1,

    /// <summary>The owner's company-filtered records (an empty list is a recorded "nothing").</summary>
    Found = 2,

    /// <summary>The paging cursor is not this company's or cannot be decoded.</summary>
    InvalidCursor = 3,
}

/// <summary>The dispatch note's (irsaliye) lifecycle as the Logistics owner records it (AT-055; user decision 2026-09-30).</summary>
public enum SalesDispatchNoteStateV1
{
    Draft = 1,
    Approved = 2,
    Cancelled = 3,
}

/// <summary>
/// API-30 (plan §3.3, AT-025): the Sales-declared read port the LOGISTICS owner answers with the company's SALES-direction dispatch
/// notes — the irsaliye prepared from definite shipments of a customer loading instruction. A purchase receipt (Purchasing's) or
/// a transfer document (Logistics's own transfer record, DEC-STK-008) is never a sales dispatch note and never appears here, so
/// sales and purchase documents do not mix in this list (AT-025).
/// <para>
/// The owner implements it over its own company-filtered tables and registers it in its own module (the
/// <see cref="ISalesOrderCustomerReturnReadPortV1"/> shape); Sales never reads Logistics tables and Logistics never reads Sales
/// tables — the Sales service composes the order references from its own store. The port carries NO authority: the Sales read
/// measures its own name before and after asking. It CREATES NOTHING (no attempt, audit or version effect).
/// </para>
/// </summary>
public interface ISalesDispatchNoteReadPortV1
{
    /// <summary>
    /// One page of the context company's dispatch notes, newest first (creation time, then id), each with every line it holds
    /// (a cancelled note keeps its released lines as history). <paramref name="query"/>'s cursor is the owner's opaque,
    /// company-bound cursor of the previous page.
    /// </summary>
    Task<SalesDispatchNotePageAnswerV1> ListSalesDispatchNotesAsync(SalesDispatchNotePageQueryV1 query,
        CancellationToken cancellationToken = default);
}

/// <summary>A page request: 1–100 notes, the owner's cursor, and an optional exact lifecycle state.</summary>
public sealed record SalesDispatchNotePageQueryV1(int PageSize, string? Cursor, SalesDispatchNoteStateV1? State);

/// <summary>The owner's answer: its outcome, the company it read under, the notes and the next cursor (null at the end).</summary>
public sealed record SalesDispatchNotePageAnswerV1(
    SalesDispatchReadOutcomeV1 Outcome,
    Guid CompanyId,
    IReadOnlyList<SalesDispatchNoteV1> Notes,
    string? NextCursor);

/// <summary>
/// One dispatch note: its instruction (one company, one customer — party and company account), the lifecycle, the system number
/// given on approval (null while a draft), the optional hand-entered e-İrsaliye number and the step times as recorded.
/// </summary>
public sealed record SalesDispatchNoteV1(
    Guid DispatchNoteId,
    Guid InstructionId,
    SalesDispatchNoteStateV1 State,
    Guid CustomerPartyId,
    Guid CompanyAccountId,
    string? SystemNumber,
    string? EDispatchNumber,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    IReadOnlyList<SalesDispatchShareV1> Lines);

/// <summary>
/// One dispatch-note line = one definite shipment of one instruction line, bound to the Sales order line it serves (LOG-4 D4),
/// with the shipment's recorded time and its exact SHIPPED quantity in the instruction line's unit (Ö2: never converted, never
/// rounded, nothing priced).
/// </summary>
public sealed record SalesDispatchShareV1(
    int LineNo,
    Guid SalesOrderId,
    Guid SalesOrderLineId,
    Guid ShipmentId,
    DateTimeOffset ShippedAtUtc,
    Guid ProductId,
    Guid UnitId,
    decimal Quantity);

/// <summary>
/// Whether the "not yet invoiced" half of a share is measured. No invoice owner exists on main (API-32…36 are later S5 slices,
/// SAT-D7), so the owner answers every posted share and says it could not subtract invoiced ones: <see cref="NoSource"/>.
/// A later invoice owner adds a measured state; until then nothing is assumed invoiced or uninvoiced.
/// <para>
/// Deliberately NOT a dispatch → invoice link: Sinan's S5 answer (2026-10-28, EK13 pending) adds an invoice-first flow (depo
/// teslim) where one invoice is issued against the order before its dispatch notes, the notes are attached to that invoice later,
/// and an excess over the invoiced quantity gets an extra invoice. "Invoiced" must therefore be measured by the invoice owner per
/// order line (invoiced quantity against shipped quantity), so this port reports shipment facts only and never claims a share is
/// free of an invoice.
/// </para>
/// </summary>
public enum SalesInvoicedStateSourceV1
{
    /// <summary>No invoice record exists to subtract: every approved share is listed, none is claimed uninvoiced.</summary>
    NoSource = 0,
}

/// <summary>
/// MOD-06 / API-31 (plan §3.3, §5; AT-055; DEC-SAL-006): the Sales-declared read port the LOGISTICS owner answers with the posted
/// sales shipment shares an invoice may later take — the lines of APPROVED, not cancelled dispatch notes (a released line of a
/// cancelled note is never a share; Sinan's S5 answer of 2026-10-28: a shipment becomes invoiceable once its dispatch note is
/// issued and approved). The smallest invoiceable unit is one dispatch-note line × its order share (dispatch/invoice
/// design spec §3.3). AT-055's single link is the owner's: a shipment is on at most one non-cancelled note, so a share appears once.
/// <para>
/// The same owner rules as <see cref="ISalesDispatchNoteReadPortV1"/>: company-filtered, no authority of its own, creates nothing,
/// no Sales table read. Prices, taxes, rates and due dates are NOT the owner's and are not here.
/// </para>
/// </summary>
public interface ISalesInvoiceableShipmentReadPortV1
{
    /// <summary>
    /// The context company's posted shares, oldest approval first (approval time, note id, line number), at most
    /// <see cref="SalesInvoiceableShipmentQueryV1.Limit"/>; <see cref="SalesInvoiceableShipmentAnswerV1.HasMore"/> says more exist.
    /// An optional company account narrows the read to one customer's notes.
    /// </summary>
    Task<SalesInvoiceableShipmentAnswerV1> ReadInvoiceableShipmentsAsync(SalesInvoiceableShipmentQueryV1 query,
        CancellationToken cancellationToken = default);
}

/// <summary>The read's bounds: an optional customer company account and the number of shares (1 or more).</summary>
public sealed record SalesInvoiceableShipmentQueryV1(Guid? CompanyAccountId, int Limit);

/// <summary>The owner's answer: its outcome, the company it read under, the shares, whether more exist, and the invoiced-state source.</summary>
public sealed record SalesInvoiceableShipmentAnswerV1(
    SalesDispatchReadOutcomeV1 Outcome,
    Guid CompanyId,
    IReadOnlyList<SalesInvoiceableShipmentV1> Shares,
    bool HasMore,
    SalesInvoicedStateSourceV1 InvoicedState);

/// <summary>
/// One posted share: its approved note (system number, optional e-İrsaliye number, approval time), the note's customer, the note
/// line, the shipment with its recorded time, the Sales order line it serves, the product, the unit and the exact shipped quantity.
/// </summary>
public sealed record SalesInvoiceableShipmentV1(
    Guid DispatchNoteId,
    string SystemNumber,
    string? EDispatchNumber,
    DateTimeOffset ApprovedAtUtc,
    Guid CustomerPartyId,
    Guid CompanyAccountId,
    int NoteLineNo,
    Guid ShipmentId,
    DateTimeOffset ShippedAtUtc,
    Guid SalesOrderId,
    Guid SalesOrderLineId,
    Guid ProductId,
    Guid UnitId,
    decimal Quantity);
