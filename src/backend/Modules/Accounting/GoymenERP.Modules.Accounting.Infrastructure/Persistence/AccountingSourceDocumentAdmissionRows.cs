namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>
/// B-04b: one admission of one source document: the attempt record and, once final, the terminal refusal record. Status
/// 1 = waiting (a mapping gap, retryable), 2 = admitted, 3 = refused. Only its status facts change.
/// </summary>
internal sealed class AccountingSourceDocumentAdmissionRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid SourceDocumentId { get; set; }
    public string DocumentKind { get; set; } = string.Empty;
    public Guid InvoiceId { get; set; }
    public long FinalizedVersion { get; set; }
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public Guid SourceActorId { get; set; }
    public int Status { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public Guid? VoucherDraftId { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset FirstAttemptedAtUtc { get; set; }
    public DateTimeOffset LastAttemptedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>B-04b: the draft voucher of one admitted source document. Immutable; its approval is a later slice.</summary>
internal sealed class AccountingSourceDocumentVoucherDraftRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public string DocumentKind { get; set; } = string.Empty;
    public Guid SourceDocumentId { get; set; }
    public Guid InvoiceId { get; set; }
    public long FinalizedVersion { get; set; }
    public Guid SourceActorId { get; set; }
    public Guid PartyId { get; set; }
    public Guid CurrencyId { get; set; }
    public string? ExchangeRate { get; set; }
    public DateOnly DocumentDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public Guid? PeriodId { get; set; }
    public long MappingVersion { get; set; }
    public int Status { get; set; }
    public decimal DocumentNet { get; set; }
    public decimal DocumentVat { get; set; }
    public decimal DocumentWithholding { get; set; }
    public decimal DocumentPayable { get; set; }
    public decimal FunctionalNet { get; set; }
    public decimal FunctionalVat { get; set; }
    public decimal FunctionalWithholding { get; set; }
    public decimal FunctionalPayable { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>B-04b: one line of a draft voucher: Side 1 = debit, 2 = credit.</summary>
internal sealed class AccountingSourceDocumentVoucherDraftLineRow : IAccountingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid DraftId { get; set; }
    public int LineNumber { get; set; }
    public string MappingKey { get; set; } = string.Empty;
    public int Side { get; set; }
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
    public int? SourceLineNumber { get; set; }
    public string? VatRateCode { get; set; }
}
