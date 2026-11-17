using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;

namespace GoymenERP.Modules.Documents.Application;

/// <summary>
/// F-026 (PLT-S9): the generated-document port. A producing module hands over the PDF it rendered (its own generator,
/// embedded Noto Sans — OPEN-019) with the template snapshot; the vault keeps it as a new logical document linked to the
/// producer's owner record. The single link written here is the document's first and therefore primary link (KLN-D49
/// once it lands); further links go through <see cref="LinkAsync"/>.
/// KLN-D59: the authority re-validated here is the producing command's own permission, which the owner of the kind
/// declared (<see cref="IDocumentOwnerLinkPortV1.GeneratedDocumentProducerPermissions"/>); <c>documents.files.upload</c>
/// is not checked and no new permission is opened. The context actor must still hold that permission company-wide now,
/// so the port cannot be used outside a command that already passed its own permission check. The envelope and the
/// same-save audit carry the initiating user, the producer permission, the producing command and the document.
/// KLN-D60: the server-generated PDF is not malware-scanned; it is stored as <see cref="DocumentScanState.SystemGenerated"/>
/// with its SHA-256, after the unchanged PDF allowlist, signature and size checks. This path is the only one that sets
/// the marker (the store refuses it on uploads and versions).
/// </summary>
public sealed partial class DocumentService : IGeneratedDocumentStorePortV1
{
    private const int MaximumMetadataLength = 128;

