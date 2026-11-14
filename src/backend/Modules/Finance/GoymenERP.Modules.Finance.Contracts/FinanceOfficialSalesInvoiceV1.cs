using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// F-005 D1 (FNS-D17…FNS-D21): the two technical names of the manual official sales invoice record. They are
/// registered declarations only, not granted (FNS-D26: the Finance and Accounting roles may hold both, but no role
/// or seed receives them here; the grant is a human gate). Maker≠approver is enforced per person.
/// </summary>
public static class FinanceOfficialSalesInvoicePermissionNamesV1
{
    public const string Record = "finance.official-sales-invoices.record";
    public const string Approve = "finance.official-sales-invoices.approve";
}

/// <summary>
/// Read is not a third permission: the host answers it for a holder of Record OR Approve (company-wide).
/// </summary>
public enum FinanceOfficialSalesInvoiceAuthorityActionV1 { Record, Approve, Read }

[JsonConverter(typeof(JsonStringEnumConverter<FinanceOfficialSalesInvoiceStatusV1>))]
public enum FinanceOfficialSalesInvoiceStatusV1 { PendingApproval = 1, Approved = 2 }

/// <summary>
/// FNS-D17: every field is mandatory. Amounts are exact decimal text (DEC-FIN-014): PayableAmount is in
/// CurrencyId, the four functional amounts are the company's functional (TL) equivalents; FNS-D21 adds the tax
/// base (matrah) TL equivalent, which is only stored and shown (no comparison rule or tolerance). No tolerance is chosen
/// here; Sales' existing matching rule stays the only comparison (FNS-D17 limits).
/// </summary>
public sealed record FinanceOfficialSalesInvoiceRecordRequestV1(
    Guid InvoiceId,
    string OfficialNo,
    string Ettn,
    Guid CustomerPartyId,
    Guid CurrencyId,
    string PayableAmount,
    string PayableFunctionalAmount,
    string TaxBaseFunctionalAmount,
    string VatFunctionalAmount,
    string WithholdingFunctionalAmount,
    Guid PdfDocumentId);

public sealed record FinanceOfficialSalesInvoiceApproveRequestV1(Guid InvoiceId, string ExpectedVersion);

/// <summary>
/// #66 6860586175 (FNS-D17 stays, PDF first): one "Kaydet" step first reserves the record id, then uploads the PDF
/// to the owner (finance.official-sales-invoice, that id), then records with the same id. The request carries
/// nothing; the server generates the id for the context company. A reservation has no money effect, no expiry and
/// is never deleted.
/// </summary>
public sealed record FinanceOfficialSalesInvoiceReservationRequestV1();

public sealed record FinanceOfficialSalesInvoiceReservationV1(
    Guid InvoiceId,
    Guid CompanyId,
    string ReservedBy,
    DateTimeOffset ReservedAtUtc);

public sealed record FinanceOfficialSalesInvoiceReservationResultV1(
    CommandOutcome Outcome,
    FinanceOfficialSalesInvoiceReservationV1? Resource,
    bool IsReplay,
    string OriginalCorrelationId,
    IReadOnlyList<ValidationIssue> Issues);

public sealed record FinanceOfficialSalesInvoiceViewV1(
    Guid InvoiceId,
    Guid CompanyId,
    string OfficialNo,
    string Ettn,
    Guid CustomerPartyId,
    Guid CurrencyId,
    string PayableAmount,
    string PayableFunctionalAmount,
    string TaxBaseFunctionalAmount,
    string VatFunctionalAmount,
    string WithholdingFunctionalAmount,
    Guid PdfDocumentId,
    int PdfDocumentVersionNo,
    string PdfSha256,
    FinanceOfficialSalesInvoiceStatusV1 Status,
    string Version,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAtUtc);

/// <summary>
/// ExistingOfficialNo is set only with the <c>pdf_already_recorded</c> refusal (#66 6860586175): the official
/// number of the record that already holds the same PDF content in this company; null otherwise.
/// </summary>
public sealed record FinanceOfficialSalesInvoiceResultV1(
    CommandOutcome Outcome,
    FinanceOfficialSalesInvoiceViewV1? Resource,
    bool IsReplay,
    string OriginalCorrelationId,
    IReadOnlyList<ValidationIssue> Issues,
    string? ExistingOfficialNo);

