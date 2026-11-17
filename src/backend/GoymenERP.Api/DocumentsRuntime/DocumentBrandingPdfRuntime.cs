using System.Globalization;
using System.Security.Cryptography;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.DocumentGeneration.Pdf;
using GoymenERP.Modules.Documents.Application.Contracts;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.DocumentsRuntime;

public static class DocumentBrandingPdfRuntime
{
    public static IServiceCollection AddDocumentBrandingPdfRuntime(this IServiceCollection services)
    {
        services.TryAddScoped<IDocumentBrandingAssetRenderValidatorV1, DocumentBrandingPdfAssetValidator>();
        services.TryAddScoped<PublishedDocumentBrandingReader>();
        return services;
    }
}

public sealed class DocumentBrandingPdfAssetValidator : IDocumentBrandingAssetRenderValidatorV1
{
    public Task<DocumentBrandingAssetRenderOutcomeV1> ValidateAsync(DocumentBrandingAssetContentV1 asset,
        DocumentBrandingAssetSlotV1 slot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(slot) || asset is null || asset.Proof is null)
            return Task.FromResult(DocumentBrandingAssetRenderOutcomeV1.Unavailable);
        try
        {
            DocumentPdfBrandingValidation.Validate(PublishedDocumentBrandingReader.Asset(asset));
            return Task.FromResult(DocumentBrandingAssetRenderOutcomeV1.Available);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException
            or FormatException or IOException or PdfSharp.Pdf.IO.PdfReaderException)
        { return Task.FromResult(DocumentBrandingAssetRenderOutcomeV1.Invalid); }
    }
}

public sealed record PublishedDocumentBranding(DocumentPdfBranding? Render, DocumentTemplatePublicationV1? Publication);

/// <summary>The producing read/command keeps its original actor and company across all awaited owner calls.</summary>
internal sealed class DocumentPdfProducerFrame(ICompanyContext company, ICurrentUser user)
{
    public Guid CompanyId { get; } = company.IsResolved ? company.CompanyId : Guid.Empty;
    private readonly string? actor = user.UserId;
    public void Require()
    {
        if (CompanyId == Guid.Empty || !company.IsResolved || company.CompanyId != CompanyId || !user.IsAuthenticated
            || string.IsNullOrWhiteSpace(actor) || user.UserId != actor || !user.CanAccessCompany(CompanyId))
            throw new DocumentBrandingServiceException(403, "forbidden");
    }
}

