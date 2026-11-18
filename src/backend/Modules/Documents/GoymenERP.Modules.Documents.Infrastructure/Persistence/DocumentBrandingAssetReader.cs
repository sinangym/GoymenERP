using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Documents.Infrastructure.Persistence;

/// <summary>Only Documents tables and the vault's immutable object port; no current-version or generated-file alias.</summary>
internal sealed class DocumentBrandingAssetReader(ICompanyContext company, IDocumentObjectStoreV1? objects,
    DocumentsServiceBindings bindings)
{
    public async Task<DocumentBrandingAssetContentV1?> ReadAsync(DocumentsDbContext db, Guid companyId,
        Guid? documentId, int? versionNo, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        RequireCompany(companyId);
        if (documentId is null && versionNo is null) return null;
        if (documentId is null || documentId == Guid.Empty || versionNo is not >= 1) throw Invalid();
        var source = await (from version in db.Versions.AsNoTracking()
            join blob in db.Blobs.AsNoTracking() on new { version.CompanyId, version.Sha256 } equals new { blob.CompanyId, blob.Sha256 }
            where version.CompanyId == companyId && version.DocumentId == documentId && version.VersionNo == versionNo
                && db.Links.Any(link => link.CompanyId == companyId && link.DocumentId == version.DocumentId
                    && link.OwnerKind == DocumentBrandingContractV1.CompanyOwnerKind && link.OwnerId == companyId)
            select new { version, blob }).SingleOrDefaultAsync(ct);
        RequireCompany(companyId);
        if (source is null) throw Invalid();
        var item = source.version;
        var kind = (DocumentFileKind)source.blob.ContentKind;
        if (item.ScanState != (int)DocumentScanState.Clean || string.IsNullOrWhiteSpace(item.ScannerId))
            throw new DocumentBrandingServiceException(409, "document_branding_asset_not_clean");
        if (!DocumentContentPolicy.StandardKinds.Contains(kind) || !DocumentContentHash.IsWellFormed(item.Sha256)
            || source.blob.SizeBytes <= 0 || source.blob.SizeBytes > bindings.MaximumUploadBytes
            || string.IsNullOrWhiteSpace(item.UploadedBy) || item.ScannerId.Length > 64)
            throw Invalid();
        if (objects is null) throw Unavailable();
        byte[]? bytes;
        try { bytes = await objects.ReadAsync(DocumentObjectKeys.For(companyId, item.Sha256), ct); }
        catch (DocumentObjectStoreUnavailableException) { throw Unavailable(); }
        RequireCompany(companyId);
        if (bytes is null || bytes.LongLength != source.blob.SizeBytes
            || !string.Equals(DocumentContentHash.Compute(bytes), item.Sha256, StringComparison.Ordinal)) throw Unavailable();
        var name = DocumentFileName.TryCreate(item.FileName) ?? throw Invalid();
        var info = DocumentContentPolicy.Info(kind);
        var classification = DocumentContentPolicy.Classify(name, info.ContentType, bytes, bindings.MaximumUploadBytes);
        if (!classification.IsAccepted || classification.Kind!.Kind != kind) throw Invalid();
        return new DocumentBrandingAssetContentV1(new DocumentBrandingAssetProofV1(companyId, item.DocumentId, item.VersionNo, info.ContentType, bytes.LongLength, item.Sha256,
            item.ScannerId, item.UploadedBy, item.UploadedAtUtc), bytes.AsMemory());
    }

    private void RequireCompany(Guid companyId)
    {
        if (companyId == Guid.Empty || !company.IsResolved || company.CompanyId != companyId)
            throw new DocumentBrandingServiceException(403, "forbidden");
    }
    private static DocumentBrandingServiceException Invalid() => new DocumentBrandingServiceException(409, "document_branding_asset_invalid");
    private static DocumentBrandingServiceException Unavailable() => new DocumentBrandingServiceException(503, "service_unavailable");
}
