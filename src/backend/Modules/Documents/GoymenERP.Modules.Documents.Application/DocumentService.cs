using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;

namespace GoymenERP.Modules.Documents.Application;

/// <summary>
/// The vault's use cases (ERP-000G core, AT-065 first owner: the Cari party). The route binds nothing but the server
/// context and the wire; this service decides, with the Logistics service's fences:
/// <list type="bullet">
/// <item><b>Authority</b>: a fresh central measurement of the vault's own name (upload or read) before anything is read,
/// after every awaited owner/store read and right before the single save; none after the commit.</item>
/// <item><b>Owner</b>: the linked record's owner answers, through its declared port, whether the record exists in the
/// context company and the actor may read it. Anything else is 404 (non-disclosure) — a document is never more
/// visible than its owner record, and another company's document is not found.</item>
/// <item><b>Content</b> (DEC-DOC-009): a safe file name, an allowlisted kind proven by the bytes' signature, a
/// configured size bound; then the malware scan. Malware is refused and nothing is stored; no scanner, or a scanner
/// that cannot answer, stores the version as NotScanned — never served.</item>
/// <item><b>Replay</b> (ADR-011): the attempt of (company, command, key hash) answers first; another body or actor under
/// the same key is one 409.</item>
/// <item><b>Duplicates</b> (AT-057, DEC-DOC-002): the same content in the same company is a warning, never a refusal —
/// the new document is stored (the object once), the earlier documents the actor may read are listed, and the count
/// found at upload time is in the audit event. No retention or deletion exists (OPEN-018/021): nothing is removed.</item>
/// </list>
/// </summary>
public sealed class DocumentService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    ICorrelationContext correlation,
    IEnumerable<IDocumentOwnerLinkPortV1> ownerPorts,
    IDocumentStore store,
    IDocumentObjectStoreV1? objects,
    IDocumentContentScannerV1? scanner,
    DocumentsServiceBindings bindings,
    TimeProvider timeProvider) : IDocumentService
{
    private const int ListLimit = 200;
    private const int DuplicateLimit = 10;
    private readonly IReadOnlyDictionary<string, IDocumentOwnerLinkPortV1> owners = IndexOwners(ownerPorts);

    public async Task<DocumentUploadResponse> UploadAsync(UploadDocumentRequest request, DocumentCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        const string Permission = DocumentsPermissionManifestV1.FilesUpload;
        var scope = await RequireAsync(Permission, cancellationToken);

        var owner = RequireOwnerLink(request.OwnerKind, request.OwnerId);
        var fileName = DocumentFileName.TryCreate(request.FileName)
            ?? throw new DocumentServiceException(400, DocumentProblemCodesV1.ValidationFailed, [new("file", DocumentRuleCodes.FileNameInvalid)]);
        var content = request.Content ?? [];
        var decision = DocumentContentPolicy.Classify(fileName, request.DeclaredContentType, content, bindings.MaximumUploadBytes);
        if (!decision.IsAccepted)
        {
            throw new DocumentServiceException(422, decision.RefusalCode!, [new("file", decision.RefusalCode!)]);
        }

        var kind = decision.Kind!;
        var sha256 = DocumentContentHash.Compute(content);
        var requestHash = RequestHash(owner, fileName, kind, sha256);
        var envelope = Envelope(scope, metadata, Permission);

        DocumentStoreResult? replay;
        try
        {
            replay = await GuardedAsync(scope, Permission, () => store.FindReplayAsync(envelope, requestHash, DocumentCommands.UploadV1,
                cancellationToken), cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        if (replay is not null)
        {
            return await AnswerAsync(replay, scope, Permission, requireReplayFence: true, cancellationToken);
        }

        if (!IsReadableOwner(await GuardedAsync(scope, Permission,
                () => CheckOwnerAsync(owner.Kind, owner.OwnerId, cancellationToken), cancellationToken)))
        {
            throw new DocumentServiceException(404, "not_found");
        }

        var verdict = await ScanAsync(content, kind.Kind, cancellationToken);
        if (verdict == DocumentScanVerdictV1.Infected)
        {
            // Nothing is stored: not the object, not a version, not an attempt. The same key may be retried with other bytes.
            throw new DocumentServiceException(422, DocumentProblemCodesV1.ContentRejected, [new("file", DocumentProblemCodesV1.ContentRejected)]);
        }

        var scanState = verdict == DocumentScanVerdictV1.Clean ? DocumentScanState.Clean : DocumentScanState.NotScanned;
        var objectKey = DocumentObjectKeys.For(scope.CompanyId, sha256);
        var objectStore = objects ?? throw new DocumentServiceException(503, "service_unavailable");
        try
        {
            // The object first, then the metadata: a failed save leaves at most an unreferenced, immutable,
            // content-addressed object of this company (reconciliation is ERP-000N's), never a row without its bytes.
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
                timeProvider.GetUtcNow()), token => RequireWritableOwnerStillAsync(scope, Permission, owner, token), cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        return await AnswerAsync(stored, scope, Permission, stored.IsReplay, cancellationToken);
    }

    public async Task<DocumentListResponse> ListAsync(string? ownerKind, Guid ownerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string Permission = DocumentsPermissionManifestV1.FilesRead;
        var scope = await RequireAsync(Permission, cancellationToken);
        var owner = RequireOwnerLink(ownerKind, ownerId);
        if (!IsReadableOwner(await GuardedAsync(scope, Permission,
                () => CheckOwnerAsync(owner.Kind, owner.OwnerId, cancellationToken), cancellationToken)))
        {
            throw new DocumentServiceException(404, "not_found");
        }

        IReadOnlyList<StoredDocument> documents;
        try
        {
            documents = await GuardedAsync(scope, Permission, () => store.ListByOwnerAsync(owner.Kind, owner.OwnerId, ListLimit, cancellationToken),
                cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        // Whether the upload is offered: the upload name measured now, for this company (a measurement, not a grant).
        var upload = await authority.RevalidatePermissionScopeAsync(DocumentsPermissionManifestV1.FilesUpload, scope.CompanyId, cancellationToken);
        var canUpload = upload.IsGranted && upload.IsCompanyWide && Matches(scope.CompanyId, scope.ActorId);
        await RequireStillAsync(scope, Permission, cancellationToken);
        return new DocumentListResponse([.. documents.Select(document => View(document, scope.CompanyId))], canUpload, Limits());
    }

    public async Task<DocumentContentV1> GetContentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string Permission = DocumentsPermissionManifestV1.FilesRead;
        var scope = await RequireAsync(Permission, cancellationToken);
        if (documentId == Guid.Empty)
        {
            throw new DocumentServiceException(404, "not_found");
        }

        StoredDocument? document;
        try
        {
            document = await GuardedAsync(scope, Permission, () => store.FindDocumentAsync(documentId, cancellationToken), cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        // Another company's document is not found (the tenant filter); a document of this company whose owner record the
        // actor may not read is not found either — the same answer, so neither is disclosed.
        if (document is null || document.CompanyId != scope.CompanyId
            || !IsReadableOwner(await GuardedAsync(scope, Permission,
                () => CheckOwnerAsync(document.OwnerKind, document.OwnerId, cancellationToken), cancellationToken)))
        {
            throw new DocumentServiceException(404, "not_found");
        }

        if (document.ScanState != DocumentScanState.Clean)
        {
            throw new DocumentServiceException(409, DocumentProblemCodesV1.NotScanned, [new("document", DocumentProblemCodesV1.NotScanned)]);
        }

        var objectStore = objects ?? throw new DocumentServiceException(503, "service_unavailable");
        byte[]? content;
        try
        {
            content = await objectStore.ReadAsync(document.ObjectKey, cancellationToken);
        }
        catch (DocumentObjectStoreUnavailableException)
        {
            throw new DocumentServiceException(503, "service_unavailable");
        }

        // The bytes served are the bytes recorded: length and SHA-256 re-checked, else nothing is served.
        if (content is null || content.LongLength != document.SizeBytes
            || !string.Equals(DocumentContentHash.Compute(content), document.Sha256, StringComparison.Ordinal))
        {
            throw new DocumentServiceException(503, "service_unavailable");
        }

        await RequireStillAsync(scope, Permission, cancellationToken);
        return new DocumentContentV1(document.Id, document.FileName, DocumentContentPolicy.Info(document.Kind).ContentType, content, document.Sha256);
    }

    // ---- answers --------------------------------------------------------------------------------

    private async Task<DocumentUploadResponse> AnswerAsync(DocumentStoreResult result, Scope scope, string permission,
        bool requireReplayFence, CancellationToken cancellationToken)
    {
        var document = result.Document;
        if (requireReplayFence && (document.CompanyId != scope.CompanyId
            || !IsReadableOwner(await GuardedAsync(scope, permission,
                () => CheckOwnerAsync(document.OwnerKind, document.OwnerId, cancellationToken), cancellationToken))))
        {
            throw new DocumentServiceException(404, "not_found");
        }

        IReadOnlyList<StoredDocument> sameContent;
        try
        {
            sameContent = requireReplayFence
                ? await GuardedAsync(scope, permission,
                    () => store.FindByContentAsync(document.Sha256, document.Id, DuplicateLimit, cancellationToken), cancellationToken)
                : await store.FindByContentAsync(document.Sha256, document.Id, DuplicateLimit, cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        // Only the duplicates whose owner record the actor may read are named (DEC-DOC-002: authority is judged apart).
        var readable = new Dictionary<(string, Guid), bool>();
        var duplicates = new List<DocumentDuplicateView>();
        foreach (var candidate in sameContent)
        {
            var key = (candidate.OwnerKind, candidate.OwnerId);
            if (!readable.TryGetValue(key, out var isReadable))
            {
                isReadable = IsReadableOwner(requireReplayFence
                    ? await GuardedAsync(scope, permission,
                        () => CheckOwnerAsync(candidate.OwnerKind, candidate.OwnerId, cancellationToken), cancellationToken)
                    : await CheckOwnerAsync(candidate.OwnerKind, candidate.OwnerId, cancellationToken));
                readable[key] = isReadable;
            }

            if (isReadable)
            {
                duplicates.Add(new DocumentDuplicateView(candidate.Id, candidate.FileName, candidate.OwnerKind, candidate.OwnerId,
                    candidate.UploadedAtUtc));
            }
        }

        if (requireReplayFence)
        {
            if (!IsReadableOwner(await GuardedAsync(scope, permission,
                    () => CheckOwnerAsync(document.OwnerKind, document.OwnerId, cancellationToken), cancellationToken)))
            {
                throw new DocumentServiceException(404, "not_found");
            }
        }

        return new DocumentUploadResponse(View(document, scope.CompanyId), duplicates, result.IsReplay, result.OriginalCorrelationId);
    }

    private static DocumentView View(StoredDocument document, Guid companyId)
    {
        if (document.CompanyId != companyId)
        {
            throw new InvalidOperationException("Belge okuması şirket bağlamı dışında kayıt döndü.");
        }

        return new DocumentView(document.Id, document.CompanyId, document.OwnerKind, document.OwnerId, document.VersionNo, document.FileName,
            DocumentContentPolicy.Info(document.Kind).ContentType, document.SizeBytes, document.Sha256, document.ScanState.ToString(),
            document.ScanState == DocumentScanState.Clean, document.UploadedBy, document.UploadedAtUtc);
    }

    private DocumentUploadLimitsView Limits() => new(bindings.MaximumUploadBytes,
        [.. DocumentContentPolicy.AllowedKinds.SelectMany(kind => kind.Extensions)],
        [.. DocumentContentPolicy.AllowedKinds.Select(kind => kind.ContentType)]);

    // ---- owner, scan ----------------------------------------------------------------------------

    private DocumentOwnerLink RequireOwnerLink(string? kind, Guid ownerId)
    {
        if (DocumentOwnerLink.TryCreate(kind, ownerId) is not { } link || !owners.ContainsKey(link.Kind))
        {
            throw new DocumentServiceException(400, DocumentProblemCodesV1.ValidationFailed,
                [new(kind is null || !owners.ContainsKey(kind) ? "ownerKind" : "ownerId", DocumentProblemCodesV1.ValidationFailed)]);
        }

        return link;
    }

    private async Task<DocumentOwnerAccessV1> CheckOwnerAsync(string kind, Guid ownerId, CancellationToken cancellationToken)
    {
        if (!owners.TryGetValue(kind, out var port))
        {
            // A stored link whose owner port is gone: nothing is served on it.
            return DocumentOwnerAccessV1.NotFound;
        }

        try
        {
            return await port.CheckReadableAsync(ownerId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return DocumentOwnerAccessV1.Denied;
        }
        catch
        {
            return DocumentOwnerAccessV1.Unavailable;
        }
    }

    private static bool IsReadableOwner(DocumentOwnerAccessV1 access) => access switch
    {
        DocumentOwnerAccessV1.Readable => true,
        DocumentOwnerAccessV1.NotFound or DocumentOwnerAccessV1.Denied => false,
        DocumentOwnerAccessV1.Conflict => throw new DocumentServiceException(409, DocumentProblemCodesV1.ConcurrentChange,
            [new("owner", DocumentProblemCodesV1.ConcurrentChange)]),
        DocumentOwnerAccessV1.Unavailable => throw new DocumentServiceException(503, "service_unavailable"),
        _ => throw new DocumentServiceException(503, "service_unavailable"),
    };

    private async Task<DocumentScanVerdictV1> ScanAsync(byte[] content, DocumentFileKind kind, CancellationToken cancellationToken)
    {
        if (scanner is null)
        {
            return DocumentScanVerdictV1.Unavailable;
        }

        try
        {
            var verdict = await scanner.ScanAsync(content, kind, cancellationToken);
            return Enum.IsDefined(verdict) ? verdict : DocumentScanVerdictV1.Unavailable;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // A scanner that fails did not say "clean".
            return DocumentScanVerdictV1.Unavailable;
        }
    }

    private static string RequestHash(DocumentOwnerLink owner, DocumentFileName fileName, DocumentFileKindInfo kind, string sha256) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', DocumentCommands.UploadV1, owner.Kind,
            owner.OwnerId.ToString("D"), fileName.Value, kind.ContentType, sha256))));

    private static IReadOnlyDictionary<string, IDocumentOwnerLinkPortV1> IndexOwners(IEnumerable<IDocumentOwnerLinkPortV1> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        var index = new Dictionary<string, IDocumentOwnerLinkPortV1>(StringComparer.Ordinal);
        foreach (var port in ports)
        {
            if (DocumentOwnerLink.TryCreate(port.Kind, Guid.NewGuid()) is null || !index.TryAdd(port.Kind, port))
            {
                throw new InvalidOperationException($"Belge bağlantı türü geçersiz veya iki kez kayıtlı: {port.Kind}");
            }
        }

        return index;
    }

    private static DocumentServiceException Store(DocumentStoreException exception) => exception.Code switch
    {
        DocumentStoreCodes.SameKeyPayloadMismatch => new DocumentServiceException(409, DocumentProblemCodesV1.SameKeyPayloadMismatch,
            [new("Idempotency-Key", DocumentProblemCodesV1.SameKeyPayloadMismatch)]),
        DocumentStoreCodes.ConcurrentChange => new DocumentServiceException(409, DocumentProblemCodesV1.ConcurrentChange,
            [new("document", DocumentProblemCodesV1.ConcurrentChange)]),
        DocumentStoreCodes.CompanyContextMismatch => new DocumentServiceException(403, "forbidden"),
        _ => new DocumentServiceException(503, "service_unavailable"),
    };

    // ---- authority ------------------------------------------------------------------------------

    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);

    private async Task<Scope> RequireAsync(string permission, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new DocumentServiceException(401, "unauthenticated");
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new DocumentServiceException(403, "forbidden");
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !Matches(companyId, actorId))
        {
            throw new DocumentServiceException(403, "forbidden");
        }

        return new Scope(companyId, actorId, [.. scope.RoleCodes.Order(StringComparer.Ordinal)]);
    }

    private async Task RequireStillAsync(Scope scope, string permission, CancellationToken cancellationToken)
    {
        var current = await RequireAsync(permission, cancellationToken);
        if (current.CompanyId != scope.CompanyId || !string.Equals(current.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw new DocumentServiceException(403, "forbidden");
        }
    }

    private async Task RequireWritableOwnerStillAsync(Scope scope, string permission, DocumentOwnerLink owner,
        CancellationToken cancellationToken)
    {
        if (!IsReadableOwner(await GuardedAsync(scope, permission,
                () => CheckOwnerAsync(owner.Kind, owner.OwnerId, cancellationToken), cancellationToken)))
        {
            throw new DocumentServiceException(404, "not_found");
        }
    }

    private async Task<T> GuardedAsync<T>(Scope scope, string permission, Func<Task<T>> read, CancellationToken cancellationToken)
    {
        T result;
        try
        {
            result = await read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await RequireStillAsync(scope, permission, cancellationToken);
        return result;
    }

    private bool Matches(Guid companyId, string actorId) =>
        currentUser.IsAuthenticated && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId == companyId && currentUser.CanAccessCompany(companyId);

    private DocumentWriteEnvelope Envelope(Scope scope, DocumentCommandMetadata metadata, string permission) =>
        new(scope.ActorId, permission, scope.RoleCodes, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey,
            correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);
}