public sealed record FinanceOfficialSalesInvoicePageV1(
    Guid CompanyId,
    IReadOnlyList<FinanceOfficialSalesInvoiceViewV1> Items,
    string? NextCursor);

/// <summary>
/// Fresh host-owned company-wide authority. An unmapped central permission or a non-company-wide scope answers
/// Unavailable/Denied; the module never trusts a cached grant.
/// </summary>
public interface IFinanceOfficialSalesInvoiceAuthorityV1
{
    Task<PermissionDecision> CheckAsync(
        FinanceContext context,
        FinanceOfficialSalesInvoiceAuthorityActionV1 action,
        Guid invoiceId,
        CancellationToken cancellationToken);
}

public enum FinanceOfficialSalesInvoicePdfOutcomeV1
{
    Unavailable,
    Denied,
    NotFound,
    NotPdf,
    OwnerMismatch,
    NotDownloadable,
    Accepted,
}

public sealed record FinanceOfficialSalesInvoicePdfCheckV1(
    FinanceOfficialSalesInvoicePdfOutcomeV1 Outcome,
    int? VersionNo,
    string? Sha256);

/// <summary>
/// FNS-D18 host port over the Documents vault. Finance never reads document tables: the host answers with the
/// actor's own Documents read authority whether the document is a clean, downloadable PDF owned by this record
/// (owner kind finance.official-sales-invoice, owner id <paramref name="invoiceId"/>; #66 6860586175), and which
/// version/hash it is. A PDF owned by any other record or owner answers OwnerMismatch.
/// <paramref name="versionNo"/> null asks for the latest version (record); a value asks for that exact pinned
/// version (approve re-check). Any doubt answers a non-Accepted outcome.
/// </summary>
public interface IFinanceOfficialSalesInvoiceDocumentPortV1
{
    Task<FinanceOfficialSalesInvoicePdfCheckV1> CheckAsync(
        FinanceContext context,
        Guid documentId,
        Guid invoiceId,
        int? versionNo,
        CancellationToken cancellationToken);
}

/// <summary>
/// #66 6860586175: answers the Documents owner link (finance.official-sales-invoice) for a record id. Found only
/// for a reservation or record of the context company while the actor holds Record or Approve company-wide; an
/// unknown id or another company's id answers NotFound, no authority Denied. It never creates or changes anything.
/// </summary>
public interface IFinanceOfficialSalesInvoiceOwnerReadV1
{
    Task<ReadOutcome> CheckReadableAsync(
        FinanceContext context,
        Guid invoiceId,
        CancellationToken cancellationToken);
}

/// <summary>
/// FNS-D20: there is no edit, cancel or withdraw command; an approved record never changes. A reservation
/// (#66 6860586175) only pre-allocates the record id for the PDF upload; it is consumed once by the record.
/// </summary>
public interface IFinanceOfficialSalesInvoiceServiceV1
{
    Task<FinanceOfficialSalesInvoiceReservationResultV1> ReserveAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceOfficialSalesInvoiceReservationRequestV1 request,
        CancellationToken cancellationToken);

    Task<FinanceOfficialSalesInvoiceResultV1> RecordAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceOfficialSalesInvoiceRecordRequestV1 request,
        CancellationToken cancellationToken);

    Task<FinanceOfficialSalesInvoiceResultV1> ApproveAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceOfficialSalesInvoiceApproveRequestV1 request,
        CancellationToken cancellationToken);

    Task<ReadResult<FinanceOfficialSalesInvoiceViewV1>> GetAsync(
        FinanceContext context,
        Guid invoiceId,
        CancellationToken cancellationToken);

    Task<ReadResult<FinanceOfficialSalesInvoicePageV1>> ListAsync(
        FinanceContext context,
        bool pendingOnly,
        string? cursor,
        CancellationToken cancellationToken);
}
