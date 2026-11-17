namespace GoymenERP.Modules.Documents.Domain;

public sealed record GeneratedDocumentAssetSnapshot(Guid DocumentId, int VersionNo, string Sha256)
{
    public bool IsWellFormed() => DocumentId != Guid.Empty && VersionNo >= 1 && DocumentContentHash.IsWellFormed(Sha256);
}

/// <summary>Immutable material references of the actual published template, retained with each generated vault version.</summary>
public sealed record GeneratedDocumentPublicationSnapshot(Guid CompanyId, Guid PublicationId, int Module, string TemplateCode,
    long PublicationVersion, long DraftVersion, long BrandingVersion, GeneratedDocumentAssetSnapshot? Logo,
    GeneratedDocumentAssetSnapshot? Letterhead)
{
    public bool IsWellFormed() => CompanyId != Guid.Empty && PublicationId != Guid.Empty && Module is >= 1 and <= 3
        && PublicationVersion > 0 && DraftVersion > 0 && BrandingVersion > 0
        && !string.IsNullOrWhiteSpace(TemplateCode) && (Logo is not null || Letterhead is not null)
        && (Logo is null || Logo.IsWellFormed()) && (Letterhead is null || Letterhead.IsWellFormed());
}
