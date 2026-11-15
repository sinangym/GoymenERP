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
    public int? ProducerSchemaVersion { get; set; }
    public Guid BatchId { get; set; }
    public string ResultJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>Immutable owner delivery for the internal Work admission bridge.</summary>
internal sealed class FinanceOfficialInvoiceImportWorkDeliveryRow : IFinanceTenantRow
{
    public Guid DeliveryId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CommandReceiptId { get; set; }
    public Guid BatchId { get; set; }
    public string SourceKey { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = string.Empty;
    public long ObservationOrdinal { get; set; }
    public int DesiredState { get; set; }
    public Guid EvidenceId { get; set; }
    public int PayloadSchemaVersion { get; set; }
    public byte[] PayloadUtf8 { get; set; } = [];
    public string PayloadSha256 { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? CausationId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

/// <summary>
/// FIN-S5 / FNS-D19 (D5): an append-only claim of one official identity (official number or ETTN) by the source
/// document of an admitted v2 file. The live holder is the claim with the highest generation; a later acceptance may
/// add the next generation only after every open item of the holder document is cancelled. A unique generation per
/// identity makes two concurrent acceptances of the same identity collide in SQL Server.
/// </summary>
internal sealed class FinanceOfficialInvoiceIdentityClaimRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string IdentityKind { get; set; } = string.Empty;
    public string IdentityValue { get; set; } = string.Empty;
    public int Generation { get; set; }
    public Guid HolderBatchId { get; set; }
    public Guid HolderSourceDocumentId { get; set; }
    public string ClaimedBy { get; set; } = string.Empty;
    public DateTimeOffset ClaimedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? CausationId { get; set; }
}

/// <summary>The two official identity kinds a claim can carry.</summary>
internal static class FinanceOfficialInvoiceIdentityKinds
{
    public const string OfficialNo = "official-no";
    public const string Ettn = "ettn";
}
