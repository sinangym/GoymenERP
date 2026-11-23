namespace GoymenERP.Modules.Documents.Infrastructure.Persistence;

internal sealed class CompanyBrandingRevisionRow : IDocumentsTenantRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public Guid? LogoDocumentId { get; set; }
    public int? LogoVersionNo { get; set; }
    public Guid? LetterheadDocumentId { get; set; }
    public int? LetterheadVersionNo { get; set; }
    public string PreparedBy { get; set; } = string.Empty;
    public DateTimeOffset PreparedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
}

internal sealed class DocumentTemplateDraftRow : IDocumentsTenantRow
{
    public Guid CompanyId { get; set; }
    public int Module { get; set; }
    public string TemplateCode { get; set; } = string.Empty;
    public long Version { get; set; }
    public long BrandingVersion { get; set; }
    public string PreparedBy { get; set; } = string.Empty;
    public DateTimeOffset PreparedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
}

internal sealed class DocumentTemplatePublicationRow : IDocumentsTenantRow
{
    public Guid CompanyId { get; set; }
    public int Module { get; set; }
    public string TemplateCode { get; set; } = string.Empty;
    public long Version { get; set; }
    public Guid Id { get; set; }
    public long DraftVersion { get; set; }
    public long BrandingVersion { get; set; }
    public string PreparedBy { get; set; } = string.Empty;
    public string PublishedBy { get; set; } = string.Empty;
    public DateTimeOffset PublishedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Independent of file-upload attempts: template commands need no invented document identity.</summary>
internal sealed class DocumentBrandingAttemptRow : IDocumentsTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public int Status { get; set; }
    public string? Code { get; set; }
    public string? ResultJson { get; set; }
}
