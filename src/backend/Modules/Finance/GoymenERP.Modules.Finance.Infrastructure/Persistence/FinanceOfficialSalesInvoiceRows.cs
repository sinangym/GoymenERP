namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

/// <summary>
/// F-005 D1 (FNS-D17/D18/D20): the manual official sales invoice record. It is inserted once as PendingApproval and
/// may change exactly once, to Approved, by a different actor (maker≠approver, also a database constraint). An
/// approved row never changes; there is no edit, cancel or withdraw path.
/// </summary>
internal sealed class FinanceOfficialSalesInvoiceRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string OfficialNo { get; set; } = string.Empty;
    public string Ettn { get; set; } = string.Empty;
    public Guid CustomerPartyId { get; set; }
    public Guid CurrencyId { get; set; }
    public string PayableAmountText { get; set; } = string.Empty;
    public string PayableFunctionalAmountText { get; set; } = string.Empty;
    public string TaxBaseFunctionalAmountText { get; set; } = string.Empty;
    public string VatFunctionalAmountText { get; set; } = string.Empty;
    public string WithholdingFunctionalAmountText { get; set; } = string.Empty;
    public Guid PdfDocumentId { get; set; }
    public int PdfDocumentVersionNo { get; set; }
    public string PdfSha256 { get; set; } = string.Empty;
    public int Status { get; set; }
    public long Version { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? CausationId { get; set; }
}

/// <summary>
/// ADR-011 command receipt local to the official sales invoice reserve/record/approve commands. Append-only.
/// </summary>
internal sealed class FinanceOfficialSalesInvoiceCommandRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public Guid InvoiceId { get; set; }
    public string ResultJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>
/// #66 6860586175 (PDF first): a server-generated record id reserved by one actor in one company before the PDF
/// upload, so the PDF can be owned by (finance.official-sales-invoice, Id). The record consumes it once (the record's
/// primary key is the same id and references this row). No money effect; append-only, never deleted and no expiry is
/// chosen here.
/// </summary>
internal sealed class FinanceOfficialSalesInvoiceReservationRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ReservedBy { get; set; } = string.Empty;
    public DateTimeOffset ReservedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? CausationId { get; set; }
}
