using System.Security.Cryptography;
using System.Text;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;

namespace GoymenERP.Modules.Documents.Application;

/// <summary>
/// F-026 (PLT-S9): the generated-document port. A producing module hands over the PDF it rendered (its own generator,
/// embedded Noto Sans — OPEN-019) with the template snapshot; the vault keeps it as a new logical document linked to the
/// producer's owner record, with the same fences as an upload. The single link written here is the document's first
/// and therefore primary link (KLN-D49 once it lands); further links go through <see cref="LinkAsync"/>.
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
        // ASSUMPTION (NEEDS-SINAN raised): the vault's own upload name gates a generated PDF too; no new name is chosen here.
        const string Permission = DocumentsPermissionManifestV1.FilesUpload;
        var scope = await RequireAsync(Permission, cancellationToken);
        if (request.CompanyId == Guid.Empty || request.CompanyId != scope.CompanyId)
        {
            // The caller's company is its own measured context, never a choice: another company is refused, not followed.
            throw new DocumentServiceException(403, "forbidden");
        }

        var metadata = RequireGeneratedMetadata(request);
        var template = GeneratedDocumentTemplate.TryCreate(request.TemplateCode, request.TemplateVersion)
            ?? throw new DocumentServiceException(400, DocumentProblemCodesV1.ValidationFailed,
                [new("template", GeneratedDocumentStoreCodesV1.TemplateInvalid)]);
        var owner = RequireOwnerLink(request.OwnerKind, request.OwnerId);
        if (!owners[owner.Kind].AcceptsGeneratedDocuments)
        {
            throw new DocumentServiceException(422, GeneratedDocumentStoreCodesV1.OwnerNotAccepted,
                [new("ownerKind", GeneratedDocumentStoreCodesV1.OwnerNotAccepted)]);
        }

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
        var requestHash = GeneratedRequestHash(owner, fileName, kind, sha256, template);
        var envelope = Envelope(scope, metadata, Permission);

        DocumentStoreResult? replay;
        try
        {
            replay = await GuardedAsync(scope, Permission, () => store.FindReplayAsync(envelope, requestHash, DocumentCommands.StoreGeneratedV1,
                cancellationToken), cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        if (replay is not null)
        {
            return await AnswerGeneratedAsync(replay, scope, Permission, cancellationToken);
        }

        if (!IsReadableOwner(await GuardedAsync(scope, Permission,
                () => CheckOwnerAsync(owner.Kind, owner.OwnerId, cancellationToken), cancellationToken)))
        {
            throw new DocumentServiceException(404, "not_found");
        }

        var verdict = await ScanAsync(content, kind.Kind, cancellationToken);
        await RequireOwnerStillReadableAsync(scope, Permission, owner, cancellationToken);
        if (verdict == DocumentScanVerdictV1.Infected)
        {
            // Nothing is stored. A generated PDF is scanned like any upload (DEC-DOC-009); trusting the producer is not chosen here.
            throw new DocumentServiceException(422, DocumentProblemCodesV1.ContentRejected, [new("pdf", DocumentProblemCodesV1.ContentRejected)]);
        }

        var scanState = verdict == DocumentScanVerdictV1.Clean ? DocumentScanState.Clean : DocumentScanState.NotScanned;
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

        await RequireStillAsync(scope, Permission, cancellationToken);
        DocumentStoreResult stored;
        try
        {
            stored = await store.StoreUploadAsync(envelope, requestHash, new NewDocumentUpload(Guid.NewGuid(), owner, fileName, kind,
                content.LongLength, sha256, objectKey, scanState, scanState == DocumentScanState.Clean ? scanner!.ScannerId : null,
                timeProvider.GetUtcNow(), template), token => RequireWritableOwnerStillAsync(scope, Permission, owner, token),
                cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        return stored.IsReplay
            ? await AnswerGeneratedAsync(stored, scope, Permission, cancellationToken)
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

    /// <summary>The template snapshot is part of the request: the same key with another template is another request (ADR-011).</summary>
    private static string GeneratedRequestHash(DocumentOwnerLink owner, DocumentFileName fileName, DocumentFileKindInfo kind, string sha256,
        GeneratedDocumentTemplate template) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', DocumentCommands.StoreGeneratedV1, owner.Kind,
            owner.OwnerId.ToString("D"), fileName.Value, kind.ContentType, sha256, template.Code, template.Version))));
}
