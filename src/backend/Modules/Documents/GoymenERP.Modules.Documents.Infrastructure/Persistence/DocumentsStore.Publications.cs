using System.Text.Json;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Documents.Infrastructure.Persistence;

public sealed partial class DocumentsStore
{
    private async Task RequirePublicationAsync(GeneratedDocumentPublicationSnapshot? snapshot, Guid companyId, CancellationToken ct)
    {
        if (snapshot is null) return;
        if (!snapshot.IsWellFormed() || snapshot.CompanyId != companyId) throw InvalidPublication();
        var row = await (from publication in dbContext.TemplatePublications.AsNoTracking()
            join branding in dbContext.BrandingRevisions.AsNoTracking()
                on new { publication.CompanyId, Version = publication.BrandingVersion } equals new { branding.CompanyId, branding.Version }
            where publication.CompanyId == companyId && publication.Id == snapshot.PublicationId
            select new { publication, branding }).SingleOrDefaultAsync(ct);
        if (row is null || row.publication.Module != snapshot.Module || row.publication.TemplateCode != snapshot.TemplateCode
            || row.publication.Version != snapshot.PublicationVersion || row.publication.DraftVersion != snapshot.DraftVersion
            || row.publication.BrandingVersion != snapshot.BrandingVersion) throw InvalidPublication();
        await RequireAssetAsync(snapshot.Logo, row.branding.LogoDocumentId, row.branding.LogoVersionNo);
        await RequireAssetAsync(snapshot.Letterhead, row.branding.LetterheadDocumentId, row.branding.LetterheadVersionNo);

        async Task RequireAssetAsync(GeneratedDocumentAssetSnapshot? asset, Guid? documentId, int? versionNo)
        {
            if (asset is null && documentId is null && versionNo is null) return;
            if (asset is null || asset.DocumentId != documentId || asset.VersionNo != versionNo) throw InvalidPublication();
            var version = await dbContext.Versions.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId
                && x.DocumentId == asset.DocumentId && x.VersionNo == asset.VersionNo, ct);
            if (version is null || version.ScanState != (int)DocumentScanState.Clean || version.Sha256 != asset.Sha256
                || string.IsNullOrWhiteSpace(version.ScannerId)) throw InvalidPublication();
        }
    }

    private static DocumentStoreException InvalidPublication() => new("document_publication_invalid", "Published template facts do not match the Documents owner.");

    private static string AddPublicationSnapshot(string legacyJson, GeneratedDocumentPublicationSnapshot? publication)
    {
        if (publication is null) return legacyJson;
        using var document = JsonDocument.Parse(legacyJson);
        var properties = document.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.Ordinal);
        properties.Add("templatePublication", JsonSerializer.SerializeToElement(publication));
        return JsonSerializer.Serialize(properties, Json);
    }
}