    public async Task<GeneratedDocumentStoreResultV1> StoreAsync(GeneratedDocumentStoreRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await StoreGeneratedCoreAsync(request, cancellationToken);
        }
        catch (DocumentServiceException exception)
        {
            // A refusal is an outcome for the producing module; cancellation and broken invariants still propagate.
            return GeneratedDocumentStoreResultV1.Refused(GeneratedOutcome(exception), exception.Code);
        }
    }

    private async Task<GeneratedDocumentStoreResultV1> StoreGeneratedCoreAsync(GeneratedDocumentStoreRequestV1 request,
        CancellationToken cancellationToken)
    {
        // The owner kind is resolved first: only its owner declares which producing permissions may store here.
        var owner = RequireOwnerLink(request.OwnerKind, request.OwnerId);
        var ownerPort = owners[owner.Kind];
        if (!ownerPort.AcceptsGeneratedDocuments)
        {
            throw new DocumentServiceException(422, GeneratedDocumentStoreCodesV1.OwnerNotAccepted,
                [new("ownerKind", GeneratedDocumentStoreCodesV1.OwnerNotAccepted)]);
        }

        // KLN-D59: the producing command's own permission, never documents.files.upload; an undeclared one is refused
        // before any authority is asked, and the declared one is re-validated fresh for the context actor below.
        if (request.ProducerPermission is not { } permission || string.IsNullOrWhiteSpace(permission)
            || !string.Equals(permission, permission.Trim(), StringComparison.Ordinal)
            || ownerPort.GeneratedDocumentProducerPermissions is not { } declared || !declared.Contains(permission, StringComparer.Ordinal))
        {
            throw new DocumentServiceException(403, GeneratedDocumentStoreCodesV1.ProducerNotDeclared,
                [new("producerPermission", GeneratedDocumentStoreCodesV1.ProducerNotDeclared)]);
        }

        var scope = await RequireAsync(permission, cancellationToken);
        if (request.CompanyId == Guid.Empty || request.CompanyId != scope.CompanyId)
        {
            // The caller's company is its own measured context, never a choice: another company is refused, not followed.
            throw new DocumentServiceException(403, "forbidden");
        }

        var metadata = RequireGeneratedMetadata(request);
        var publication = PublicationSnapshot(request.Publication);
        if (request.Publication is not null && publication is null)
            throw new DocumentServiceException(400, DocumentProblemCodesV1.ValidationFailed);
        if (publication is not null && publication.CompanyId != scope.CompanyId)
            throw new DocumentServiceException(403, "forbidden");
        var template = GeneratedDocumentTemplate.TryCreate(request.TemplateCode, request.TemplateVersion, publication)
            ?? throw new DocumentServiceException(400, DocumentProblemCodesV1.ValidationFailed,
                [new("template", GeneratedDocumentStoreCodesV1.TemplateInvalid)]);
        var producer = GeneratedDocumentProducerCommand.TryCreate(request.ProducerCommand)
            ?? throw new DocumentServiceException(400, DocumentProblemCodesV1.ValidationFailed,
                [new("producerCommand", GeneratedDocumentStoreCodesV1.ProducerCommandInvalid)]);

        var fileName = DocumentFileName.TryCreate(request.FileName)
            ?? throw new DocumentServiceException(400, DocumentProblemCodesV1.ValidationFailed,
                [new("fileName", DocumentRuleCodes.FileNameInvalid)]);
        var content = request.Pdf ?? [];
        var pdf = DocumentContentPolicy.Info(DocumentFileKind.Pdf);
        var decision = DocumentContentPolicy.Classify(fileName, pdf.ContentType, content, bindings.MaximumUploadBytes);
        if (!decision.IsAccepted)
        {
            throw new DocumentServiceException(422, decision.RefusalCode!, [new("pdf", decision.RefusalCode!)]);
        }

        var kind = decision.Kind!;
        if (kind.Kind != DocumentFileKind.Pdf)
        {
            throw new DocumentServiceException(422, GeneratedDocumentStoreCodesV1.NotPdf, [new("pdf", GeneratedDocumentStoreCodesV1.NotPdf)]);
        }

        var sha256 = DocumentContentHash.Compute(content);
        var requestHash = GeneratedRequestHash(owner, fileName, kind, sha256, template, permission, producer);
        var envelope = Envelope(scope, metadata, permission);

        DocumentStoreResult? replay;
        try
        {
            replay = await GuardedAsync(scope, permission, () => store.FindReplayAsync(envelope, requestHash, DocumentCommands.StoreGeneratedV1,
                cancellationToken), cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        if (replay is not null)
        {
            return await AnswerGeneratedAsync(replay, scope, permission, cancellationToken);
        }

        if (!IsReadableOwner(await GuardedAsync(scope, permission,
                () => CheckOwnerAsync(owner.Kind, owner.OwnerId, cancellationToken), cancellationToken)))
        {
            throw new DocumentServiceException(404, "not_found");
        }

        // KLN-D60: no malware scan for a server-generated PDF; the type, signature and size checks above stay.
        var objectKey = DocumentObjectKeys.For(scope.CompanyId, sha256);
        var objectStore = objects ?? throw new DocumentServiceException(503, "service_unavailable");
        try
        {
            await objectStore.PutAsync(objectKey, content, sha256, cancellationToken);
        }
        catch (DocumentObjectStoreUnavailableException)
        {
            throw new DocumentServiceException(503, "service_unavailable");
        }

        await RequireStillAsync(scope, permission, cancellationToken);
        DocumentStoreResult stored;
        try
        {
            stored = await store.StoreUploadAsync(envelope, requestHash, new NewDocumentUpload(Guid.NewGuid(), owner, fileName, kind,
                content.LongLength, sha256, objectKey, DocumentScanState.SystemGenerated, null, timeProvider.GetUtcNow(), template,
                producer.Value), token => RequireWritableOwnerStillAsync(scope, permission, owner, token),
                cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        return stored.IsReplay
            ? await AnswerGeneratedAsync(stored, scope, permission, cancellationToken)
            : GeneratedAnswer(stored, scope.CompanyId);
    }

    /// <summary>A replayed attempt is answered only while its document is of the context company and its owner is readable now.</summary>
    private async Task<GeneratedDocumentStoreResultV1> AnswerGeneratedAsync(DocumentStoreResult result, Scope scope, string permission,
        CancellationToken cancellationToken)
    {
        var document = result.Document;
        if (document.CompanyId != scope.CompanyId
            || !IsReadableOwner(await GuardedAsync(scope, permission,
                () => CheckOwnerAsync(document.OwnerKind, document.OwnerId, cancellationToken), cancellationToken)))
        {
            throw new DocumentServiceException(404, "not_found");
        }

        return GeneratedAnswer(result, scope.CompanyId);
    }

    private static GeneratedDocumentStoreResultV1 GeneratedAnswer(DocumentStoreResult result, Guid companyId)
    {
        var view = View(result.Document, companyId);
        return new GeneratedDocumentStoreResultV1(
            result.IsReplay ? GeneratedDocumentStoreOutcomeV1.Replayed : GeneratedDocumentStoreOutcomeV1.Stored,
            view.Id, view.VersionNo, view.Sha256, view.ScanState, view.Downloadable, result.OriginalCorrelationId, null);
    }

    /// <summary>The route's metadata rules, re-checked here because an in-process caller has no route in front of it.</summary>
    private static DocumentCommandMetadata RequireGeneratedMetadata(GeneratedDocumentStoreRequestV1 request)
    {
        var key = request.IdempotencyKey;
        var correlationId = request.CorrelationId;
        var causationId = request.CausationId;
        if (string.IsNullOrWhiteSpace(key) || !string.Equals(key, key.Trim(), StringComparison.Ordinal)
            || key.Length > MaximumMetadataLength || key.Contains(',', StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(correlationId) || correlationId.Trim().Length > MaximumMetadataLength
            || (causationId is not null && (string.IsNullOrWhiteSpace(causationId) || causationId.Length > MaximumMetadataLength)))
        {
            throw new DocumentServiceException(400, DocumentProblemCodesV1.CommandMetadataInvalid,
                [new("Idempotency-Key", DocumentProblemCodesV1.CommandMetadataInvalid)]);
        }

        return new DocumentCommandMetadata(key, correlationId, causationId);
    }

    private static GeneratedDocumentStoreOutcomeV1 GeneratedOutcome(DocumentServiceException exception) => exception.Status switch
    {
        400 => GeneratedDocumentStoreOutcomeV1.InvalidRequest,
        401 => GeneratedDocumentStoreOutcomeV1.Unauthenticated,
        403 => GeneratedDocumentStoreOutcomeV1.Forbidden,
        404 => GeneratedDocumentStoreOutcomeV1.OwnerNotFound,
        409 when exception.Code == DocumentProblemCodesV1.SameKeyPayloadMismatch => GeneratedDocumentStoreOutcomeV1.IdempotencyConflict,
        409 => GeneratedDocumentStoreOutcomeV1.ConcurrentChange,
        422 when exception.Code == DocumentProblemCodesV1.ContentRejected => GeneratedDocumentStoreOutcomeV1.ContentRejected,
        422 when exception.Code == GeneratedDocumentStoreCodesV1.OwnerNotAccepted => GeneratedDocumentStoreOutcomeV1.OwnerNotAccepted,
        422 => GeneratedDocumentStoreOutcomeV1.InvalidRequest,
        _ => GeneratedDocumentStoreOutcomeV1.Unavailable,
    };

    /// <summary>
    /// The template snapshot and the producer (KLN-D59 permission and command) are part of the request: the same key with
    /// another template or producer is another request (ADR-011).
    /// </summary>
    private static string GeneratedRequestHash(DocumentOwnerLink owner, DocumentFileName fileName, DocumentFileKindInfo kind, string sha256,
        GeneratedDocumentTemplate template, string producerPermission, GeneratedDocumentProducerCommand producer)
    {
        // Preserve the exact old input for legacy/null publications.
        var canonical = string.Join('\n', DocumentCommands.StoreGeneratedV1, owner.Kind, owner.OwnerId.ToString("D"), fileName.Value,
            kind.ContentType, sha256, template.Code, template.Version, producerPermission, producer.Value);
        if (template.Publication is { } publication) canonical += "\npublication:" + JsonSerializer.Serialize(publication);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static GeneratedDocumentPublicationSnapshot? PublicationSnapshot(DocumentTemplatePublicationV1? value)
    {
        if (value is null) return null;
        static bool Parse(string text, out long number) => long.TryParse(text, System.Globalization.NumberStyles.None,
            CultureInfo.InvariantCulture, out number) && number > 0 && number.ToString(CultureInfo.InvariantCulture) == text;
        if (!DocumentBrandingContractV1.IsKnown(value.Module, value.TemplateCode) || !Parse(value.Version, out var publication)
            || !Parse(value.DraftVersion, out var draft) || !Parse(value.BrandingVersion, out var branding)
            || value.TemplateVersion != "pub." + value.Version || value.PreparedBy == value.PublishedBy
            || value.Logo is { } logo && logo.CompanyId != value.CompanyId
            || value.Letterhead is { } letterhead && letterhead.CompanyId != value.CompanyId) return null;
        var result = new GeneratedDocumentPublicationSnapshot(value.CompanyId, value.PublicationId, (int)value.Module,
            value.TemplateCode, publication, draft, branding,
            value.Logo is null ? null : new(value.Logo.DocumentId, value.Logo.VersionNo, value.Logo.Sha256),
            value.Letterhead is null ? null : new(value.Letterhead.DocumentId, value.Letterhead.VersionNo, value.Letterhead.Sha256));
        return result.IsWellFormed() ? result : null;
    }
}
