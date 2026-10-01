namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

/// <summary>The immutable exact carrier and canonical row snapshot uploaded for later approval.</summary>
internal sealed class FinanceOfficialInvoiceImportBatchRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string BatchVersion { get; set; } = string.Empty;
    public string BatchChecksum { get; set; } = string.Empty;
    public string TemplateVersion { get; set; } = string.Empty;
    public byte[] FileBytes { get; set; } = [];
    public string SourceRowsJson { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public DateTimeOffset UploadedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? CausationId { get; set; }
}

/// <summary>Append-only maker-checker decision; the upload row is never updated.</summary>
internal sealed class FinanceOfficialInvoiceImportApprovalRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BatchId { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = string.Empty;
    public DateTimeOffset ApprovedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? CausationId { get; set; }
}

/// <summary>ADR-011 command receipt local to official-file upload and approval.</summary>
internal sealed class FinanceOfficialInvoiceImportCommandRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public Guid BatchId { get; set; }
    public string ResultJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}
