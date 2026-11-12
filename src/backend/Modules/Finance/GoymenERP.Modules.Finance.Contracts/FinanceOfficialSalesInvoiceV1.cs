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

public sealed record FinanceOfficialSalesInvoiceResultV1(
    CommandOutcome Outcome,
    FinanceOfficialSalesInvoiceViewV1? Resource,
    bool IsReplay,
    string OriginalCorrelationId,
    IReadOnlyList<ValidationIssue> Issues);

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
/// actor's own Documents read authority whether the document is a clean, downloadable PDF linked to the customer
/// party, and which version/hash it is. <paramref name="versionNo"/> null asks for the latest version (record);
/// a value asks for that exact pinned version (approve re-check). Any doubt answers a non-Accepted outcome.
/// </summary>
public interface IFinanceOfficialSalesInvoiceDocumentPortV1
{
    Task<FinanceOfficialSalesInvoicePdfCheckV1> CheckAsync(
        FinanceContext context,
        Guid documentId,
        Guid customerPartyId,
        int? versionNo,
        CancellationToken cancellationToken);
}

/// <summary>
/// FNS-D20: there is no edit, cancel or withdraw command; an approved record never changes.
/// </summary>
public interface IFinanceOfficialSalesInvoiceServiceV1
{
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