/// <summary>
/// Producer-purpose read only: a publication confers no grant. The producing service must also repeat its record-scope
/// read after this owner call; this fence checks the actual permission and actor/company without inventing a company-wide grant.
/// </summary>
public sealed class PublishedDocumentBrandingReader(IDocumentPublishedTemplateReadPortV1 owner, ICompanyContext company,
    ICurrentUser user, IErpAuthorityRevalidationService authority)
{
    public async Task<PublishedDocumentBranding> ReadAsync(Guid companyId, DocumentTemplateModuleV1 module,
        string permission, CancellationToken cancellationToken = default) =>
        await ReadCoreAsync(companyId, module, permission, null, null, cancellationToken);

    public Task<PublishedDocumentBranding> ReadSelectedAsync(Guid companyId, DocumentTemplateModuleV1 module,
        string permission, Guid publicationId, string publicationVersion, CancellationToken cancellationToken = default) =>
        ReadCoreAsync(companyId, module, permission, publicationId, publicationVersion, cancellationToken);

    private async Task<PublishedDocumentBranding> ReadCoreAsync(Guid companyId, DocumentTemplateModuleV1 module,
        string permission, Guid? selectedId, string? selectedVersion, CancellationToken cancellationToken)
    {
        var code = DocumentBrandingContractV1.TemplateCode(module) ?? throw Unavailable();
        var actor = user.UserId;
        await Fence();
        DocumentPublishedTemplateReadV1 answer;
        try { answer = selectedId is null ? await owner.ReadAsync(companyId, module, code, cancellationToken)
            : await owner.ReadPublicationAsync(companyId, module, code, selectedId.Value, selectedVersion!, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch { await Fence(); throw Unavailable(); }
        await Fence();
        if (answer is null || answer.CompanyId != companyId || answer.Module != module || answer.TemplateCode != code)
            throw Unavailable();
        if (selectedId is null && answer.Outcome == DocumentPublishedTemplateOutcomeV1.NoSource && answer.Publication is null
            && answer.Logo is null && answer.Letterhead is null && answer.UnavailableReason is null)
            return new(null, null);
        var publication = answer.Publication;
        if (answer.Outcome != DocumentPublishedTemplateOutcomeV1.Available || answer.UnavailableReason is not null
            || publication is null || publication.CompanyId != companyId || publication.Module != module || publication.TemplateCode != code
            || publication.PublicationId == Guid.Empty || selectedId is not null && (publication.PublicationId != selectedId || publication.Version != selectedVersion)
            || !Version(publication.Version) || !Version(publication.DraftVersion)
            || !Version(publication.BrandingVersion) || publication.TemplateVersion != "pub." + publication.Version
            || string.IsNullOrWhiteSpace(publication.PreparedBy) || string.IsNullOrWhiteSpace(publication.PublishedBy)
            || publication.PreparedBy == publication.PublishedBy || publication.Logo is null && publication.Letterhead is null)
            throw Unavailable();
        var logo = Match(publication.Logo, answer.Logo); var letterhead = Match(publication.Letterhead, answer.Letterhead);
        return new(new(companyId, publication.PublicationId, code, publication.TemplateVersion, logo, letterhead), publication);

        DocumentPdfBrandingAsset? Match(DocumentBrandingAssetProofV1? proof, DocumentBrandingAssetContentV1? asset)
        {
            if (proof is null && asset is null) return null;
            if (proof is null || asset is null || proof != asset.Proof || proof.CompanyId != companyId) throw Unavailable();
            return Asset(asset);
        }
        async Task Fence()
        {
            Same(); PermissionScope measured;
            try { measured = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch { Same(); throw Unavailable(); }
            Same(); if (!measured.IsGranted) throw new DocumentBrandingServiceException(403, "forbidden");
        }
        void Same()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (companyId == Guid.Empty || !company.IsResolved || company.CompanyId != companyId || !user.IsAuthenticated
                || string.IsNullOrWhiteSpace(actor) || user.UserId != actor || !user.CanAccessCompany(companyId))
                throw new DocumentBrandingServiceException(403, "forbidden");
        }
    }

    internal static DocumentPdfBrandingAsset Asset(DocumentBrandingAssetContentV1 asset)
    {
        var proof = asset.Proof;
        if (proof.CompanyId == Guid.Empty || proof.DocumentId == Guid.Empty || proof.VersionNo < 1 || proof.SizeBytes != asset.Content.Length
            || proof.SizeBytes < 1 || proof.Sha256 is not { Length: 64 } || !proof.Sha256.All(char.IsAsciiHexDigit)
            || string.IsNullOrWhiteSpace(proof.ScannerId) || string.IsNullOrWhiteSpace(proof.UploadedBy)
            || proof.UploadedAtUtc.Offset != TimeSpan.Zero || proof.ContentType is not ("image/png" or "image/jpeg" or "application/pdf")
            || !string.Equals(proof.Sha256, Convert.ToHexString(SHA256.HashData(asset.Content.Span)), StringComparison.OrdinalIgnoreCase))
            throw Unavailable();
        return new(proof.DocumentId, proof.VersionNo, proof.ContentType, proof.Sha256, asset.Content);
    }
    private static bool Version(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
        && version > 0 && value == version.ToString(CultureInfo.InvariantCulture);
    private static DocumentBrandingServiceException Unavailable() => new(503, "service_unavailable");
}
