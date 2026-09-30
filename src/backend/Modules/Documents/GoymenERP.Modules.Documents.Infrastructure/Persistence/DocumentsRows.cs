namespace GoymenERP.Modules.Documents.Infrastructure.Persistence;

internal interface IDocumentsTenantRow
{
    Guid CompanyId { get; }
}

internal sealed class OrganizationCompanyReference
{
    public Guid Id { get; set; }
}

/// <summary>
/// DEC-DOC-001's blob: one row per (company, content hash) — the physical content of this company, stored once
/// (AT-065 "tek belge bir kez saklanır"; physical dedup within the company, DEC-DOC-002). Kind and size are facts of the
/// bytes. Append-only: never updated, never deleted.
/// </summary>
internal sealed class ContentBlobRow : IDocumentsTenantRow
{
    public Guid CompanyId { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public int ContentKind { get; set; }

    public string ObjectKey { get; set; } = string.Empty;

    public string FirstStoredBy { get; set; } = string.Empty;

    public DateTimeOffset FirstStoredAtUtc { get; set; }
}

/// <summary>DEC-DOC-001's logical document: the company-scoped identity the versions and links hang from. Append-only.</summary>
internal sealed class DocumentRow : IDocumentsTenantRow
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// DEC-DOC-001/003's version: an immutable content + name + scan result of a document (a new content is a new version,
/// never an overwrite). The scan result is the version's own (DEC-DOC-009): Clean with the scanner's id, or NotScanned.
/// </summary>
internal sealed class DocumentVersionRow : IDocumentsTenantRow
{
    public Guid CompanyId { get; set; }

    public Guid DocumentId { get; set; }

    public int VersionNo { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public int ScanState { get; set; }

    public string? ScannerId { get; set; }

    public string UploadedBy { get; set; } = string.Empty;

    public DateTimeOffset UploadedAtUtc { get; set; }

    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>DEC-DOC-001's record link: the document ↔ an owner record of an owner-declared kind, in the same company.</summary>
internal sealed class DocumentLinkRow : IDocumentsTenantRow
{
    public Guid CompanyId { get; set; }

    public Guid DocumentId { get; set; }

    public string OwnerKind { get; set; } = string.Empty;

    public Guid OwnerId { get; set; }

    public string LinkedBy { get; set; } = string.Empty;

    public DateTimeOffset LinkedAtUtc { get; set; }
}

/// <summary>ADR-011's attempt: (company, command, key hash) → the document it stored; the raw key is never stored.</summary>
internal sealed class DocumentAttemptRow : IDocumentsTenantRow
{
    public Guid CompanyId { get; set; }

    public string CommandName { get; set; } = string.Empty;

    public string IdempotencyKeyHash { get; set; } = string.Empty;

    public string RequestHash { get; set; } = string.Empty;

    public string ActorId { get; set; } = string.Empty;

    public Guid DocumentId { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public DateTimeOffset RecordedAtUtc { get; set; }
}
